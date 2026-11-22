using StepCapture.Core.Model;

namespace StepCapture.Core.Export;

public interface IGuideExporter
{
    /// <summary>Name shown in the export menu, such as "Word document".</summary>
    string Name { get; }

    /// <summary>File extension including the dot, such as ".docx".</summary>
    string Extension { get; }

    void Export(Guide guide, string path);
}

public static class Exporters
{
    public static IReadOnlyList<IGuideExporter> All { get; } = new IGuideExporter[]
    {
        new HtmlExporter(),
        new DocxExporter(),
        new PdfExporter(),
        new MarkdownExporter(),
    };
}
