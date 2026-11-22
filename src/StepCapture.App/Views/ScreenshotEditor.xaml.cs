using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using StepCapture.App.Imaging;
using DColor = System.Drawing.Color;
using DPointF = System.Drawing.PointF;
using DRect = System.Drawing.Rectangle;
using WColor = System.Windows.Media.Color;

namespace StepCapture.App.Views;

/// <summary>
/// Shows a step's screenshot with a markup toolbar: arrows, lines, boxes, circles, pen, highlighter,
/// text and numbered callouts, plus select-to-blur and crop. Each finished shape is drawn into the
/// PNG straight away (so exports need nothing special) and can be undone.
/// </summary>
public partial class ScreenshotEditor : UserControl
{
    private enum Tool { Select, Arrow, Line, Rectangle, Ellipse, Pen, Highlight, Text, Number }

    private static readonly (string Name, WColor Color)[] Palette =
    {
        ("Red", WColor.FromRgb(0xE5, 0x48, 0x4D)),
        ("Orange", WColor.FromRgb(0xF7, 0x6B, 0x15)),
        ("Yellow", WColor.FromRgb(0xFF, 0xD3, 0x2C)),
        ("Green", WColor.FromRgb(0x30, 0xA4, 0x6C)),
        ("Blue", WColor.FromRgb(0x3E, 0x63, 0xDD)),
        ("Purple", WColor.FromRgb(0x8E, 0x4E, 0xC6)),
        ("Black", WColor.FromRgb(0x1A, 0x1F, 0x2B)),
        ("White", WColor.FromRgb(0xFF, 0xFF, 0xFF)),
    };

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(StepItem), typeof(ScreenshotEditor),
        new PropertyMetadata(null, (d, _) => ((ScreenshotEditor)d).OnItemChanged()));

    public StepItem? Item
    {
        get => (StepItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    /// <summary>Raised after the screenshot changed (markup, blur, crop, undo, remove).</summary>
    public event Action? Edited;

    private Tool _tool = Tool.Arrow;
    private WColor _color = Palette[0].Color;
    private int _size = 1;

    private Point? _start;
    private readonly List<Point> _penPoints = new();
    private Shape? _preview;
    private TextBox? _textEditor;
    private Point _textAnchor;

    public ScreenshotEditor()
    {
        InitializeComponent();
        foreach (var (name, color) in Palette)
        {
            var swatch = new RadioButton
            {
                GroupName = "Color", Style = (Style)FindResource("Swatch"),
                Background = new SolidColorBrush(color), ToolTip = name, Tag = color,
            };
            swatch.Checked += (_, _) => { _color = color; if (_textEditor is not null) _textEditor.Foreground = new SolidColorBrush(color); };
            Swatches.Children.Add(swatch);
        }
        ((RadioButton)Swatches.Children[0]).IsChecked = true;
        MediumSize.IsChecked = true;
        ArrowTool.IsChecked = true;
    }

    // ---------------- toolbar ----------------

    private void OnToolChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !Enum.TryParse(tag, out Tool tool)) return;
        CommitText();
        CancelDrawing();
        ClearSelection();
        _tool = tool;
        Overlay.Cursor = tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
        HintText.Text = tool switch
        {
            Tool.Select => "Drag to select an area, then blur or crop it.",
            Tool.Arrow => "Drag to draw an arrow. Hold Shift for straight angles.",
            Tool.Line => "Drag to draw a line. Hold Shift for straight angles.",
            Tool.Rectangle => "Drag to draw a box. Hold Shift for a square.",
            Tool.Ellipse => "Drag to draw a circle. Hold Shift for a perfect circle.",
            Tool.Pen => "Drag to draw freehand.",
            Tool.Highlight => "Drag over an area to highlight it.",
            Tool.Text => "Click where the text should go, type, then press Enter.",
            Tool.Number => "Click to place numbered callouts: 1, 2, 3…",
            _ => "",
        };
    }

    private void OnSizeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, out int size)) _size = size;
    }

    /// <summary>Keyboard shortcuts, called by the window when no text box has focus.</summary>
    public bool HandleShortcut(KeyEventArgs e)
    {
        if (Item is not { HasImage: true } && !(e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)) return false;
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OnUndo(this, new RoutedEventArgs());
            return true;
        }
        if (Keyboard.Modifiers != ModifierKeys.None) return false;
        string? tag = e.Key switch
        {
            Key.V => "Select", Key.A => "Arrow", Key.L => "Line", Key.R => "Rectangle", Key.E => "Ellipse",
            Key.P => "Pen", Key.H => "Highlight", Key.T => "Text", Key.N => "Number",
            Key.Escape => "Cancel",
            _ => null,
        };
        if (tag is null) return false;
        if (tag == "Cancel") { CancelDrawing(); ClearSelection(); return true; }
        foreach (var child in ((WrapPanel)Root.Children[0]).Children)
            if (child is RadioButton { GroupName: "Tool" } rb && (string)rb.Tag == tag) rb.IsChecked = true;
        return true;
    }

    // ---------------- item ----------------

    private void OnItemChanged()
    {
        CancelText();
        CancelDrawing();
        ClearSelection();
        Root.DataContext = Item;
    }

    private (int W, int H) PixelSize => Item?.PixelSize ?? (0, 0);
    private double PixelsPerUnit => Overlay.ActualWidth > 0 ? PixelSize.W / Overlay.ActualWidth : 1;
    private float ImageScale => MarkupGeometry.ScaleFor(PixelSize.W, PixelSize.H);
    private float StrokePx => MarkupGeometry.StrokeWidths[_size] * ImageScale;
    private float FontPx => MarkupGeometry.FontSizes[_size] * ImageScale;

    private DPointF ToPx(Point p) => new((float)(p.X * PixelsPerUnit), (float)(p.Y * PixelsPerUnit));
    private Point ToCanvas(DPointF p) => new(p.X / PixelsPerUnit, p.Y / PixelsPerUnit);

    private Point Clamp(Point p) =>
        new(Math.Clamp(p.X, 0, Overlay.ActualWidth), Math.Clamp(p.Y, 0, Overlay.ActualHeight));

    private DColor DrawingColor => DColor.FromArgb(_color.A, _color.R, _color.G, _color.B);

    // ---------------- mouse ----------------

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Item is not { HasImage: true }) return;
        var p = Clamp(e.GetPosition(Overlay));

        if (_textEditor is not null)
        {
            CommitText();
            e.Handled = true;
            return;
        }

        switch (_tool)
        {
            case Tool.Text:
                OpenTextEditor(p);
                e.Handled = true;
                return;

            case Tool.Number:
                Commit(new MarkupShape(MarkupKind.Number, new[] { ToPx(p) }, DrawingColor, StrokePx, FontPx,
                    Item.NextCallout.ToString()), usedCallout: true);
                return;

            case Tool.Select:
                ClearSelection();
                SelectionRect.Visibility = Visibility.Visible;
                break;

            case Tool.Pen:
                _penPoints.Clear();
                _penPoints.Add(p);
                break;
        }

        _start = p;
        Overlay.CaptureMouse();
        UpdatePreview(p);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_start is null) return;
        var p = Clamp(e.GetPosition(Overlay));
        if (_tool == Tool.Pen) _penPoints.Add(p);
        UpdatePreview(p);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_start is not { } start) return;
        Overlay.ReleaseMouseCapture();
        var end = ConstrainIfShift(start, Clamp(e.GetPosition(Overlay)));
        _start = null;

        if (_tool == Tool.Select)
        {
            bool has = SelectionRect.Width > 4 && SelectionRect.Height > 4;
            if (!has) SelectionRect.Visibility = Visibility.Collapsed;
            BlurButton.IsEnabled = CropButton.IsEnabled = has;
            return;
        }

        bool tooSmall = (end - start).Length < 3;
        MarkupShape? shape = _tool switch
        {
            Tool.Pen => new MarkupShape(MarkupKind.Pen, _penPoints.Select(ToPx).ToArray(), DrawingColor, StrokePx, FontPx),
            _ when tooSmall => null,
            Tool.Arrow => Two(MarkupKind.Arrow),
            Tool.Line => Two(MarkupKind.Line),
            Tool.Rectangle => Two(MarkupKind.Rectangle),
            Tool.Ellipse => Two(MarkupKind.Ellipse),
            Tool.Highlight => Two(MarkupKind.Highlight),
            _ => null,
        };
        CancelDrawing();
        if (shape is not null) Commit(shape);

        MarkupShape Two(MarkupKind kind) => new(kind, new[] { ToPx(start), ToPx(end) }, DrawingColor, StrokePx, FontPx);
    }

    private Point ConstrainIfShift(Point start, Point end)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return end;
        var kind = _tool switch
        {
            Tool.Arrow => MarkupKind.Arrow, Tool.Line => MarkupKind.Line, Tool.Rectangle => MarkupKind.Rectangle,
            Tool.Ellipse => MarkupKind.Ellipse, Tool.Highlight => MarkupKind.Highlight, _ => MarkupKind.Pen,
        };
        if (_tool == Tool.Select) kind = MarkupKind.Pen; // no constraint
        var c = MarkupGeometry.Constrain(kind, new DPointF((float)start.X, (float)start.Y), new DPointF((float)end.X, (float)end.Y));
        return Clamp(new Point(c.X, c.Y));
    }

    // ---------------- live preview ----------------

    private void UpdatePreview(Point current)
    {
        var start = _start!.Value;
        current = ConstrainIfShift(start, current);
        double stroke = StrokePx / PixelsPerUnit;
        var brush = new SolidColorBrush(_color);

        if (_tool == Tool.Select)
        {
            PlaceRect(SelectionRect, start, current);
            return;
        }

        Shape shape;
        switch (_tool)
        {
            case Tool.Line:
                shape = new Line { X1 = start.X, Y1 = start.Y, X2 = current.X, Y2 = current.Y };
                break;
            case Tool.Arrow:
            {
                var (tip, left, right, shaftEnd) = MarkupGeometry.ArrowHead(ToPx(start), ToPx(current), StrokePx);
                var geo = new GeometryGroup();
                geo.Children.Add(new LineGeometry(start, ToCanvas(shaftEnd)));
                var head = new PathFigure { StartPoint = ToCanvas(tip), IsClosed = true, IsFilled = true };
                head.Segments.Add(new LineSegment(ToCanvas(left), true));
                head.Segments.Add(new LineSegment(ToCanvas(right), true));
                geo.Children.Add(new PathGeometry(new[] { head }));
                shape = new System.Windows.Shapes.Path { Data = geo, Fill = brush };
                break;
            }
            case Tool.Rectangle:
                shape = new Rectangle();
                PlaceRect(shape, start, current);
                break;
            case Tool.Ellipse:
                shape = new Ellipse();
                PlaceRect(shape, start, current);
                break;
            case Tool.Highlight:
                shape = new Rectangle { Fill = new SolidColorBrush(WColor.FromArgb(90, _color.R, _color.G, _color.B)) };
                PlaceRect(shape, start, current);
                stroke = 0;
                break;
            case Tool.Pen:
                shape = new Polyline { Points = new PointCollection(_penPoints) };
                break;
            default:
                return;
        }

        shape.Stroke = _tool == Tool.Highlight ? null : brush;
        shape.StrokeThickness = stroke;
        shape.StrokeStartLineCap = shape.StrokeEndLineCap = PenLineCap.Round;
        shape.StrokeLineJoin = PenLineJoin.Round;
        shape.IsHitTestVisible = false;

        if (_preview is not null) Overlay.Children.Remove(_preview);
        _preview = shape;
        Overlay.Children.Add(shape);
    }

    private static void PlaceRect(FrameworkElement el, Point a, Point b)
    {
        Canvas.SetLeft(el, Math.Min(a.X, b.X));
        Canvas.SetTop(el, Math.Min(a.Y, b.Y));
        el.Width = Math.Abs(a.X - b.X);
        el.Height = Math.Abs(a.Y - b.Y);
    }

    private void CancelDrawing()
    {
        if (_start is not null) Overlay.ReleaseMouseCapture();
        _start = null;
        _penPoints.Clear();
        if (_preview is not null) Overlay.Children.Remove(_preview);
        _preview = null;
    }

    // ---------------- text ----------------

    private void OpenTextEditor(Point p)
    {
        _textAnchor = p;
        _textEditor = new TextBox
        {
            Style = (Style)FindResource("BareTextBox"),
            Foreground = new SolidColorBrush(_color),
            Background = new SolidColorBrush(WColor.FromArgb(40, 0, 0, 0)),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold,
            FontSize = Math.Max(8, FontPx / PixelsPerUnit),
            AcceptsReturn = true,
            MinWidth = 80,
        };
        _textEditor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { CommitText(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelText(); e.Handled = true; }
        };
        _textEditor.LostKeyboardFocus += (_, _) => CommitText();
        Canvas.SetLeft(_textEditor, p.X);
        Canvas.SetTop(_textEditor, p.Y);
        Overlay.Children.Add(_textEditor);
        _textEditor.Focus();
    }

    private void CommitText()
    {
        if (_textEditor is null) return;
        string text = _textEditor.Text.TrimEnd();
        CancelText();
        if (text.Length > 0)
            Commit(new MarkupShape(MarkupKind.Text, new[] { ToPx(_textAnchor) }, DrawingColor, StrokePx, FontPx, text));
    }

    private void CancelText()
    {
        if (_textEditor is null) return;
        var tb = _textEditor;
        _textEditor = null; // set first: removing the box raises LostKeyboardFocus
        Overlay.Children.Remove(tb);
    }

    // ---------------- applying edits ----------------

    private void Commit(MarkupShape shape, bool usedCallout = false)
    {
        if (Item?.Model.ImagePng is not { } png) return;
        try
        {
            Item.SetImage(Annotator.DrawMarkup(png, shape), usedCallout);
            Edited?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Draw markup", ex);
        }
    }

    private void ClearSelection()
    {
        SelectionRect.Visibility = Visibility.Collapsed;
        SelectionRect.Width = SelectionRect.Height = 0;
        BlurButton.IsEnabled = CropButton.IsEnabled = false;
    }

    private DRect? SelectionInPixels()
    {
        if (Item is not { HasImage: true } || SelectionRect.Visibility != Visibility.Visible) return null;
        double k = PixelsPerUnit;
        return new DRect(
            (int)Math.Round(Canvas.GetLeft(SelectionRect) * k), (int)Math.Round(Canvas.GetTop(SelectionRect) * k),
            (int)Math.Round(SelectionRect.Width * k), (int)Math.Round(SelectionRect.Height * k));
    }

    private void OnBlur(object sender, RoutedEventArgs e) => EditSelection(Annotator.Pixelate);
    private void OnCrop(object sender, RoutedEventArgs e) => EditSelection(Annotator.Crop);

    private void EditSelection(Func<byte[], DRect, byte[]> edit)
    {
        if (Item?.Model.ImagePng is not { } png || SelectionInPixels() is not { } region) return;
        Item.SetImage(edit(png, region));
        ClearSelection();
        Edited?.Invoke();
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (Item is not { CanUndoImage: true }) return;
        CancelText();
        Item.UndoImage();
        ClearSelection();
        Edited?.Invoke();
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Item is null) return;
        CancelText();
        Item.SetImage(null);
        Edited?.Invoke();
    }
}
