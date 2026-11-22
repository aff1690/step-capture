using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace StepCapture.Core.Export;

/// <summary>
/// A company letterhead taken from a Word template (.dotx or .docx). Word exports are built on the template
/// itself, so its header, footer, margins and styles carry over exactly. PDF and HTML exports reuse the
/// header picture and the footer text.
/// </summary>
public sealed class Letterhead
{
    private const double EmuPerPoint = 12700;

    public required string TemplatePath { get; init; }

    /// <summary>The first picture in the template's header (usually the logo banner), or null.</summary>
    public byte[]? HeaderImage { get; init; }

    /// <summary>MIME type of <see cref="HeaderImage"/>, such as image/png.</summary>
    public string HeaderImageType { get; init; } = "image/png";

    /// <summary>Size of the header picture as placed in the template, in points.</summary>
    public double HeaderImageWidth { get; init; }
    public double HeaderImageHeight { get; init; }

    /// <summary>Text of the template's footer, one line per paragraph.</summary>
    public string FooterText { get; init; } = "";

    public static Letterhead Load(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var main = doc.MainDocumentPart ?? throw new InvalidDataException("The template has no document body.");

        byte[]? image = null;
        string imageType = "image/png";
        double w = 0, h = 0;
        foreach (var header in PreferDefault(main, main.HeaderParts, isHeader: true))
        {
            var blip = header.Header?.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
            if (blip?.Embed?.Value is not { } relId || header.GetPartById(relId) is not ImagePart imagePart) continue;
            using (var s = imagePart.GetStream())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                image = ms.ToArray();
            }
            imageType = imagePart.ContentType;
            var extent = header.Header!.Descendants<DW.Extent>().FirstOrDefault();
            if (extent is { Cx: not null, Cy: not null })
            {
                w = extent.Cx.Value / EmuPerPoint;
                h = extent.Cy.Value / EmuPerPoint;
            }
            break;
        }

        string footer = "";
        foreach (var part in PreferDefault(main, main.FooterParts, isHeader: false))
        {
            var lines = part.Footer?.Descendants<Paragraph>()
                .Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text)).Replace('\t', ' ').Trim())
                .Where(l => l.Length > 0)
                .ToList();
            if (lines is { Count: > 0 })
            {
                footer = string.Join("\n", lines);
                break;
            }
        }

        return new Letterhead
        {
            TemplatePath = path,
            HeaderImage = image,
            HeaderImageType = imageType,
            HeaderImageWidth = w,
            HeaderImageHeight = h,
            FooterText = footer,
        };
    }

    /// <summary>Header/footer parts with the one the document uses by default first.</summary>
    private static IEnumerable<T> PreferDefault<T>(MainDocumentPart main, IEnumerable<T> parts, bool isHeader) where T : OpenXmlPart
    {
        var sect = main.Document?.Body?.Elements<SectionProperties>().LastOrDefault();
        string? defaultId = isHeader
            ? sect?.Elements<HeaderReference>().FirstOrDefault(r => r.Type?.Value == HeaderFooterValues.Default)?.Id?.Value
            : sect?.Elements<FooterReference>().FirstOrDefault(r => r.Type?.Value == HeaderFooterValues.Default)?.Id?.Value;
        var list = parts.ToList();
        if (defaultId is not null && main.GetPartById(defaultId) is T preferred)
        {
            list.Remove(preferred);
            list.Insert(0, preferred);
        }
        return list;
    }
}

/// <summary>Options that apply to every export format.</summary>
public sealed record ExportOptions
{
    /// <summary>Company letterhead to put the guide on, or null for a blank page.</summary>
    public Letterhead? Letterhead { get; init; }

    public static ExportOptions Default { get; } = new();
}
