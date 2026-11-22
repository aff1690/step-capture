using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace StepCapture.App.Themes;

public enum AppTheme { Light, Dark }

/// <summary>Switches between the light and dark color themes and remembers the choice.</summary>
internal static class ThemeManager
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StepCapture", "settings.json");

    private sealed record Settings(AppTheme? Theme);

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static event Action? Changed;

    /// <summary>Applies the saved theme, or follows the Windows app theme the first time.</summary>
    public static void Initialize()
    {
        Apply(LoadSaved() ?? WindowsTheme(), save: false);
    }

    public static void Toggle() => Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark, save: true);

    public static void Apply(AppTheme theme, bool save)
    {
        Current = theme;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var source = new Uri($"pack://application:,,,/StepCapture;component/Themes/{theme}.xaml", UriKind.Absolute);
        dictionaries[0] = new ResourceDictionary { Source = source };

        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
        if (save) Save(theme);
        Changed?.Invoke();
    }

    /// <summary>Makes the window's title bar dark or light to match. Call from SourceInitialized.</summary>
    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = Current == AppTheme.Dark ? 1 : 0;
        // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 on Windows 10 20H1+ and 11, 19 on older Windows 10 builds.
        if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
    }

    private static AppTheme WindowsTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch
        {
            return AppTheme.Dark;
        }
    }

    private static AppTheme? LoadSaved()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath))?.Theme;
        }
        catch
        {
            return null;
        }
    }

    private static void Save(AppTheme theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings(theme)));
        }
        catch (Exception ex)
        {
            Log.Error("Save settings", ex);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
