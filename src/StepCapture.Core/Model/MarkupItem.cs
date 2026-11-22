namespace StepCapture.Core.Model;

public enum MarkupKind { Arrow, Line, Rectangle, Ellipse, Pen, Highlight, Text, Number }

/// <summary>A point in image pixels.</summary>
public readonly record struct PointD(double X, double Y)
{
    public PointD Offset(double dx, double dy) => new(X + dx, Y + dy);
}

/// <summary>
/// One editable shape drawn on a screenshot (arrow, box, text...). Coordinates and sizes are in image pixels.
/// The shapes are kept separately from the screenshot so they can be selected, moved, edited and deleted later.
/// </summary>
public sealed class MarkupItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MarkupKind Kind { get; set; }

    /// <summary>
    /// Two points (start and end, or opposite corners) for arrows, lines, boxes, circles and highlights;
    /// every sampled point for the pen; the top-left corner for text; the center for numbered callouts.
    /// </summary>
    public List<PointD> Points { get; set; } = new();

    /// <summary>Color as 0xAARRGGBB.</summary>
    public uint Color { get; set; } = 0xFFE5484D;

    public double StrokeWidth { get; set; } = 5;
    public double FontSize { get; set; } = 30;
    public string Text { get; set; } = "";

    public MarkupItem Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        Points = new List<PointD>(Points),
        Color = Color,
        StrokeWidth = StrokeWidth,
        FontSize = FontSize,
        Text = Text,
    };

    public void Move(double dx, double dy)
    {
        for (int i = 0; i < Points.Count; i++) Points[i] = Points[i].Offset(dx, dy);
    }
}
