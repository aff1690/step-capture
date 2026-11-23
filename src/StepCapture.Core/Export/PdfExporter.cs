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

    public void Export(Guide guide, string path, ExportOptions? options = null)
    {
        GuideFontResolver.EnsureInstalled();

        using var doc = new PdfDocument();
        doc.Info.Title = guide.Title;
        doc.Info.Creator = "StepCapture";

        var titleFont = new XFont(Family, 22, XFontStyleEx.Bold);
        var metaFont = new XFont(Family, 10, XFontStyleEx.Regular);
        var captionFont = new XFont(Family, 13, XFontStyleEx.Bold);
        var numFont = new XFont(Family, 10, XFontStyleEx.Bold);
        var overviewFont = new XFont(Family, 16, XFontStyleEx.Bold);
        var bodyFont = new XFont(Family, 11, XFontStyleEx.Regular);
        var accent = new XSolidBrush(XColor.FromArgb(0xE8, 0x59, 0x0C));
        var muted = new XSolidBrush(XColor.FromArgb(0x5B, 0x64, 0x75));
        var border = new XPen(XColor.FromArgb(0xD0, 0xD4, 0xDC), 0.75);

        var page = new PageCursor(doc, options?.Letterhead);
        double width = page.ContentWidth;

        page.DrawLines(Wrap(page.Gfx, guide.Title, titleFont, width), titleFont, XBrushes.Black, 28);
        page.DrawLines(new[] { $"{guide.NumberedStepCount} steps · {guide.Created:MMMM d, yyyy}" }, metaFont, muted, 14);
        page.Y += 6;
        if (!string.IsNullOrWhiteSpace(guide.Description))
        {
            page.DrawLines(Wrap(page.Gfx, guide.Description.Trim(), bodyFont, width), bodyFont, XBrushes.Black, 15);
            page.Y += 8;
        }

        var numbers = guide.StepNumbers();
        for (int i = 0; i < guide.Steps.Count; i++)
        {
            var step = guide.Steps[i];
            int? n = numbers[i];
            bool overview = n is null;
            // Overview steps: a larger heading with no number badge, text aligned to the margin.
            double badge = overview ? 0 : 20, gap = overview ? 0 : 8;
            var capFont = overview ? overviewFont : captionFont;
            double capLine = overview ? 20 : 17;
            var captionLines = Wrap(page.Gfx, step.Caption, capFont, width - badge - gap);
            var noteLines = string.IsNullOrWhiteSpace(step.Notes)
                ? new List<string>()
                : Wrap(page.Gfx, step.Notes.Trim(), bodyFont, width - badge - gap);

            double textHeight = Math.Max(badge, captionLines.Count * capLine) + noteLines.Count * 15;
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
            double spaceAbove = overview ? 24 : 18;
            page.EnsureSpace(spaceAbove + textHeight + (image is null ? 0 : imgH + 10));
            page.Y += spaceAbove;

            double top = page.Y;
            if (!overview)
            {
                page.Gfx.DrawEllipse(accent, page.Left, top, badge, badge);
                page.Gfx.DrawString(n!.Value.ToString(), numFont, XBrushes.White,
                    new XRect(page.Left, top, badge, badge), XStringFormats.Center);
            }

            double textX = page.Left + badge + gap;
            double y = top + (overview ? 0 : 2);
            foreach (var line in captionLines)
            {
                page.Gfx.DrawString(line, capFont, XBrushes.Black, new XRect(textX, y, width, capLine), XStringFormats.TopLeft);
                y += capLine;
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

    /// <summary>Tracks the current page and vertical position, adding pages as needed, and draws the letterhead.</summary>
    private sealed class PageCursor
    {
        private const double LetterheadEdge = 36; // header/footer distance from the page edge (0.5 inch)
        private readonly PdfDocument _doc;
        private readonly Letterhead? _letterhead;
        private readonly XImage? _banner;
        private readonly XFont _footerFont = new(Family, 8.5, XFontStyleEx.Regular);
        private readonly XBrush _footerBrush = new XSolidBrush(XColor.FromArgb(0x5B, 0x64, 0x75));
        public XGraphics Gfx { get; private set; } = null!;
        public double Y { get; set; }
        public double Left => Margin;
        public double ContentWidth { get; private set; }
        public double ContentHeight { get; private set; }
        private double _top, _bottom;

        public PageCursor(PdfDocument doc, Letterhead? letterhead)
        {
            _doc = doc;
            _letterhead = letterhead;
            if (letterhead?.HeaderImage is { Length: > 0 } bytes)
                _banner = XImage.FromStream(new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true));
            NewPage();
        }

        public void EnsureSpace(double height)
        {
            if (Y + height > _bottom && Y > _top + 1) NewPage();
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
            double pageW = page.Width.Point, pageH = page.Height.Point;
            ContentWidth = pageW - 2 * Margin;
            _top = Margin;
            _bottom = pageH - Margin;

            if (_banner is not null)
            {
                // Same width as in the Word template, capped to the content width.
                double w = _letterhead!.HeaderImageWidth > 0 ? Math.Min(_letterhead.HeaderImageWidth, ContentWidth) : ContentWidth;
                double h = w * _banner.PixelHeight / _banner.PixelWidth;
                Gfx.DrawImage(_banner, (pageW - w) / 2, LetterheadEdge, w, h);
                _top = Math.Max(Margin, LetterheadEdge + h + 18);
            }
            if (!string.IsNullOrWhiteSpace(_letterhead?.FooterText))
            {
                var lines = Wrap(Gfx, _letterhead.FooterText, _footerFont, ContentWidth);
                double lineH = 11, y = pageH - LetterheadEdge - lines.Count * lineH;
                foreach (var line in lines)
                {
                    Gfx.DrawString(line, _footerFont, _footerBrush, new XRect(Margin, y, ContentWidth, lineH), XStringFormats.TopCenter);
                    y += lineH;
                }
                _bottom = Math.Min(_bottom, pageH - LetterheadEdge - lines.Count * lineH - 14);
            }

            ContentHeight = _bottom - _top;
            Y = _top;
        }

        public void Finish()
        {
            Gfx?.Dispose();
            _banner?.Dispose();
        }
    }
}
