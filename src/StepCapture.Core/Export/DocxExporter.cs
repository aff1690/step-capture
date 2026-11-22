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
    private const long MaxImageWidthEmu = 6L * 914400; // 6 inches, fits Letter and A4 with 1" margins
    private const long MaxImageHeightEmu = 7L * 914400;

    public string Name => "Word document";
    public string Extension => ".docx";

    public void Export(Guide guide, string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        body.Append(TextParagraph(guide.Title, sizeHalfPoints: 40, bold: true, spacingAfter: 120));
        body.Append(TextParagraph($"{guide.Steps.Count} steps · {guide.Created:MMMM d, yyyy}",
            sizeHalfPoints: 20, color: "5B6475", spacingAfter: 240));
        if (!string.IsNullOrWhiteSpace(guide.Description))
            body.Append(TextParagraph(guide.Description.Trim(), spacingAfter: 240));

        uint imageId = 1;
        int n = 0;
        foreach (var step in guide.Steps)
        {
            n++;
            var caption = new Paragraph(
                new ParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "240", After = "80" }),
                Run($"{n}. ", bold: true, color: "E8590C", sizeHalfPoints: 26),
                Run(step.Caption, bold: true, sizeHalfPoints: 24));
            body.Append(caption);

            if (!string.IsNullOrWhiteSpace(step.Notes))
                body.Append(TextParagraph(step.Notes.Trim(), color: "5B6475", spacingAfter: 80, keepNext: step.ImagePng is not null));

            if (step.ImagePng is { Length: > 0 } png)
                body.Append(ImageParagraph(main, png, imageId++, $"Step {n}"));
        }

        body.Append(new SectionProperties(
            new PageSize { Width = 12240, Height = 15840 },             // US Letter
            new PageMargin { Top = 1440, Bottom = 1440, Left = 1440, Right = 1440, Header = 720, Footer = 720 }));
        main.Document.Save();
    }

    private static Run Run(string text, bool bold = false, string? color = null, int? sizeHalfPoints = null)
    {
        var props = new RunProperties(new RunFonts { Ascii = "Segoe UI", HighAnsi = "Segoe UI", ComplexScript = "Segoe UI" });
        if (bold) props.Append(new Bold());
        if (color is not null) props.Append(new Color { Val = color });
        if (sizeHalfPoints is { } s) props.Append(new FontSize { Val = s.ToString() });

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

    private static Paragraph ImageParagraph(MainDocumentPart main, byte[] png, uint id, string name)
    {
        var imagePart = main.AddImagePart(ImagePartType.Png);
        using (var ms = new MemoryStream(png)) imagePart.FeedData(ms);
        string relId = main.GetIdOfPart(imagePart);

        var (wPx, hPx) = PngInfo.GetSize(png);
        long cx = wPx * EmuPerPixel, cy = hPx * EmuPerPixel;
        double scale = Math.Min(1.0, Math.Min((double)MaxImageWidthEmu / cx, (double)MaxImageHeightEmu / cy));
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
