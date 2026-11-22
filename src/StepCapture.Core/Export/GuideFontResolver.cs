using PdfSharp.Fonts;

namespace StepCapture.Core.Export;

/// <summary>
/// Supplies TrueType fonts to PDFsharp. Uses Segoe UI or Arial from the Windows font folder,
/// and common open-source fonts elsewhere (so the exporter also runs in Linux test environments).
/// </summary>
internal sealed class GuideFontResolver : IFontResolver
{
    private static readonly object Gate = new();
    private static bool _installed;

    public static void EnsureInstalled()
    {
        lock (Gate)
        {
            if (_installed) return;
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = new GuideFontResolver();
            _installed = true;
        }
    }

    private static readonly string[] FontDirs =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts"),
        "/usr/share/fonts/truetype/dejavu",
        "/usr/share/fonts/truetype/liberation",
        "/usr/share/fonts/TTF",
        "/Library/Fonts",
        "/System/Library/Fonts/Supplemental",
    };

    // Candidates per face, best first.
    private static readonly Dictionary<string, string[]> Faces = new()
    {
        ["regular"] = new[] { "segoeui.ttf", "arial.ttf", "DejaVuSans.ttf", "LiberationSans-Regular.ttf", "Arial.ttf" },
        ["bold"] = new[] { "segoeuib.ttf", "arialbd.ttf", "DejaVuSans-Bold.ttf", "LiberationSans-Bold.ttf", "Arial Bold.ttf" },
        ["italic"] = new[] { "segoeuii.ttf", "ariali.ttf", "DejaVuSans-Oblique.ttf", "LiberationSans-Italic.ttf", "Arial Italic.ttf" },
        ["bolditalic"] = new[] { "segoeuiz.ttf", "arialbi.ttf", "DejaVuSans-BoldOblique.ttf", "LiberationSans-BoldItalic.ttf", "Arial Bold Italic.ttf" },
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        string face = (isBold, isItalic) switch
        {
            (true, true) => "bolditalic",
            (true, false) => "bold",
            (false, true) => "italic",
            _ => "regular",
        };
        return new FontResolverInfo(face);
    }

    public byte[]? GetFont(string faceName)
    {
        if (!Faces.TryGetValue(faceName, out var candidates)) candidates = Faces["regular"];
        foreach (var file in candidates)
            foreach (var dir in FontDirs)
            {
                string p = Path.Combine(dir, file);
                if (File.Exists(p)) return File.ReadAllBytes(p);
            }
        if (faceName != "regular") return GetFont("regular");
        throw new FileNotFoundException("No usable TrueType font found for PDF export.");
    }
}
