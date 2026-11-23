using System.Collections.Concurrent;
using System.Text;
using System.Windows.Threading;
using StepCapture.App.Imaging;
using StepCapture.App.Native;
using StepCapture.Core.Model;
using StepCapture.Core.Recording;
using static StepCapture.App.Native.NativeMethods;

namespace StepCapture.App.Recording;

/// <summary>
/// Records clicks and keystrokes into guide steps.
///
/// Threads:
///  * Hook thread: receives raw input. On a click (or the first key of a typing run) it grabs the
///    screen immediately, so the screenshot shows the state *before* the app reacts. It does no other work.
///  * Worker thread: asks UI Automation what was clicked, draws the highlight, encodes the PNG and feeds
///    the <see cref="StepBuilder"/>.
///  * UI thread: receives StepAdded/StepUpdated/StepRemoved via the dispatcher.
/// </summary>
internal sealed class Recorder : IDisposable
{
    // Hotkeys (Ctrl+Shift+F8 / F9 / F10) are handled inside the keyboard hook and never recorded.
    public const int ScreenshotHotkeyVk = 0x77; // F8
    public const int PauseHotkeyVk = 0x78; // F9
    public const int StopHotkeyVk = 0x79;  // F10
    public const string ScreenshotHotkeyText = "Ctrl+Shift+F8";
    public const string PauseHotkeyText = "Ctrl+Shift+F9";
    public const string StopHotkeyText = "Ctrl+Shift+F10";

    private abstract record RawEvent(DateTimeOffset Time);
    private sealed record RawClick(DateTimeOffset Time, int X, int Y, MouseButton Button, CapturedScreen Shot) : RawEvent(Time);
    private sealed record RawKey(DateTimeOffset Time, KeyAction Action, CapturedScreen? Shot) : RawEvent(Time);
    private sealed record RawShot(DateTimeOffset Time, CapturedScreen Shot, bool DrawCursor) : RawEvent(Time);
    private sealed record RawFlush(DateTimeOffset Time) : RawEvent(Time);

    private readonly Dispatcher _ui;
    private readonly BlockingCollection<RawEvent> _queue = new();
    private readonly StepBuilder _builder;
    private InputHookThread? _hooks;
    private Thread? _worker;
    private volatile bool _paused;
    private volatile bool _running;
    private volatile bool _typingRun; // set by the hook thread; reset by screenshots too

    public event Action<Step>? StepAdded;
    public event Action<Step>? StepUpdated;
    public event Action<Step>? StepRemoved;
    public event Action? ScreenshotTaken;
    public event Action? PauseHotkeyPressed;
    public event Action? StopHotkeyPressed;

    public Recorder(Dispatcher ui)
    {
        _ui = ui;
        _builder = new StepBuilder(doubleClickMs: (int)GetDoubleClickTime());
        _builder.StepAdded += s => _ui.BeginInvoke(() => StepAdded?.Invoke(s));
        _builder.StepUpdated += s => _ui.BeginInvoke(() => StepUpdated?.Invoke(s));
        _builder.StepRemoved += s => _ui.BeginInvoke(() => StepRemoved?.Invoke(s));
    }

    public bool IsPaused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            if (value)
            {
                _typingRun = false;
                _queue.TryAdd(new RawFlush(DateTimeOffset.Now));
            }
        }
    }

    /// <summary>
    /// Adds a screenshot of the monitor under the cursor as an overview step, without needing a click.
    /// Works while paused too, so you can pause, set the screen up, and take a shot.
    /// </summary>
    /// <param name="fromBar">
    /// True when triggered from the recording bar: the pointer is just on the button, so capture the monitor
    /// showing the app being recorded (the bar never takes focus) and leave the cursor out.
    /// False for the hotkey: capture the monitor under the pointer, with the cursor.
    /// </param>
    public void CaptureScreenshot(bool fromBar)
    {
        if (!_running) return;
        _typingRun = false;
        CapturedScreen shot;
        if (fromBar)
            shot = ScreenCapture.CaptureForegroundMonitor();
        else
        {
            GetCursorPos(out var pt);
            shot = ScreenCapture.CaptureMonitorAt(pt.X, pt.Y);
        }
        bool drawCursor = !fromBar;
        if (_queue.TryAdd(new RawShot(DateTimeOffset.Now, shot, drawCursor)))
            _ui.BeginInvoke(() => ScreenshotTaken?.Invoke());
        else
            shot.Dispose();
    }

    public void Start()
    {
        if (_running) throw new InvalidOperationException("Already recording.");
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "StepCapture recorder" };
        _worker.SetApartmentState(ApartmentState.MTA); // UI Automation client calls should not run on STA
        _worker.Start();

        _hooks = new InputHookThread { MouseDown = OnMouseDown, KeyDown = OnKeyDown };
        _running = true;
        _hooks.Start();
    }

    /// <summary>Stops recording and waits until every captured step has been delivered.</summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _hooks?.Dispose();
        _hooks = null;
        _queue.CompleteAdding();
        _worker?.Join(TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        Stop();
        _queue.Dispose();
    }

    // ---------------- hook thread ----------------

    private void OnMouseDown(int x, int y, int message)
    {
        if (!_running || _paused) return;
        if (IsOwnWindow(WindowFromPoint(new POINT { X = x, Y = y }))) return; // clicks on our toolbar

        var button = message switch
        {
            WM_RBUTTONDOWN => MouseButton.Right,
            WM_MBUTTONDOWN => MouseButton.Middle,
            _ => MouseButton.Left,
        };
        _typingRun = false;
        var shot = ScreenCapture.CaptureMonitorAt(x, y);
        if (!_queue.TryAdd(new RawClick(DateTimeOffset.Now, x, y, button, shot))) shot.Dispose();
    }

    private bool OnKeyDown(int vk, int scanCode, uint flags)
    {
        var mods = CurrentModifiers();

        if (mods == (Modifiers.Ctrl | Modifiers.Shift) && vk == ScreenshotHotkeyVk)
        {
            try { CaptureScreenshot(fromBar: false); }
            catch (Exception ex) { Log.Error("Screenshot hotkey", ex); }
            return true; // swallow the hotkey
        }

        if (mods == (Modifiers.Ctrl | Modifiers.Shift) && vk is PauseHotkeyVk or StopHotkeyVk)
        {
            if (vk == PauseHotkeyVk) _ui.BeginInvoke(() => PauseHotkeyPressed?.Invoke());
            else _ui.BeginInvoke(() => StopHotkeyPressed?.Invoke());
            return true; // swallow the hotkey
        }

        if (!_running || _paused) return false;
        if (IsOwnWindow(GetForegroundWindow())) return false; // typing in StepCapture itself

        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));
        string? text = TranslateKey(vk, scanCode, mods, layout);
        uint mapped = MapVirtualKeyEx((uint)vk, MAPVK_VK_TO_CHAR, layout);
        char? layoutChar = mapped is > 0 and < 0x8000 ? (char)mapped : null;

        var action = KeyClassifier.Classify(vk, mods, text, layoutChar);
        if (action.Kind == KeyActionKind.Ignore) return false;

        CapturedScreen? shot = null;
        bool startsTyping = action.Kind == KeyActionKind.Text && !_typingRun;
        if (startsTyping || action.Kind == KeyActionKind.Press)
            shot = ScreenCapture.CaptureForegroundMonitor();

        if (action.Kind == KeyActionKind.Text) _typingRun = true;
        else if (action.Kind == KeyActionKind.Press) _typingRun = false;

        if (!_queue.TryAdd(new RawKey(DateTimeOffset.Now, action, shot))) shot?.Dispose();
        return false;
    }

    private static Modifiers CurrentModifiers()
    {
        var m = Modifiers.None;
        if (IsKeyDown(VK_SHIFT)) m |= Modifiers.Shift;
        if (IsKeyDown(VK_CONTROL)) m |= Modifiers.Ctrl;
        if (IsKeyDown(VK_MENU)) m |= Modifiers.Alt;
        if (IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN)) m |= Modifiers.Win;
        return m;
    }

    private static readonly byte[] KeyState = new byte[256];
    private static readonly StringBuilder KeyBuffer = new(8);

    /// <summary>The text a key produces in the foreground app's keyboard layout, or null.</summary>
    private static string? TranslateKey(int vk, int scanCode, Modifiers mods, IntPtr layout)
    {
        Array.Clear(KeyState);
        if (mods.HasFlag(Modifiers.Shift)) KeyState[VK_SHIFT] = KeyState[VK_LSHIFT] = 0x80;
        if (mods.HasFlag(Modifiers.Ctrl) && mods.HasFlag(Modifiers.Alt)) // AltGr
        {
            KeyState[VK_CONTROL] = KeyState[VK_LCONTROL] = 0x80;
            KeyState[VK_MENU] = KeyState[VK_RMENU] = 0x80;
        }
        if ((GetKeyState(VK_CAPITAL) & 1) != 0) KeyState[VK_CAPITAL] = 0x01;
        KeyState[vk] = 0x80;

        KeyBuffer.Clear();
        // Flag 0x4: don't change the keyboard state (keeps dead keys working for the user). Windows 10 1607+.
        int n = ToUnicodeEx((uint)vk, (uint)scanCode, KeyState, KeyBuffer, 8, 0x4, layout);
        return n > 0 ? KeyBuffer.ToString(0, Math.Min(n, KeyBuffer.Length)) : null;
    }

    // ---------------- worker thread ----------------

    private void WorkerLoop()
    {
        try
        {
            foreach (var e in _queue.GetConsumingEnumerable())
            {
                try { Process(e); }
                catch (Exception ex) { Log.Error("Processing recorded input", ex); }
            }
            _builder.Flush();
        }
        catch (Exception ex)
        {
            Log.Error("Recorder worker", ex);
        }
    }

    private void Process(RawEvent e)
    {
        switch (e)
        {
            case RawClick c:
                using (c.Shot)
                {
                    var found = ElementInspector.AtPoint(c.X, c.Y);
                    byte[] png = Annotator.RenderClick(c.Shot, c.Shot.ToBitmap(c.X, c.Y));
                    _builder.OnClick(new ClickInput(c.Time, c.X, c.Y, c.Button, found.Info, png));
                }
                break;

            case RawKey k:
                using (k.Shot)
                {
                    ElementInspector.Result focus = default;
                    byte[]? png = null;
                    bool opensTypingStep = k.Action.Kind == KeyActionKind.Text && !_builder.IsTyping;
                    if (k.Shot is not null || opensTypingStep)
                        focus = ElementInspector.Focused();
                    if (k.Shot is not null)
                        png = Annotator.RenderFocus(k.Shot, k.Action.Kind == KeyActionKind.Text ? focus.Bounds : null);
                    _builder.OnKey(new KeyInput(k.Time, k.Action, focus.Info, png));
                }
                break;

            case RawShot shot:
                using (shot.Shot)
                {
                    string? app = ElementInspector.Focused().Info?.AppName; // the app in front, for the caption
                    _builder.OnScreenshot(shot.Time, Annotator.RenderPlain(shot.Shot, shot.DrawCursor), app);
                }
                break;

            case RawFlush:
                _builder.Flush();
                break;
        }
    }
}
