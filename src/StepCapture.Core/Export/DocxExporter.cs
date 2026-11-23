using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using StepCapture.Core.Model;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace StepCapture.Core.Export;

/// <summary>A Word document: title, numbered step captions, notes and screenshots.</summary>
public sealed class DocxExporter : IGuideExporter
{
    private const long EmuPerPixel = 9525;           // at 96 DPI
    private const long EmuPerTwip = 635;

    public string Name => "Word document";
    public string Extension => ".docx";

    public void Export(Guide guide, string path, ExportOptions? options = null)
    {
        var letterhead = options?.Letterhead;
        using var doc = letterhead is null ? CreateBlank(path) : CreateFromTemplate(letterhead.TemplatePath, path);
        var main = doc.MainDocumentPart!;
        var body = main.Document.Body!;
        var section = body.Elements<SectionProperties>().Last();
        var (maxWidth, maxHeight) = ImageArea(section);

        section.InsertBeforeSelf(TextParagraph(guide.Title, sizeHalfPoints: 40, bold: true, spacingAfter: 120));
        section.InsertBeforeSelf(TextParagraph($"{guide.StepCountText} · {guide.Created:MMMM d, yyyy}",
            sizeHalfPoints: 20, color: "5B6475", spacingAfter: 240));
        if (!string.IsNullOrWhiteSpace(guide.Description))
            section.InsertBeforeSelf(TextParagraph(guide.Description.Trim(), spacingAfter: 240));

        uint imageId = 1000; // clear of ids used by pictures in a letterhead header
        var numbers = guide.StepNumbers();
        for (int i = 0; i < guide.Steps.Count; i++)
        {
            var step = guide.Steps[i];
            int? n = numbers[i];
            // Overview steps read as a heading with no number; regular steps get "1.", "2."...
            var caption = n is null
                ? new Paragraph(
                    new ParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "360", After = "100" }),
                    Run(step.Caption, bold: true, color: "1D2330", sizeHalfPoints: 30))
                : new Paragraph(
                    new ParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "240", After = "80" }),
                    Run($"{n}. ", bold: true, color: "E8590C", sizeHalfPoints: 26),
                    Run(step.Caption, bold: true, sizeHalfPoints: 24));
            section.InsertBeforeSelf(caption);

            if (!string.IsNullOrWhiteSpace(step.Notes))
                foreach (var p in NoteParagraphs(step, keepWithImage: step.ImagePng is not null))
                    section.InsertBeforeSelf(p);

            if (step.ImagePng is { Length: > 0 } png)
                section.InsertBeforeSelf(ImageParagraph(main, png, imageId++, n is null ? "Overview" : $"Step {n}", maxWidth, maxHeight));
        }

        main.Document.Save();
    }

    private static WordprocessingDocument CreateBlank(string path)
    {
        var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(new SectionProperties(
            new PageSize { Width = 12240, Height = 15840 },             // US Letter
            new PageMargin { Top = 1440, Bottom = 1440, Left = 1440, Right = 1440, Header = 720, Footer = 720 })));
        return doc;
    }

    /// <summary>Starts from a copy of the letterhead template: keeps its header, footer, margins and styles, drops its body text.</summary>
    private static WordprocessingDocument CreateFromTemplate(string templatePath, string path)
    {
        File.Copy(templatePath, path, overwrite: true);
        File.SetAttributes(path, FileAttributes.Normal);
        var doc = WordprocessingDocument.Open(path, true);
        if (doc.DocumentType != WordprocessingDocumentType.Document)
            doc.ChangeDocumentType(WordprocessingDocumentType.Document); // .dotx -> .docx

        var main = doc.MainDocumentPart ?? throw new InvalidDataException("The letterhead template has no document body.");
        main.Document ??= new Document(new Body());
        var body = main.Document.Body ??= new Body();
        var section = body.Elements<SectionProperties>().LastOrDefault();
        foreach (var child in body.ChildElements.ToList())
            if (child != section) child.Remove();
        if (section is null) body.Append(new SectionProperties(new PageSize { Width = 12240, Height = 15840 },
            new PageMargin { Top = 1440, Bottom = 1440, Left = 1440, Right = 1440, Header = 720, Footer = 720 }));
        return doc;
    }

    /// <summary>Largest picture that fits between the page margins, in EMU.</summary>
    private static (long Width, long Height) ImageArea(SectionProperties section)
    {
        var size = section.GetFirstChild<PageSize>();
        var margin = section.GetFirstChild<PageMargin>();
        long pageW = size?.Width?.Value ?? 12240, pageH = size?.Height?.Value ?? 15840;
        long left = margin?.Left?.Value ?? 1440, right = margin?.Right?.Value ?? 1440;
        long top = margin?.Top?.Value ?? 1440, bottom = margin?.Bottom?.Value ?? 1440;
        long width = Math.Max(1440, pageW - left - right) * EmuPerTwip;
        long height = (long)(Math.Max(2880, pageH - Math.Abs(top) - Math.Abs(bottom)) * EmuPerTwip * 0.8); // room for the caption
        return (width, height);
    }

    /// <summary>
    /// One paragraph per note line, indented by level, with bold/italic/underline. A line starting with the
    /// number of a callout on the screenshot gets that number as a circled badge in the callout's color.
    /// </summary>
    private static IEnumerable<Paragraph> NoteParagraphs(Step step, bool keepWithImage)
    {
        var colors = RichText.CalloutColors(step);
        var lines = RichText.Parse(step.Notes);
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var pp = new ParagraphProperties();
            if (keepWithImage || i < lines.Count - 1) pp.Append(new KeepNext());
            pp.Append(new SpacingBetweenLines { After = i == lines.Count - 1 ? "80" : "0" });
            if (line.Indent > 0) pp.Append(new Indentation { Left = (line.Indent * 360).ToString() });
            var para = new Paragraph(pp);

            var content = line;
            if (RichText.TryLeadingNumber(line, out int n, out var rest) && colors.TryGetValue(n, out uint argb))
            {
                content = rest;
                string badge = n is >= 1 and <= 20 ? ((char)(0x2460 + n - 1)).ToString() : $"({n})";
                var r = Run(badge + " ", bold: true, color: $"{argb & 0xFFFFFF:X6}", sizeHalfPoints: 26);
                r.RunProperties!.RunFonts = new RunFonts { Ascii = "Segoe UI Symbol", HighAnsi = "Segoe UI Symbol", ComplexScript = "Segoe UI Symbol" };
                para.Append(r);
            }
            foreach (var span in content.Spans)
                para.Append(Run(span.Text, span.Bold, "5B6475", italic: span.Italic, underline: span.Underline));
            yield return para;
        }
    }

    private static Run Run(string text, bool bold = false, string? color = null, int? sizeHalfPoints = null,
        bool italic = false, bool underline = false)
    {
        var props = new RunProperties(new RunFonts { Ascii = "Segoe UI", HighAnsi = "Segoe UI", ComplexScript = "Segoe UI" });
        if (bold) props.Append(new Bold());
        if (italic) props.Append(new Italic());
        if (color is not null) props.Append(new Color { Val = color });
        if (sizeHalfPoints is { } s) props.Append(new FontSize { Val = s.ToString() });
        if (underline) props.Append(new Underline { Val = UnderlineValues.Single }); // schema order: after sz

        var run = new Run(props);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) run.Append(new Break());
            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
        return run;
    }

    private static Paragraph TextParagraph(string text, int sizeHalfPoints = 22, bool bold = false,
        string? color = null, int spacingAfter = 0, bool keepNext = false)
    {
        var pp = new ParagraphProperties();
        if (keepNext) pp.Append(new KeepNext());
        pp.Append(new SpacingBetweenLines { After = spacingAfter.ToString() });
        return new Paragraph(pp, Run(text, bold, color, sizeHalfPoints));
    }

    private static Paragraph ImageParagraph(MainDocumentPart main, byte[] png, uint id, string name, long maxWidth, long maxHeight)
    {
        var imagePart = main.AddImagePart(ImagePartType.Png);
        using (var ms = new MemoryStream(png)) imagePart.FeedData(ms);
        string relId = main.GetIdOfPart(imagePart);

        var (wPx, hPx) = PngInfo.GetSize(png);
        long cx = wPx * EmuPerPixel, cy = hPx * EmuPerPixel;
        double scale = Math.Min(1.0, Math.Min((double)maxWidth / cx, (double)maxHeight / cy));
        cx = (long)(cx * scale);
        cy = (long)(cy * scale);

        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = id, Name = name },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = id, Name = name + ".png" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                                new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = "D0D4DC" })) { Width = 9525 })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U,
            });

        return new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { After = "200" }),
            new Run(drawing));
    }
}
