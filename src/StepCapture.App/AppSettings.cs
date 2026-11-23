using System.Text.Json;
using StepCapture.App.Themes;

namespace StepCapture.App;

/// <summary>User preferences saved to %LOCALAPPDATA%\StepCapture\settings.json.</summary>
internal sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StepCapture", "settings.json");

    public AppTheme? Theme { get; set; }

    /// <summary>Word template (.dotx/.docx) with the company letterhead.</summary>
    public string? LetterheadPath { get; set; }

    /// <summary>True to export on the letterhead, false for a blank page.</summary>
    public bool UseLetterhead { get; set; }

    /// <summary>Folder that Save, Open and Export dialogs start in (null: Windows picks).</summary>
    public string? DefaultFolder { get; set; }

    /// <summary>The default folder if it is set and still exists.</summary>
    public string? DefaultFolderIfExists => DefaultFolder is { } f && Directory.Exists(f) ? f : null;

    /// <summary>Details beside the screenshot (true) or above it (false).</summary>
    public bool SideBySide { get; set; }

    /// <summary>Share of the width the details take in side-by-side layout.</summary>
    public double SideDetailsRatio { get; set; } = 0.38;

    /// <summary>Height of the details area above the screenshot in stacked layout.</summary>
    public double StackedDetailsHeight { get; set; } = 300;

    public bool StepsPanelCollapsed { get; set; }
    public double StepsPanelWidth { get; set; } = 360;

    public static AppSettings Current { get; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error("Load settings", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Error("Save settings", ex);
        }
    }
}
