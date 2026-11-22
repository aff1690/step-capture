using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using StepCapture.Core.Export;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace StepCapture.Core.Tests;

public sealed class LetterheadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("stepcapture-letterhead-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Builds a small .dotx with a picture in the header and contact details in the footer.</summary>
    private string BuildTemplate()
    {
        string path = Path.Combine(_dir, "Company.dotx");
        using var doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Template);
        var main = doc.AddMainDocumentPart();

        var header = main.AddNewPart<HeaderPart>();
        var image = header.AddImagePart(ImagePartType.Png);
        using (var ms = new MemoryStream(TestData.Png(600, 110, 30, 60, 90))) image.FeedData(ms);
        string imageId = header.GetIdOfPart(image);
        header.Header = new Header(new Paragraph(new Run(new Drawing(new DW.Inline(
            new DW.Extent { Cx = 5943600, Cy = 1100455 },
            new DW.DocProperties { Id = 1, Name = "Logo" },
            new A.Graphic(new A.GraphicData(new PIC.Picture(
                new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = 1, Name = "logo.png" }, new PIC.NonVisualPictureDrawingProperties()),
                new PIC.BlipFill(new A.Blip { Embed = imageId }, new A.Stretch(new A.FillRectangle())),
                new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 5943600, Cy = 1100455 }),
                    new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle }))) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))))));

        var footer = main.AddNewPart<FooterPart>();
        footer.Footer = new Footer(new Paragraph(
            new Run(new Text("T: (555) 010-0000 | ") { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }),
            new Run(new Text("www.example.com"))));

        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text("Template placeholder text"))),
            new SectionProperties(
                new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(header) },
                new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(footer) },
                new PageSize { Width = 12240, Height = 15840 },
                new PageMargin { Top = 1440, Bottom = 1440, Left = 1440, Right = 1440, Header = 720, Footer = 720 })));
        return path;
    }

    [Fact]
    public void ReadsHeaderPictureAndFooterText()
    {
        var lh = Letterhead.Load(BuildTemplate());
        Assert.NotNull(lh.HeaderImage);
        Assert.Equal((600, 110), PngInfo.GetSize(lh.HeaderImage!));
        Assert.Equal(468, lh.HeaderImageWidth, 0);   // 6.5 inches
        Assert.Equal("T: (555) 010-0000 | www.example.com", lh.FooterText);
    }

    [Fact]
    public void WordExportKeepsTemplateHeaderAndFooterButNotItsText()
    {
        string template = BuildTemplate();
        string path = Path.Combine(_dir, "guide.docx");
        new DocxExporter().Export(TestData.SampleGuide(), path, new ExportOptions { Letterhead = Letterhead.Load(template) });

        using var doc = WordprocessingDocument.Open(path, false);
        Assert.Equal(DocumentFormat.OpenXml.WordprocessingDocumentType.Document, doc.DocumentType);
        var main = doc.MainDocumentPart!;
        Assert.Single(main.HeaderParts);
        Assert.Single(main.FooterParts);
        string text = main.Document.Body!.InnerText;
        Assert.DoesNotContain("Template placeholder text", text);
        Assert.Contains("Press Enter", text);
        Assert.Equal(2, main.ImageParts.Count());
        Assert.NotNull(main.Document.Body!.Elements<SectionProperties>().Single().GetFirstChild<HeaderReference>());
        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description)));
        Assert.True(File.Exists(template)); // the template itself is untouched
    }

    [Fact]
    public void PdfAndHtmlExportsUseLetterhead()
    {
        var options = new ExportOptions { Letterhead = Letterhead.Load(BuildTemplate()) };
        string pdf = Path.Combine(_dir, "guide.pdf");
        new PdfExporter().Export(TestData.SampleGuide(), pdf, options);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 4));

        string html = HtmlExporter.Render(TestData.SampleGuide(), options.Letterhead);
        Assert.Contains("class=\"letterhead\"", html);
        Assert.Contains("www.example.com", html);
        Assert.Equal(3, html.Split("data:image/png;base64,").Length - 1); // banner + 2 screenshots
    }
}
