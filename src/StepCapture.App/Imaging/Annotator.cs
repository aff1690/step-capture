using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static StepCapture.App.Native.NativeMethods;

namespace StepCapture.App.Imaging;

/// <summary>Draws click highlights and the cursor onto screenshots, and edits saved step images.</summary>
internal static class Annotator
{
    private static readonly Color Accent = Color.FromArgb(232, 89, 12);

    /// <summary>Scale factor so highlights look the same on 1080p and 4K screens.</summary>
    private static float ScaleFor(Bitmap b) => MarkupGeometry.ScaleFor(b.Width, b.Height);

    /// <summary>Highlights the click point with a ring, draws the cursor on top and returns a PNG.</summary>
    public static byte[] RenderClick(CapturedScreen shot, Point click)
    {
        var bmp = shot.Bitmap;
        float s = ScaleFor(bmp);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float r = 24 * s;
            var rect = new RectangleF(click.X - r, click.Y - r, 2 * r, 2 * r);
            using var fill = new SolidBrush(Color.FromArgb(60, Accent));
            using var halo = new Pen(Color.FromArgb(150, Color.White), 7 * s);
            using var ring = new Pen(Color.FromArgb(235, Accent), 3.5f * s);
            g.FillEllipse(fill, rect);
            g.DrawEllipse(halo, rect);
            g.DrawEllipse(ring, rect);
            DrawCursor(g, shot);
        }
        return ToPng(bmp);
    }

    /// <summary>Outlines the focused control (if known) and returns a PNG.</summary>
    public static byte[] RenderFocus(CapturedScreen shot, Rectangle? screenBounds)
    {
        var bmp = shot.Bitmap;
        if (screenBounds is { } b && b.Width > 0 && b.Height > 0)
        {
            float s = ScaleFor(bmp);
            var rect = new Rectangle(b.X - shot.Bounds.X, b.Y - shot.Bounds.Y, b.Width, b.Height);
            rect.Inflate((int)(4 * s), (int)(4 * s));
            rect.Intersect(new Rectangle(Point.Empty, bmp.Size));
            if (rect.Width > 0 && rect.Height > 0)
            {
                using var g = Graphics.FromImage(bmp);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var halo = new Pen(Color.FromArgb(150, Color.White), 7 * s);
                using var pen = new Pen(Color.FromArgb(235, Accent), 3.5f * s);
                using var path = RoundedRect(rect, 6 * s);
                g.DrawPath(halo, path);
                g.DrawPath(pen, path);
            }
        }
        return ToPng(bmp);
    }

    /// <summary>Pixelates a region of a PNG so text in it can't be read.</summary>
    public static byte[] Pixelate(byte[] png, Rectangle region)
    {
        using var bmp = Load(png);
        region.Intersect(new Rectangle(Point.Empty, bmp.Size));
        if (region.Width < 2 || region.Height < 2) return png;

        int block = Math.Max(8, Math.Min(region.Width, region.Height) / 12);
        int w = Math.Max(1, region.Width / block), h = Math.Max(1, region.Height / block);
        using var small = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(bmp, new Rectangle(0, 0, w, h), region, GraphicsUnit.Pixel);
        }
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(small, region, new Rectangle(0, 0, w, h), GraphicsUnit.Pixel);
        }
        return ToPng(bmp);
    }

    /// <summary>Crops a PNG to a region.</summary>
    public static byte[] Crop(byte[] png, Rectangle region)
    {
        using var bmp = Load(png);
        region.Intersect(new Rectangle(Point.Empty, bmp.Size));
        if (region.Width < 8 || region.Height < 8) return png;
        using var cropped = bmp.Clone(region, PixelFormat.Format24bppRgb);
        return ToPng(cropped);
    }

    /// <summary>Draws one markup shape onto a PNG and returns the new PNG.</summary>
    public static byte[] DrawMarkup(byte[] png, MarkupShape shape)
    {
        using var bmp = Load(png);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            DrawShape(g, shape);
        }
        return ToPng(bmp);
    }

    private static void DrawShape(Graphics g, MarkupShape s)
    {
        var pts = s.Points;
        if (pts.Count == 0) return;
        using var pen = new Pen(s.Color, s.StrokeWidth)
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round,
        };

        switch (s.Kind)
        {
            case MarkupKind.Line when pts.Count >= 2:
                g.DrawLine(pen, pts[0], pts[^1]);
                break;

            case MarkupKind.Arrow when pts.Count >= 2:
            {
                var (tip, left, right, shaftEnd) = MarkupGeometry.ArrowHead(pts[0], pts[^1], s.StrokeWidth);
                g.DrawLine(pen, pts[0], shaftEnd);
                using var brush = new SolidBrush(s.Color);
                using var outline = new Pen(s.Color, Math.Max(1f, s.StrokeWidth * 0.4f)) { LineJoin = LineJoin.Round };
                var head = new[] { tip, left, right };
                g.FillPolygon(brush, head);
                g.DrawPolygon(outline, head);
                break;
            }

            case MarkupKind.Rectangle when pts.Count >= 2:
            {
                var r = MarkupGeometry.Normalize(pts[0], pts[^1]);
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                break;
            }

            case MarkupKind.Ellipse when pts.Count >= 2:
                g.DrawEllipse(pen, MarkupGeometry.Normalize(pts[0], pts[^1]));
                break;

            case MarkupKind.Highlight when pts.Count >= 2:
            {
                using var brush = new SolidBrush(Color.FromArgb(90, s.Color));
                g.FillRectangle(brush, MarkupGeometry.Normalize(pts[0], pts[^1]));
                break;
            }

            case MarkupKind.Pen:
                if (pts.Count == 1)
                {
                    using var dot = new SolidBrush(s.Color);
                    float d = s.StrokeWidth;
                    g.FillEllipse(dot, pts[0].X - d / 2, pts[0].Y - d / 2, d, d);
                }
                else
                {
                    g.DrawLines(pen, pts.ToArray());
                }
                break;

            case MarkupKind.Number:
            {
                float radius = MarkupGeometry.CalloutRadius(s.FontSize);
                var c = pts[0];
                var circle = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
                using var fill = new SolidBrush(s.Color);
                using var ring = new Pen(Color.White, Math.Max(2f, s.FontSize / 12f));
                g.FillEllipse(fill, circle);
                g.DrawEllipse(ring, circle);
                using var font = new Font("Segoe UI", s.FontSize * 0.75f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var textBrush = new SolidBrush(MarkupGeometry.IsLight(s.Color) ? Color.FromArgb(26, 31, 43) : Color.White);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(s.Text, font, textBrush, circle, format);
                break;
            }

            case MarkupKind.Text when !string.IsNullOrWhiteSpace(s.Text):
            {
                // Outlined text so it stays readable on any background.
                using var path = new GraphicsPath();
                using var family = new FontFamily("Segoe UI");
                path.AddString(s.Text, family, (int)FontStyle.Bold, s.FontSize, pts[0], StringFormat.GenericTypographic);
                var haloColor = MarkupGeometry.IsLight(s.Color) ? Color.FromArgb(220, 26, 31, 43) : Color.FromArgb(235, Color.White);
                using var halo = new Pen(haloColor, Math.Max(3f, s.FontSize / 5f)) { LineJoin = LineJoin.Round };
                using var fill = new SolidBrush(s.Color);
                g.DrawPath(halo, path);
                g.FillPath(fill, path);
                break;
            }
        }
    }

    private static void DrawCursor(Graphics g, CapturedScreen shot)
    {
        if (shot.Cursor == IntPtr.Zero) return;
        IntPtr hdc = g.GetHdc();
        try { DrawIconEx(hdc, shot.CursorOrigin.X, shot.CursorOrigin.Y, shot.Cursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL); }
        finally { g.ReleaseHdc(hdc); }
    }

    private static GraphicsPath RoundedRect(Rectangle r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Bitmap Load(byte[] png)
    {
        using var ms = new MemoryStream(png);
        using var img = Image.FromStream(ms);
        return new Bitmap(img); // detach from the stream
    }

    private static byte[] ToPng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
