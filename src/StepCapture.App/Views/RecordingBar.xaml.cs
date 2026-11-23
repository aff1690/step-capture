using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using StepCapture.App.Recording;
using static StepCapture.App.Native.NativeMethods;

namespace StepCapture.App.Views;

/// <summary>Small always-on-top bar shown while recording. It is hidden from its own screenshots.</summary>
public partial class RecordingBar : Window
{
    public event Action? ScreenshotClicked;
    public event Action? PauseClicked;
    public event Action? StopClicked;

    public RecordingBar()
    {
        InitializeComponent();
        ToolTip = $"Drag to move. {Recorder.ScreenshotHotkeyText} takes a screenshot, {Recorder.PauseHotkeyText} pauses, {Recorder.StopHotkeyText} stops.";
        ShotButton.ToolTip = $"Add a screenshot of the screen as it is now, without clicking ({Recorder.ScreenshotHotkeyText})";
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + 4;
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        // Don't steal focus from the app being recorded when the bar is clicked.
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_NOACTIVATE));
        // Keep the bar out of screenshots (Windows 10 2004 and later; ignored on older versions).
        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
    }

    public void SetStepCount(int count) => CountText.Text = count == 1 ? "1 step" : $"{count} steps";

    public void SetPaused(bool paused)
    {
        _paused = paused;
        StatusText.Text = paused ? "PAUSED" : "RECORDING";
        PauseButton.Content = paused ? "Resume" : "Pause";
        Dot.Fill = paused ? new SolidColorBrush(Color.FromRgb(0xF5, 0xB0, 0x41)) : new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4F));
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not System.Windows.Controls.Button && e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    /// <summary>Brief "Captured" confirmation on the bar after a screenshot.</summary>
    public void FlashCaptured()
    {
        StatusText.Text = "CAPTURED";
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            StatusText.Text = _paused ? "PAUSED" : "RECORDING";
        };
        timer.Start();
    }

    private bool _paused;

    private void OnScreenshot(object sender, RoutedEventArgs e) => ScreenshotClicked?.Invoke();
    private void OnPause(object sender, RoutedEventArgs e) => PauseClicked?.Invoke();
    private void OnStop(object sender, RoutedEventArgs e) => StopClicked?.Invoke();
}
