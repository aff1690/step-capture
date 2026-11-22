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
              .notes { color:var(--muted); margin:6px 0 0 42px; white-space:pre-wrap; }
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
        sb.Append($"<p class=\"meta\">{guide.Steps.Count} steps · {E(guide.Created.ToString("MMMM d, yyyy"))}</p>\n");
        if (!string.IsNullOrWhiteSpace(guide.Description))
            sb.Append($"<p class=\"desc\">{E(guide.Description)}</p>\n");
        sb.Append("<ol>\n");
        int n = 0;
        foreach (var step in guide.Steps)
        {
            n++;
            sb.Append("<li>\n<div class=\"head\">");
            sb.Append($"<span class=\"num\">{n}</span><span class=\"cap\">{E(step.Caption)}</span></div>\n");
            if (!string.IsNullOrWhiteSpace(step.Notes))
                sb.Append($"<p class=\"notes\">{E(step.Notes)}</p>\n");
            if (step.ImagePng is { Length: > 0 } png)
                sb.Append($"<img alt=\"Step {n}: {E(step.Caption)}\" src=\"data:image/png;base64,{Convert.ToBase64String(png)}\">\n");
            sb.Append("</li>\n");
        }
        sb.Append("</ol>\n");
        if (!string.IsNullOrWhiteSpace(letterhead?.FooterText))
            sb.Append($"<p class=\"footer\">{E(letterhead.FooterText)}</p>\n");
        sb.Append("</main>\n</body>\n</html>\n");
        return sb.ToString();
    }
}
