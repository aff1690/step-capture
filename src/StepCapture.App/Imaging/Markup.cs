using System.Drawing;

namespace StepCapture.App.Imaging;

public enum MarkupKind { Arrow, Line, Rectangle, Ellipse, Pen, Highlight, Text, Number }

/// <summary>One shape drawn on a screenshot. Coordinates and sizes are in image pixels.</summary>
public sealed record MarkupShape(
    MarkupKind Kind,
    IReadOnlyList<PointF> Points,
    Color Color,
    float StrokeWidth,
    float FontSize,
    string Text = "");

/// <summary>Shared geometry so the on-screen preview and the final drawing match.</summary>
public static class MarkupGeometry
{
    /// <summary>Scale so markup looks the same size on 1080p and 4K screenshots.</summary>
    public static float ScaleFor(int width, int height) => Math.Max(1f, Math.Min(width / 1920f, height / 1080f));

    public static readonly float[] StrokeWidths = { 3f, 5f, 8f };   // small, medium, large at 1080p
    public static readonly float[] FontSizes = { 22f, 30f, 42f };

    public static float HeadLength(float strokeWidth) => Math.Max(14f, strokeWidth * 4.5f);

    /// <summary>The arrow's head triangle (tip, left, right) and where the shaft should stop.</summary>
    public static (PointF Tip, PointF Left, PointF Right, PointF ShaftEnd) ArrowHead(PointF from, PointF to, float strokeWidth)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float len = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
        float ux = dx / len, uy = dy / len;
        float head = MathF.Min(HeadLength(strokeWidth), len * 0.6f);
        float half = head * 0.45f;
        var baseCenter = new PointF(to.X - ux * head, to.Y - uy * head);
        var left = new PointF(baseCenter.X - uy * half, baseCenter.Y + ux * half);
        var right = new PointF(baseCenter.X + uy * half, baseCenter.Y - ux * half);
        var shaftEnd = new PointF(to.X - ux * head * 0.8f, to.Y - uy * head * 0.8f);
        return (to, left, right, shaftEnd);
    }

    /// <summary>Holding Shift snaps lines to 45° steps and boxes to squares.</summary>
    public static PointF Constrain(MarkupKind kind, PointF start, PointF end)
    {
        float dx = end.X - start.X, dy = end.Y - start.Y;
        switch (kind)
        {
            case MarkupKind.Arrow or MarkupKind.Line:
                double angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
                float len = MathF.Sqrt(dx * dx + dy * dy);
                return new PointF(start.X + len * (float)Math.Cos(angle), start.Y + len * (float)Math.Sin(angle));
            case MarkupKind.Rectangle or MarkupKind.Ellipse or MarkupKind.Highlight:
                float side = Math.Max(Math.Abs(dx), Math.Abs(dy));
                return new PointF(start.X + Math.Sign(dx) * side, start.Y + Math.Sign(dy) * side);
            default:
                return end;
        }
    }

    public static RectangleF Normalize(PointF a, PointF b) =>
        RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public static float CalloutRadius(float fontSize) => fontSize * 0.8f;

    /// <summary>Dark text on light colors (yellow, white), white text otherwise.</summary>
    public static bool IsLight(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255 > 0.62;
}
