using DocumentFormat.OpenXml.Packaging;
using StepCapture.Core.Export;
using StepCapture.Core.Model;
using StepCapture.Core.Recording;
using StepCapture.Core.Storage;

namespace StepCapture.Core.Tests;

public sealed class OverviewTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("stepcapture-overview-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Guide GuideWithOverview()
    {
        var guide = TestData.SampleGuide(); // three numbered steps
        guide.Steps.Insert(0, new Step
        {
            Kind = StepKind.Screenshot, IsOverview = true, Caption = "The main screen",
            Notes = "Menus are on the left.", ImagePng = TestData.Png(320, 200),
        });
        return guide;
    }

    [Fact]
    public void OverviewStepsAreNotNumbered()
    {
        var guide = GuideWithOverview();
        Assert.Equal(new int?[] { null, 1, 2, 3 }, guide.StepNumbers());
        Assert.Equal(3, guide.NumberedStepCount);
    }

    [Fact]
    public void ScreenshotBecomesOverviewStep()
    {
        var builder = new StepBuilder();
        var added = new List<Step>();
        builder.StepAdded += added.Add;
        var step = builder.OnScreenshot(DateTimeOffset.Now, TestData.Png(10, 10), "Notepad");
        Assert.Same(step, Assert.Single(added));
        Assert.True(step.IsOverview);
        Assert.Equal(StepKind.Screenshot, step.Kind);
        Assert.Equal("Overview of Notepad", step.Caption);
        Assert.Equal("Overview", CaptionBuilder.ForScreenshot(null));
    }

    [Fact]
    public void ScreenshotEndsTypingRun()
    {
        var builder = new StepBuilder();
        var added = new List<Step>();
        builder.StepAdded += added.Add;
        builder.OnKey(new KeyInput(DateTimeOffset.Now, new KeyAction(KeyActionKind.Text, "ab"), null, null));
        builder.OnScreenshot(DateTimeOffset.Now, TestData.Png(10, 10), null);
        builder.OnKey(new KeyInput(DateTimeOffset.Now, new KeyAction(KeyActionKind.Text, "c"), null, null));
        Assert.Equal(3, added.Count); // typing, screenshot, new typing step
    }

    [Fact]
    public void HtmlShowsOverviewAsHeadingWithoutNumber()
    {
        string html = HtmlExporter.Render(GuideWithOverview());
        Assert.Contains("<li class=\"overview\">", html);
        Assert.Contains("<span class=\"cap\">The main screen</span>", html);
        Assert.Contains("<span class=\"num\">1</span>", html);
        Assert.Contains("<span class=\"num\">3</span>", html);
        Assert.DoesNotContain("<span class=\"num\">4</span>", html);
        Assert.Contains("3 steps", html);
    }

    [Fact]
    public void MarkdownShowsOverviewAsHeading()
    {
        string path = Path.Combine(_dir, "g.md");
        new MarkdownExporter().Export(GuideWithOverview(), path);
        string md = File.ReadAllText(path);
        Assert.Contains("## The main screen\n", md);
        Assert.Contains("\nMenus are on the left.\n", md);
        Assert.Contains("3. Press Enter", md);
    }

    [Fact]
    public void DocxAndPdfExportOverview()
    {
        var guide = GuideWithOverview();
        string docx = Path.Combine(_dir, "g.docx");
        new DocxExporter().Export(guide, docx);
        using (var doc = WordprocessingDocument.Open(docx, false))
        {
            string text = doc.MainDocumentPart!.Document.Body!.InnerText;
            Assert.Contains("The main screen", text);
            Assert.DoesNotContain("4. ", text);
            Assert.Contains("3. ", text);
        }
        string pdf = Path.Combine(_dir, "g.pdf");
        new PdfExporter().Export(guide, pdf);
        Assert.True(new FileInfo(pdf).Length > 1000);
    }

    [Fact]
    public void OverviewFlagSurvivesSaveAndLoad()
    {
        string path = Path.Combine(_dir, "g" + GuideFile.Extension);
        GuideFile.Save(GuideWithOverview(), path);
        var loaded = GuideFile.Load(path);
        Assert.True(loaded.Steps[0].IsOverview);
        Assert.False(loaded.Steps[1].IsOverview);
        Assert.True(loaded.Steps[0].Clone().IsOverview);
    }
}
