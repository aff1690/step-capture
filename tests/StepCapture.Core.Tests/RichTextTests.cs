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
        Assert.Equal(new[] { "1. ", "Bold", " and ", "italic" }, lines[0].Spans.Select(s => s.Text));
        Assert.True(lines[0].Spans[1].Bold);
        Assert.True(lines[0].Spans[3].Italic);
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
    public void CalloutAddsNumberedLineOnce()
    {
        string notes = RichText.EnsureNumberedLine("", 1);
        Assert.Equal("1. ", notes);
        notes = RichText.EnsureNumberedLine(notes + "New project requests", 2);
        Assert.Equal("1. New project requests\n2. ", notes);
        Assert.Equal(notes, RichText.EnsureNumberedLine(notes, 2));
        Assert.True(RichText.TryLeadingNumber(RichText.Parse("12) Twelve")[0], out int n, out var rest));
        Assert.Equal(12, n);
        Assert.Equal("Twelve", rest.PlainText);
    }

    private static Step StepWithCallouts() => new()
    {
        Kind = StepKind.Screenshot, Caption = "Overview", ImagePng = TestData.Png(200, 100),
        Notes = "1. **New** requests\n\ta. sub item\n2. Tool views\n3. No callout for this one",
        Markup =
        {
            new MarkupItem { Kind = MarkupKind.Number, Text = "1", Color = 0xFFE5484D, Points = { new PointD(10, 10) } },
            new MarkupItem { Kind = MarkupKind.Number, Text = "2", Color = 0xFF3B82F6, Points = { new PointD(50, 10) } },
        },
    };

    [Fact]
    public void HtmlNotesHaveBadgesFormattingAndIndent()
    {
        string html = HtmlExporter.RenderNotes(StepWithCallouts());
        Assert.Contains("background:#E5484D", html);
        Assert.Contains("background:#3B82F6", html);
        Assert.Contains(">1</span><strong>New</strong> requests</p>", html);
        Assert.Contains("padding-left:24px", html);
        Assert.Contains("<p>3. No callout for this one</p>", html);
    }

    [Fact]
    public void DocxNotesHaveBadgesFormattingAndIndent()
    {
        var guide = new Guide { Steps = { StepWithCallouts() } };
        string path = Path.Combine(_dir, "g.docx");
        new DocxExporter().Export(guide, path);
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        Assert.Contains("①", body.InnerText); // circled 1
        Assert.Contains("②", body.InnerText);
        Assert.Contains(body.Descendants<Run>(), r => r.InnerText == "New" && r.RunProperties?.Bold is not null);
        Assert.Contains(body.Descendants<Indentation>(), i => i.Left == "360");
        Assert.Contains("3. No callout", body.InnerText);
    }

    [Fact]
    public void PdfAndMarkdownExportFormattedNotes()
    {
        var guide = new Guide { Steps = { StepWithCallouts() } };
        string pdf = Path.Combine(_dir, "g.pdf");
        new PdfExporter().Export(guide, pdf);
        Assert.True(new FileInfo(pdf).Length > 1000);

        string md = Path.Combine(_dir, "g.md");
        new MarkdownExporter().Export(guide, md);
        string text = File.ReadAllText(md);
        Assert.Contains("1. **New** requests  \n", text);
        Assert.Contains("    a. sub item", text);
    }
}
