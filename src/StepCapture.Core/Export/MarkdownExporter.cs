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
            {
                sb.Append('\n').Append(RichMarkdown(step.Notes, indent)).Append('\n');
            }
            if (step.ImagePng is { Length: > 0 } png)
            {
                Directory.CreateDirectory(imageDir);
                string file = $"step-{i + 1:D3}.png";
                File.WriteAllBytes(Path.Combine(imageDir, file), png);
                string alt = n is null ? EscapeInline(step.Caption) : $"Step {n}";
                sb.Append('\n').Append($"{indent}![{alt}]({Uri.EscapeDataString(imageFolderName)}/{file})").Append('\n');
            }
            foreach (var (note, number, _) in CalloutNotes.ForExport(step))
            {
                sb.Append('\n').Append(indent).Append($"**({number})** ").Append(EscapeInline(note.Title)).Append('\n');
                if (!string.IsNullOrWhiteSpace(note.Details))
                    sb.Append('\n').Append(RichMarkdown(note.Details, indent + "    ")).Append('\n');
            }
            sb.Append('\n');
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    /// Formatted text as Markdown lines. List items become "1." / "-" items nested by 4 spaces per level;
    /// two trailing spaces keep other lines on their own line in Markdown viewers.
    /// </summary>
    private static string RichMarkdown(string? text, string indent)
    {
        var lines = RichText.Parse(text);
        var labels = RichText.ListLabels(lines, markdownStyle: true);
        return string.Join("  \n", lines.Select((l, i) =>
            indent + new string(' ', l.Indent * 4) + (l.List == ListKind.None ? "" : labels[i] + " ") + InlineMarkdown(l.Spans)));
    }

    /// <summary>Bold and italic as Markdown; underline has no Markdown syntax, so it uses &lt;u&gt;.</summary>
    private static string InlineMarkdown(IEnumerable<RichSpan> spans)
    {
        var sb = new StringBuilder();
        foreach (var s in spans)
        {
            string core = s.Text.Trim();
            if (core.Length == 0 || (!s.Bold && !s.Italic && !s.Underline)) { sb.Append(EscapeInline(s.Text)); continue; }
            int lead = s.Text.Length - s.Text.TrimStart().Length, trail = s.Text.Length - s.Text.TrimEnd().Length;
            string t = EscapeInline(core);
            if (s.Italic) t = $"*{t}*";
            if (s.Bold) t = $"**{t}**";
            if (s.Underline) t = $"<u>{t}</u>";
            sb.Append(s.Text[..lead]).Append(t).Append(s.Text[^trail..]);
        }
        return sb.ToString();
    }

    private static string EscapeInline(string s) =>
        s.Replace("\\", "\\\\").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("<", "\\<");
}
