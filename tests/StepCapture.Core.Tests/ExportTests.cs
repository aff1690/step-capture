using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using StepCapture.Core.Export;
using StepCapture.Core.Storage;

namespace StepCapture.Core.Tests;

public sealed class ExportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("stepcapture-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void PngSizeIsReadFromHeader() => Assert.Equal((640, 480), PngInfo.GetSize(TestData.Png(640, 480)));

    [Fact]
    public void HtmlEmbedsImagesAndEscapesText()
    {
        string html = HtmlExporter.Render(TestData.SampleGuide());
        Assert.Contains("<h1>Save a file in Notepad</h1>", html);
        Assert.Contains("notes &lt;draft&gt; &amp; more", html);
        Assert.Equal(2, CountOf(html, "data:image/png;base64,"));
        Assert.Contains("Use any name.", html);
    }

    [Fact]
    public void MarkdownWritesImagesNextToFile()
    {
        string path = Path.Combine(_dir, "My guide.md");
        new MarkdownExporter().Export(TestData.SampleGuide(), path);
        string md = File.ReadAllText(path);
        Assert.StartsWith("# Save a file in Notepad", md);
        Assert.Contains("3. Press Enter", md);
        Assert.Contains("](My%20guide_images/step-001.png)", md);
        Assert.True(File.Exists(Path.Combine(_dir, "My guide_images", "step-002.png")));
    }

    [Fact]
    public void DocxContainsOnePicturePerScreenshot()
    {
        string path = Path.Combine(_dir, "guide.docx");
        new DocxExporter().Export(TestData.SampleGuide(), path);
        using var doc = WordprocessingDocument.Open(path, false);
        var main = doc.MainDocumentPart!;
        Assert.Equal(2, main.ImageParts.Count());
        string text = main.Document.Body!.InnerText;
        Assert.Contains("Click the \"File\" menu item", text);
        Assert.Contains("Press Enter", text);
        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description)));
    }

    [Fact]
    public void PdfIsWritten()
    {
        string path = Path.Combine(_dir, "guide.pdf");
        var guide = TestData.SampleGuide();
        for (int i = 0; i < 6; i++) guide.Steps.Add(guide.Steps[1].Clone()); // force several pages
        new PdfExporter().Export(guide, path);
        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(path, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount >= 3, $"expected several pages, got {pdf.PageCount}");
    }

    [Fact]
    public void GuideFileRoundTrips()
    {
        string path = Path.Combine(_dir, "guide" + GuideFile.Extension);
        var original = TestData.SampleGuide();
        GuideFile.Save(original, path);
        var loaded = GuideFile.Load(path);

        Assert.Equal(original.Title, loaded.Title);
        Assert.Equal(original.Description, loaded.Description);
        Assert.Equal(original.Steps.Count, loaded.Steps.Count);
        for (int i = 0; i < original.Steps.Count; i++)
        {
            Assert.Equal(original.Steps[i].Id, loaded.Steps[i].Id);
            Assert.Equal(original.Steps[i].Caption, loaded.Steps[i].Caption);
            Assert.Equal(original.Steps[i].Kind, loaded.Steps[i].Kind);
            Assert.Equal(original.Steps[i].ImagePng, loaded.Steps[i].ImagePng);
        }
        using var zip = ZipFile.OpenRead(path);
        Assert.NotNull(zip.GetEntry("guide.json"));
    }

    private static int CountOf(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }
}
