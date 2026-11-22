namespace StepCapture.Core.Model;

/// <summary>Shared math for drawing and editing markup, so the editor and exports agree.</summary>
public static class MarkupGeometry
{
    /// <summary>Scale so markup looks the same size on 1080p and 4K screenshots.</summary>
    public static double ScaleFor(int width, int height) => Math.Max(1.0, Math.Min(width / 1920.0, height / 1080.0));

    public static readonly double[] StrokeWidths = { 3, 5, 8 };   // small, medium, large at 1080p
    public static readonly double[] FontSizes = { 22, 30, 42 };

    public static double HeadLength(double strokeWidth) => Math.Max(14, strokeWidth * 4.5);

    public static double CalloutRadius(double fontSize) => fontSize * 0.8;

    /// <summary>The arrow's head triangle (tip, left, right) and where the shaft should stop.</summary>
    public static (PointD Tip, PointD Left, PointD Right, PointD ShaftEnd) ArrowHead(PointD from, PointD to, double strokeWidth)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double len = Math.Max(0.001, Math.Sqrt(dx * dx + dy * dy));
        double ux = dx / len, uy = dy / len;
        double head = Math.Min(HeadLength(strokeWidth), len * 0.6);
        double half = head * 0.45;
        var baseCenter = new PointD(to.X - ux * head, to.Y - uy * head);
        var left = new PointD(baseCenter.X - uy * half, baseCenter.Y + ux * half);
        var right = new PointD(baseCenter.X + uy * half, baseCenter.Y - ux * half);
        var shaftEnd = new PointD(to.X - ux * head * 0.8, to.Y - uy * head * 0.8);
        return (to, left, right, shaftEnd);
    }

    /// <summary>Holding Shift snaps lines to 45° steps and boxes or circles to squares.</summary>
    public static PointD Constrain(MarkupKind kind, PointD start, PointD end)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        switch (kind)
        {
            case MarkupKind.Arrow or MarkupKind.Line:
                double angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
                double len = Math.Sqrt(dx * dx + dy * dy);
                return new PointD(start.X + len * Math.Cos(angle), start.Y + len * Math.Sin(angle));
            case MarkupKind.Rectangle or MarkupKind.Ellipse or MarkupKind.Highlight:
                double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
                return new PointD(start.X + Math.Sign(dx) * side, start.Y + Math.Sign(dy) * side);
            default:
                return end;
        }
    }

    /// <summary>Distance from a point to a line segment.</summary>
    public static double DistanceToSegment(PointD p, PointD a, PointD b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        double t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        double cx = a.X + t * dx - p.X, cy = a.Y + t * dy - p.Y;
        return Math.Sqrt(cx * cx + cy * cy);
    }

    /// <summary>True for light colors (yellow, white) that need dark text or a dark outline.</summary>
    public static bool IsLight(uint argb)
    {
        double r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        return (0.299 * r + 0.587 * g + 0.114 * b) / 255 > 0.62;
    }

    /// <summary>Numbers callouts 1, 2, 3… in the order they were added, so deleting one closes the gap.</summary>
    public static void RenumberCallouts(IEnumerable<MarkupItem> items)
    {
        int n = 1;
        foreach (var item in items)
            if (item.Kind == MarkupKind.Number) item.Text = (n++).ToString();
    }

    /// <summary>Moves every shape after cropping so it stays over the same part of the picture.</summary>
    public static void ShiftAll(IEnumerable<MarkupItem> items, double dx, double dy)
    {
        foreach (var item in items) item.Move(dx, dy);
    }
}
