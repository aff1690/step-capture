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
