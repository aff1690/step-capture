using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StepCapture.Core.Model;

namespace StepCapture.App.Imaging;

/// <summary>
/// Draws markup shapes with WPF. The editor shows exactly these drawings on screen, and exports use the same
/// drawings rendered into the PNG, so what you see while editing is what ends up in the guide.
/// All coordinates are image pixels.
/// </summary>
internal static class MarkupRenderer
{
    private static readonly Typeface BoldFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public static uint ToArgb(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    private static Point P(PointD p) => new(p.X, p.Y);

    private static Rect Box(PointD a, PointD b) => new(P(a), P(b));

    private static Pen RoundPen(Brush brush, double width) => new(brush, width)
    {
        StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
    };

    /// <summary>The drawing for one shape.</summary>
    public static Drawing Build(MarkupItem m)
    {
        var group = new DrawingGroup();
        var pts = m.Points;
        if (pts.Count == 0) return group;
        var brush = new SolidColorBrush(ToColor(m.Color));
        var pen = RoundPen(brush, m.StrokeWidth);

        switch (m.Kind)
        {
            case MarkupKind.Line when pts.Count >= 2:
                group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(P(pts[0]), P(pts[^1]))));
                break;

            case MarkupKind.Arrow when pts.Count >= 2:
            {
                var (tip, left, right, shaftEnd) = MarkupGeometry.ArrowHead(pts[0], pts[^1], m.StrokeWidth);
                group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(P(pts[0]), P(shaftEnd))));
                var head = new StreamGeometry();
                using (var ctx = head.Open())
                {
                    ctx.BeginFigure(P(tip), isFilled: true, isClosed: true);
                    ctx.LineTo(P(left), true, false);
                    ctx.LineTo(P(right), true, false);
                }
                group.Children.Add(new GeometryDrawing(brush, RoundPen(brush, Math.Max(1, m.StrokeWidth * 0.4)), head));
                break;
            }

            case MarkupKind.Rectangle when pts.Count >= 2:
                group.Children.Add(new GeometryDrawing(null, pen, new RectangleGeometry(Box(pts[0], pts[^1]))));
                break;

            case MarkupKind.Ellipse when pts.Count >= 2:
            {
                var r = Box(pts[0], pts[^1]);
                group.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(r)));
                break;
            }

            case MarkupKind.Highlight when pts.Count >= 2:
            {
                var c = ToColor(m.Color);
                var fill = new SolidColorBrush(Color.FromArgb(90, c.R, c.G, c.B));
                group.Children.Add(new GeometryDrawing(fill, null, new RectangleGeometry(Box(pts[0], pts[^1]))));
                break;
            }

            case MarkupKind.Pen:
                if (pts.Count == 1)
                {
                    group.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(P(pts[0]), m.StrokeWidth / 2, m.StrokeWidth / 2)));
                }
                else
                {
                    var line = new StreamGeometry();
                    using (var ctx = line.Open())
                    {
                        ctx.BeginFigure(P(pts[0]), isFilled: false, isClosed: false);
                        for (int i = 1; i < pts.Count; i++) ctx.LineTo(P(pts[i]), true, true);
                    }
                    group.Children.Add(new GeometryDrawing(null, pen, line));
                }
                break;

            case MarkupKind.Number:
            {
                double radius = MarkupGeometry.CalloutRadius(m.FontSize);
                var center = P(pts[0]);
                group.Children.Add(new GeometryDrawing(brush, new Pen(Brushes.White, Math.Max(2, m.FontSize / 12)),
                    new EllipseGeometry(center, radius, radius)));
                var text = Formatted(m.Text, m.FontSize * 0.75);
                var origin = new Point(center.X - text.Width / 2, center.Y - text.Height / 2);
                var textBrush = MarkupGeometry.IsLight(m.Color) ? new SolidColorBrush(Color.FromRgb(26, 31, 43)) : Brushes.White;
                group.Children.Add(new GeometryDrawing(textBrush, null, text.BuildGeometry(origin)));
                break;
            }

            case MarkupKind.Text when !string.IsNullOrEmpty(m.Text):
            {
                // Outlined text so it stays readable on any background.
                var geometry = Formatted(m.Text, m.FontSize).BuildGeometry(P(pts[0]));
                var halo = MarkupGeometry.IsLight(m.Color)
                    ? new SolidColorBrush(Color.FromArgb(220, 26, 31, 43))
                    : new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
                group.Children.Add(new GeometryDrawing(null, RoundPen(halo, Math.Max(3, m.FontSize / 5)), geometry));
                group.Children.Add(new GeometryDrawing(brush, null, geometry));
                break;
            }
        }
        return group;
    }

    /// <summary>Area a shape covers, including its outline.</summary>
    public static Rect Bounds(MarkupItem m)
    {
        var b = Build(m).Bounds;
        if (b.IsEmpty && m.Points.Count > 0) b = new Rect(P(m.Points[0]), new Size(1, 1));
        return b;
    }

    /// <summary>All shapes as one drawing the size of the screenshot, for display over the image.</summary>
    public static DrawingImage BuildLayer(IEnumerable<MarkupItem> items, int width, int height)
    {
        var area = new Rect(0, 0, width, height);
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(area) };
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(area))); // fixes the size
        foreach (var item in items) group.Children.Add(Build(item));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// <summary>Renders the screenshot with its markup into a new PNG.</summary>
    public static byte[] Render(byte[] basePng, IReadOnlyCollection<MarkupItem> items)
    {
        var source = Decode(basePng);
        int w = source.PixelWidth, h = source.PixelHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, w, h));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
            foreach (var item in items) dc.DrawDrawing(Build(item));
            dc.Pop();
        }
        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static BitmapSource Decode(byte[] png)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(png);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static FormattedText Formatted(string text, double size) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, BoldFace, Math.Max(1, size), Brushes.Black, 1.0);
}
