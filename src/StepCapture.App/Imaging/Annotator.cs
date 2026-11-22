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
    private static float ScaleFor(Bitmap b) => Math.Max(1f, Math.Min(b.Width / 1920f, b.Height / 1080f));

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
