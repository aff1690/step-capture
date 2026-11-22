using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using StepCapture.Core.Model;

namespace StepCapture.Core.Export;

/// <summary>A paginated PDF (US Letter) with one caption and screenshot per step.</summary>
public sealed class PdfExporter : IGuideExporter
{
    private const double Margin = 54;      // 0.75 inch, in points
    private const string Family = "Guide"; // resolved by GuideFontResolver

    public string Name => "PDF";
    public string Extension => ".pdf";

    public void Export(Guide guide, string path)
    {
        GuideFontResolver.EnsureInstalled();

        using var doc = new PdfDocument();
        doc.Info.Title = guide.Title;
        doc.Info.Creator = "StepCapture";

        var titleFont = new XFont(Family, 22, XFontStyleEx.Bold);
        var metaFont = new XFont(Family, 10, XFontStyleEx.Regular);
        var captionFont = new XFont(Family, 13, XFontStyleEx.Bold);
        var numFont = new XFont(Family, 10, XFontStyleEx.Bold);
        var bodyFont = new XFont(Family, 11, XFontStyleEx.Regular);
        var accent = new XSolidBrush(XColor.FromArgb(0xE8, 0x59, 0x0C));
        var muted = new XSolidBrush(XColor.FromArgb(0x5B, 0x64, 0x75));
        var border = new XPen(XColor.FromArgb(0xD0, 0xD4, 0xDC), 0.75);

        var page = new PageCursor(doc);
        double width = page.ContentWidth;

        page.DrawLines(Wrap(page.Gfx, guide.Title, titleFont, width), titleFont, XBrushes.Black, 28);
        page.DrawLines(new[] { $"{guide.Steps.Count} steps · {guide.Created:MMMM d, yyyy}" }, metaFont, muted, 14);
        page.Y += 6;
        if (!string.IsNullOrWhiteSpace(guide.Description))
        {
            page.DrawLines(Wrap(page.Gfx, guide.Description.Trim(), bodyFont, width), bodyFont, XBrushes.Black, 15);
            page.Y += 8;
        }

        int n = 0;
        foreach (var step in guide.Steps)
        {
            n++;
            const double badge = 20, gap = 8;
            var captionLines = Wrap(page.Gfx, step.Caption, captionFont, width - badge - gap);
            var noteLines = string.IsNullOrWhiteSpace(step.Notes)
                ? new List<string>()
                : Wrap(page.Gfx, step.Notes.Trim(), bodyFont, width - badge - gap);

            double textHeight = Math.Max(badge, captionLines.Count * 17) + noteLines.Count * 15;
            XImage? image = null;
            double imgW = 0, imgH = 0;
            if (step.ImagePng is { Length: > 0 } png)
            {
                image = XImage.FromStream(new MemoryStream(png, 0, png.Length, writable: false, publiclyVisible: true));
                double maxH = page.ContentHeight - textHeight - 30;
                // Never enlarge small crops beyond their natural size (1 px = 0.75 pt at 96 DPI).
                double s = Math.Min(0.75, Math.Min(width / image.PixelWidth, maxH / image.PixelHeight));
                imgW = image.PixelWidth * s;
                imgH = image.PixelHeight * s;
            }

            // Keep a step's caption and its screenshot on the same page.
            page.EnsureSpace(18 + textHeight + (image is null ? 0 : imgH + 10));
            page.Y += 18;

            double top = page.Y;
            page.Gfx.DrawEllipse(accent, page.Left, top, badge, badge);
            page.Gfx.DrawString(n.ToString(), numFont, XBrushes.White,
                new XRect(page.Left, top, badge, badge), XStringFormats.Center);

            double textX = page.Left + badge + gap;
            double y = top + 2;
            foreach (var line in captionLines)
            {
                page.Gfx.DrawString(line, captionFont, XBrushes.Black, new XRect(textX, y, width, 17), XStringFormats.TopLeft);
                y += 17;
            }
            foreach (var line in noteLines)
            {
                page.Gfx.DrawString(line, bodyFont, muted, new XRect(textX, y, width, 15), XStringFormats.TopLeft);
                y += 15;
            }
            page.Y = Math.Max(y, top + badge);

            if (image is not null)
            {
                page.Y += 10;
                page.Gfx.DrawImage(image, page.Left, page.Y, imgW, imgH);
                page.Gfx.DrawRectangle(border, page.Left, page.Y, imgW, imgH);
                page.Y += imgH;
                image.Dispose();
            }
        }

        page.Finish();
        doc.Save(path);
    }

    /// <summary>Word-wraps text to a width, honoring explicit line breaks.</summary>
    internal static List<string> Wrap(XGraphics gfx, string text, XFont font, double width)
    {
        var result = new List<string>();
        foreach (var para in text.Replace("\r\n", "\n").Split('\n'))
        {
            var words = para.Split(' ');
            string line = "";
            foreach (var word in words)
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (gfx.MeasureString(candidate, font).Width <= width || line.Length == 0)
                {
                    line = candidate;
                    continue;
                }
                result.Add(line);
                line = word;
            }
            // Hard-break single words that are still too long.
            while (line.Length > 1 && gfx.MeasureString(line, font).Width > width)
            {
                int cut = line.Length - 1;
                while (cut > 1 && gfx.MeasureString(line[..cut], font).Width > width) cut--;
                result.Add(line[..cut]);
                line = line[cut..];
            }
            result.Add(line);
        }
        return result;
    }

    /// <summary>Tracks the current page and vertical position, adding pages as needed.</summary>
    private sealed class PageCursor
    {
        private readonly PdfDocument _doc;
        public XGraphics Gfx { get; private set; } = null!;
        public double Y { get; set; }
        public double Left => Margin;
        public double ContentWidth { get; private set; }
        public double ContentHeight { get; private set; }
        private double _bottom;

        public PageCursor(PdfDocument doc)
        {
            _doc = doc;
            NewPage();
        }

        public void EnsureSpace(double height)
        {
            if (Y + height > _bottom && Y > Margin + 1) NewPage();
        }

        public void DrawLines(IEnumerable<string> lines, XFont font, XBrush brush, double lineHeight)
        {
            foreach (var line in lines)
            {
                EnsureSpace(lineHeight);
                Gfx.DrawString(line, font, brush, new XRect(Left, Y, ContentWidth, lineHeight), XStringFormats.TopLeft);
                Y += lineHeight;
            }
        }

        private void NewPage()
        {
            Gfx?.Dispose();
            var page = _doc.AddPage();
            page.Size = PageSize.Letter;
            Gfx = XGraphics.FromPdfPage(page);
            ContentWidth = page.Width.Point - 2 * Margin;
            ContentHeight = page.Height.Point - 2 * Margin;
            _bottom = page.Height.Point - Margin;
            Y = Margin;
        }

        public void Finish() => Gfx?.Dispose();
    }
}
