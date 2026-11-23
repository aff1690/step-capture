using System.Net;
using System.Text;
using StepCapture.Core.Model;

namespace StepCapture.Core.Export;

/// <summary>A single, self-contained HTML file with the screenshots embedded.</summary>
public sealed class HtmlExporter : IGuideExporter
{
    public string Name => "Web page (HTML)";
    public string Extension => ".html";

    public void Export(Guide guide, string path, ExportOptions? options = null) =>
        File.WriteAllText(path, Render(guide, options?.Letterhead), Encoding.UTF8);

    public static string Render(Guide guide, Letterhead? letterhead = null)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            """);
        sb.Append($"<title>{E(guide.Title)}</title>\n");
        sb.Append("""
            <style>
              :root { --ink:#1d2330; --muted:#5b6475; --line:#e3e6ec; --accent:#e8590c; --bg:#f6f7f9; }
              * { box-sizing: border-box; }
              body { margin:0; background:var(--bg); color:var(--ink); font:16px/1.5 "Segoe UI", system-ui, sans-serif; }
              main { max-width: 960px; margin: 0 auto; padding: 32px 16px 64px; }
              h1 { font-size: 28px; margin: 0 0 4px; }
              .meta { color: var(--muted); margin: 0 0 8px; }
              .desc { margin: 0 0 24px; white-space: pre-wrap; }
              ol { list-style: none; padding: 0; margin: 0; counter-reset: step; }
              li { background:#fff; border:1px solid var(--line); border-radius:10px; padding:16px; margin:0 0 16px; }
              .head { display:flex; gap:12px; align-items:flex-start; }
              .num { flex:none; width:30px; height:30px; border-radius:50%; background:var(--accent); color:#fff;
                     display:flex; align-items:center; justify-content:center; font-weight:600; font-size:14px; }
              .cap { font-weight:600; padding-top:3px; }
              li.overview { border-left:4px solid var(--accent); }
              li.overview .cap { font-size:19px; padding-top:0; }
              li.overview .notes { margin-left:0; }
              .notes { color:var(--muted); margin:6px 0 0 42px; }
              .notes p { margin:0; min-height:1.5em; white-space:pre-wrap; }
              .badge { display:inline-flex; align-items:center; justify-content:center; min-width:22px; height:22px; padding:0 4px;
                       border-radius:11px; font-weight:700; font-size:12px; margin-right:6px; vertical-align:1px; }
              .letterhead img { border:0; border-radius:0; margin:0 0 24px; }
              .footer { color:var(--muted); font-size:13px; text-align:center; margin-top:32px; white-space:pre-line; }
              img { display:block; max-width:100%; height:auto; margin:12px 0 0; border:1px solid var(--line); border-radius:6px; }
              @media print { body { background:#fff; } li { break-inside: avoid; border-color:#ccc; } }
            </style>
            </head>
            <body>
            <main>

            """);
        if (letterhead?.HeaderImage is { Length: > 0 } banner)
            sb.Append($"<div class=\"letterhead\"><img alt=\"\" src=\"data:{letterhead.HeaderImageType};base64,{Convert.ToBase64String(banner)}\"></div>\n");
        sb.Append($"<h1>{E(guide.Title)}</h1>\n");
        sb.Append($"<p class=\"meta\">{guide.StepCountText} · {E(guide.Created.ToString("MMMM d, yyyy"))}</p>\n");
        if (!string.IsNullOrWhiteSpace(guide.Description))
            sb.Append($"<p class=\"desc\">{E(guide.Description)}</p>\n");
        sb.Append("<ol>\n");
        var numbers = guide.StepNumbers();
        for (int i = 0; i < guide.Steps.Count; i++)
        {
            var step = guide.Steps[i];
            int? n = numbers[i];
            sb.Append(n is null ? "<li class=\"overview\">\n<div class=\"head\">" : "<li>\n<div class=\"head\">");
            if (n is not null) sb.Append($"<span class=\"num\">{n}</span>");
            sb.Append($"<span class=\"cap\">{E(step.Caption)}</span></div>\n");
            if (!string.IsNullOrWhiteSpace(step.Notes))
                sb.Append(RenderNotes(step));
            if (step.ImagePng is { Length: > 0 } png)
                sb.Append($"<img alt=\"{(n is null ? "" : $"Step {n}: ")}{E(step.Caption)}\" src=\"data:image/png;base64,{Convert.ToBase64String(png)}\">\n");
            sb.Append("</li>\n");
        }
        sb.Append("</ol>\n");
        if (!string.IsNullOrWhiteSpace(letterhead?.FooterText))
            sb.Append($"<p class=\"footer\">{E(letterhead.FooterText)}</p>\n");
        sb.Append("</main>\n</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>Notes with formatting and indentation; numbered lines get the matching callout's badge.</summary>
    internal static string RenderNotes(Step step)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var colors = RichText.CalloutColors(step);
        var sb = new StringBuilder("<div class=\"notes\">");
        foreach (var line in RichText.Parse(step.Notes))
        {
            var content = line;
            string badge = "";
            if (RichText.TryLeadingNumber(line, out int n, out var rest) && colors.TryGetValue(n, out uint argb))
            {
                content = rest;
                string bg = $"#{argb & 0xFFFFFF:X6}", fg = MarkupGeometry.IsLight(argb) ? "#1d2330" : "#fff";
                badge = $"<span class=\"badge\" style=\"background:{bg};color:{fg}\">{n}</span>";
            }
            sb.Append(line.Indent > 0 ? $"<p style=\"padding-left:{line.Indent * 24}px\">" : "<p>").Append(badge);
            foreach (var span in content.Spans)
            {
                string t = E(span.Text);
                if (span.Italic) t = $"<em>{t}</em>";
                if (span.Bold) t = $"<strong>{t}</strong>";
                if (span.Underline) t = $"<u>{t}</u>";
                sb.Append(t);
            }
            sb.Append("</p>");
        }
        return sb.Append("</div>\n").ToString();
    }
}
