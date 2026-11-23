using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using StepCapture.Core.Export;
using StepCapture.Core.Model;

namespace StepCapture.Core.Tests;

public sealed class RichTextTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("stepcapture-rich-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void ParsesIndentAndFormatting()
    {
        var lines = RichText.Parse("1. **Bold** and *italic*\n\ta. __under__ line");
        Assert.Equal(2, lines.Count);
        Assert.Equal(0, lines[0].Indent);
        Assert.Equal(ListKind.Numbered, lines[0].List);
        Assert.Equal(new[] { "Bold", " and ", "italic" }, lines[0].Spans.Select(s => s.Text));
        Assert.True(lines[0].Spans[0].Bold);
        Assert.True(lines[0].Spans[2].Italic);
        Assert.Equal(1, lines[1].Indent);
        Assert.True(lines[1].Spans[1].Underline);
    }

    [Fact]
    public void SpacesFromOlderNotesBecomeIndentLevels()
    {
        var lines = RichText.Parse("top\n    four\n      six");
        Assert.Equal(new[] { 0, 1, 2 }, lines.Select(l => l.Indent));
        Assert.Equal("six", lines[2].PlainText);
    }

    [Fact]
    public void UnmatchedMarkersStayAsText()
    {
        Assert.Equal("5 * 3 = 15", RichText.Parse("5 * 3 = 15")[0].PlainText);
        Assert.Equal("snake_case and __init", RichText.Parse("snake_case and __init")[0].PlainText);
    }

    [Fact]
    public void SerializeRoundTrips()
    {
        var lines = new List<RichLine>
        {
            new() { Spans = { new RichSpan("Plain * star "), new RichSpan("bold", Bold: true), new RichSpan(" "), new RichSpan("all", true, true, true) } },
            new() { Indent = 2, Spans = { new RichSpan("deep __ text") } },
        };
        string text = RichText.Serialize(lines);
        var back = RichText.Parse(text);
        Assert.Equal(2, back.Count);
        Assert.Equal("Plain * star bold all", back[0].PlainText);
        Assert.True(back[0].Spans.Single(s => s.Text == "bold").Bold);
        var all = back[0].Spans.Single(s => s.Text == "all");
        Assert.True(all.Bold && all.Italic && all.Underline);
        Assert.Equal(2, back[1].Indent);
        Assert.Equal("deep __ text", back[1].PlainText);
    }

    [Fact]
    public void ParsesListsAndLabelsLevels()
    {
        var lines = RichText.Parse("1. Tool views\n\t- Dashboard\n\t- All projects\n\n1. Requests\n\t1. First\n\t1. Second\n\t\t1. Deep\nPlain\n1. Again");
        var labels = RichText.ListLabels(lines);
        Assert.Equal(new[] { "1.", "◦", "◦", "", "2.", "a.", "b.", "i.", "", "1." }, labels); // bullets by level: • ◦ ▪
        Assert.Equal(ListKind.Numbered, lines[0].List);
        Assert.Equal("Tool views", lines[0].PlainText);
        Assert.Equal(ListKind.Bullet, lines[1].List);
        Assert.Equal(1, lines[1].Indent);
    }

    [Fact]
    public void ListsRoundTripAndPlainLookalikesStayPlain()
    {
        var lines = new List<RichLine>
        {
            new() { List = ListKind.Numbered, Spans = { new RichSpan("One") } },
            new() { Indent = 1, List = ListKind.Bullet, Spans = { new RichSpan("Sub", Bold: true) } },
            new() { Spans = { new RichSpan("2. not a list") } },
            new() { Spans = { new RichSpan("- not a bullet") } },
        };
        var back = RichText.Parse(RichText.Serialize(lines));
        Assert.Equal(new[] { ListKind.Numbered, ListKind.Bullet, ListKind.None, ListKind.None }, back.Select(l => l.List));
        Assert.Equal("2. not a list", back[2].PlainText);
        Assert.Equal("- not a bullet", back[3].PlainText);
        Assert.True(back[1].Spans[0].Bold);
    }

    [Fact]
    public void LabelStyles() 
    {
        Assert.Equal("aa", RichText.NumberLabel(27, 1));
        Assert.Equal("xiv", RichText.NumberLabel(14, 2));
        Assert.Equal("4", RichText.NumberLabel(4, 3));
    }

    private static Step StepWithCallouts()
    {
        var c1 = new MarkupItem { Kind = MarkupKind.Number, Text = "1", Color = 0xFFE5484D, Points = { new PointD(10, 10) } };
        var c2 = new MarkupItem { Kind = MarkupKind.Number, Text = "2", Color = 0xFF3B82F6, Points = { new PointD(50, 10) } };
        var step = new Step
        {
            Kind = StepKind.Screenshot, Caption = "Overview", ImagePng = TestData.Png(200, 100),
            Notes = "General **notes**\n1. First\n\t- sub item",
            Markup = { c2, c1 },
        };
        step.CalloutNotes.Add(new CalloutNote { CalloutId = c2.Id, Title = "Tool views", Details = "- Dashboard\n- *Pipeline*" });
        step.CalloutNotes.Add(new CalloutNote { CalloutId = c1.Id, Title = "New project request", Details = "Adds a lead." });
        return step;
    }

    [Fact]
    public void CalloutNotesFollowCalloutsAndSync()
    {
        var step = StepWithCallouts();
        var ordered = CalloutNotes.Ordered(step);
        Assert.Equal(new int?[] { 1, 2 }, ordered.Select(o => o.Number));
        Assert.Equal("New project request", ordered[0].Note.Title);

        // A new callout gets an empty note; deleting a callout drops its note only if it is empty.
        var c3 = new MarkupItem { Kind = MarkupKind.Number, Text = "3" };
        step.Markup.Add(c3);
        Assert.True(CalloutNotes.Sync(step));
        Assert.Equal(3, step.CalloutNotes.Count);
        step.Markup.Remove(c3);
        step.Markup.RemoveAll(m => m.Text == "1");
        CalloutNotes.Sync(step);
        Assert.Equal(2, step.CalloutNotes.Count); // empty note for 3 gone, filled note for 1 kept as orphan
        Assert.Null(CalloutNotes.Ordered(step)[^1].Number);
        Assert.Single(CalloutNotes.ForExport(step));
    }

    [Fact]
    public void HtmlRendersListsAndCalloutNotes()
    {
        var step = StepWithCallouts();
        string notes = HtmlExporter.RenderRich(step.Notes);
        Assert.Contains("<strong>notes</strong>", notes);
        Assert.Contains("<span class=\"lm\">1.</span>First", notes);
        Assert.Contains("<span class=\"lm\">◦</span>sub item", notes);
        string callouts = HtmlExporter.RenderCalloutNotes(step);
        Assert.True(callouts.IndexOf("New project request") < callouts.IndexOf("Tool views"));
        Assert.Contains("background:#E5484D", callouts);
        Assert.Contains("<em>Pipeline</em>", callouts);
    }

    [Fact]
    public void DocxRendersListsAndCalloutNotes()
    {
        var guide = new Guide { Steps = { StepWithCallouts() } };
        string path = Path.Combine(_dir, "g.docx");
        new DocxExporter().Export(guide, path);
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        Assert.Contains("\u2460", body.InnerText);
        Assert.Contains("\u2461", body.InnerText);
        Assert.Contains("Tool views", body.InnerText);
        Assert.Contains(body.Descendants<Indentation>(), i => i.Hanging == "360");
        Assert.Contains(body.Descendants<Run>(), r => r.InnerText == "notes" && r.RunProperties?.Bold is not null);
    }

    [Fact]
    public void PdfAndMarkdownRenderListsAndCalloutNotes()
    {
        var guide = new Guide { Steps = { StepWithCallouts() } };
        string pdf = Path.Combine(_dir, "g.pdf");
        new PdfExporter().Export(guide, pdf);
        Assert.True(new FileInfo(pdf).Length > 1000);

        string md = Path.Combine(_dir, "g.md");
        new MarkdownExporter().Export(guide, md);
        string text = File.ReadAllText(md);
        Assert.Contains("1. First  \n       - sub item", text); // step text is indented 3, sub-level 4 more
        Assert.Contains("**(1)** New project request", text);
        Assert.Contains("    - *Pipeline*", text);
    }

    [Fact]
    public void CalloutNotesSurviveSaveAndLoad()
    {
        string path = Path.Combine(_dir, "g" + StepCapture.Core.Storage.GuideFile.Extension);
        var step = StepWithCallouts();
        step.BaseImagePng = step.ImagePng;
        StepCapture.Core.Storage.GuideFile.Save(new Guide { Steps = { step } }, path);
        var loaded = StepCapture.Core.Storage.GuideFile.Load(path).Steps[0];
        Assert.Equal(2, loaded.CalloutNotes.Count);
        Assert.Equal(new[] { 1, 2 }, CalloutNotes.ForExport(loaded).Select(n => n.Number!.Value));
    }
}
