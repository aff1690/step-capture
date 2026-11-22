using System.Runtime.InteropServices;
using static StepCapture.App.Native.NativeMethods;

namespace StepCapture.App.Native;

/// <summary>
/// Installs global low-level mouse and keyboard hooks on a dedicated thread with its own message loop,
/// so input is never delayed by work on the UI thread. Callbacks run on that hook thread and must be quick:
/// Windows silently removes a low-level hook whose callback takes too long.
/// </summary>
internal sealed class InputHookThread : IDisposable
{
    /// <summary>Mouse button pressed: screen X, screen Y (physical pixels) and the button message.</summary>
    public Action<int, int, int>? MouseDown;

    /// <summary>Key pressed: virtual key, scan code, flags. Return true to swallow the key.</summary>
    public Func<int, int, uint, bool>? KeyDown;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private IntPtr _mouseHook, _keyboardHook;
    private LowLevelProc? _mouseProc, _keyboardProc; // kept alive so the GC doesn't collect the delegates
    private Exception? _startError;

    public InputHookThread()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "StepCapture input hooks" };
    }

    public void Start()
    {
        _thread.Start();
        _ready.Wait();
        if (_startError is not null) throw _startError;
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
        IntPtr module = GetModuleHandle(null);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
        {
            _startError = new InvalidOperationException(
                $"Could not install input hooks (Win32 error {Marshal.GetLastWin32Error()}).");
            Unhook();
            _ready.Set();
            return;
        }
        _ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        Unhook();
    }

    private void Unhook()
    {
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = _keyboardHook = IntPtr.Zero;
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                try { MouseDown?.Invoke(data.pt.X, data.pt.Y, msg); }
                catch (Exception ex) { Log.Error("Mouse hook", ex); }
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                try
                {
                    if (KeyDown?.Invoke((int)data.vkCode, (int)data.scanCode, data.flags) == true)
                        return (IntPtr)1;
                }
                catch (Exception ex) { Log.Error("Keyboard hook", ex); }
            }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        if (_thread.IsAlive) _thread.Join(2000);
        _ready.Dispose();
    }
}
