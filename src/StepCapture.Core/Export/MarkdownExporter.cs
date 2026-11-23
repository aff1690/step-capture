using System.Text;
using StepCapture.Core.Model;

namespace StepCapture.Core.Export;

/// <summary>A Markdown file plus a sibling "&lt;name&gt;_images" folder with the screenshots.</summary>
public sealed class MarkdownExporter : IGuideExporter
{
    public string Name => "Markdown";
    public string Extension => ".md";

    public void Export(Guide guide, string path, ExportOptions? options = null)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string imageFolderName = Path.GetFileNameWithoutExtension(path) + "_images";
        string imageDir = Path.Combine(dir, imageFolderName);

        var sb = new StringBuilder();
        sb.Append("# ").Append(guide.Title).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(guide.Description))
            sb.Append(guide.Description.Trim()).Append("\n\n");

        var numbers = guide.StepNumbers();
        for (int i = 0; i < guide.Steps.Count; i++)
        {
            var step = guide.Steps[i];
            int? n = numbers[i];
            // Overview steps are headings with unindented text; numbered steps are list items.
            string indent = n is null ? "" : "   ";
            if (n is null) sb.Append("## ").Append(EscapeInline(step.Caption)).Append('\n');
            else sb.Append(n).Append(". ").Append(EscapeInline(step.Caption)).Append('\n');
            if (!string.IsNullOrWhiteSpace(step.Notes))
                sb.Append('\n').Append(indent).Append(step.Notes.Trim().Replace("\n", "\n" + indent)).Append('\n');
            if (step.ImagePng is { Length: > 0 } png)
            {
                Directory.CreateDirectory(imageDir);
                string file = $"step-{i + 1:D3}.png";
                File.WriteAllBytes(Path.Combine(imageDir, file), png);
                string alt = n is null ? EscapeInline(step.Caption) : $"Step {n}";
                sb.Append('\n').Append($"{indent}![{alt}]({Uri.EscapeDataString(imageFolderName)}/{file})").Append('\n');
            }
            sb.Append('\n');
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string EscapeInline(string s) =>
        s.Replace("\\", "\\\\").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("<", "\\<");
}
