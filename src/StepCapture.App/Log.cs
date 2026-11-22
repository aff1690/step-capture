namespace StepCapture.App;

/// <summary>Minimal error log at %LOCALAPPDATA%\StepCapture\error.log.</summary>
internal static class Log
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StepCapture", "error.log");

    public static void Error(string context, Exception ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}\n\n");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
