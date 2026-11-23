using PdfSharp.Pdf.IO;
using StepCapture.Core.Export;
using StepCapture.Core.Model;

namespace StepCapture.Core.Tests;

public sealed class PdfFlowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("stepcapture-flow-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void LongNotesFlowOntoNextPageAndScreenshotFollows()
    {
        // 80 note lines are more than a page; the step must spread over pages instead of overflowing.
        string notes = string.Join("\n", Enumerable.Range(1, 80).Select(i => $"1. Item number {i} with some words"));
        var guide = new Guide
        {
            Steps =
            {
                new Step { Caption = "Long step", Notes = notes, ImagePng = TestData.Png(1600, 900) },
                new Step { Caption = "Next step", ImagePng = TestData.Png(800, 450) },
            },
        };
        string path = Path.Combine(_dir, "flow.pdf");
        new PdfExporter().Export(guide, path);
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount >= 3, $"expected the notes to continue over pages, got {pdf.PageCount}");
    }
}
