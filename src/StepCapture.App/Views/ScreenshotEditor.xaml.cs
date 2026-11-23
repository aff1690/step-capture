using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using StepCapture.App.Imaging;
using StepCapture.Core.Model;
using DRect = System.Drawing.Rectangle;
using WColor = System.Windows.Media.Color;

namespace StepCapture.App.Views;

/// <summary>
/// Shows a step's screenshot with editable markup: arrows, lines, boxes, circles, pen, highlighter,
/// text and numbered callouts. Shapes stay editable (select, move, resize, recolor, delete, edit text);
/// the exported image is re-rendered after each change. Also blur/crop and zoom.
/// </summary>
public partial class ScreenshotEditor : UserControl
{
    private enum Tool { Select, Area, Arrow, Line, Rectangle, Ellipse, Pen, Highlight, Text, Number }
    private enum Drag { None, Draw, Area, Move, Resize }

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

    private static readonly double[] ZoomSteps = { 0.1, 0.25, 0.33, 0.5, 0.67, 0.75, 1, 1.25, 1.5, 2, 3, 4 };

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(StepItem), typeof(ScreenshotEditor),
        new PropertyMetadata(null, (d, e) => ((ScreenshotEditor)d).OnItemChanged((StepItem?)e.OldValue)));

    public StepItem? Item
    {
        get => (StepItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    /// <summary>Raised after the screenshot or its markup changed.</summary>
    public event Action? Edited;

    // toolbar state
    private Tool _tool = Tool.Arrow;
    private WColor _color = Palette[0].Color;
    private int _size = 1;
    private bool _syncingToolbar;

    // selection and dragging
    private MarkupItem? _selected;
    private Drag _drag;
    private PointD _dragStart;
    private MarkupItem? _dragOriginal;
    private MarkupItem? _drawing;      // shape being drawn, not yet in the model
    private int _handle = -1;
    private bool _checkpointed;
    private Point _areaStart;
    private readonly List<UIElement> _adorners = new();

    // text editing
    private TextBox? _textEditor;
    private MarkupItem? _editingText;  // existing text shape being edited (hidden while editing)
    private PointD _textAnchor;

    // zoom: null = fit to window, otherwise screen units per image pixel
    private double? _zoom;

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
            swatch.Checked += (_, _) => OnColorPicked(color);
            Swatches.Children.Add(swatch);
        }
        ((RadioButton)Swatches.Children[0]).IsChecked = true;
        MediumSize.IsChecked = true;
        ArrowTool.IsChecked = true;
        Overlay.SizeChanged += (_, _) => RefreshOverlay();
    }

    // ================= toolbar =================

    private void OnToolChecked(object sender, RoutedEventArgs e)
    {
        if (_syncingToolbar || sender is not RadioButton { Tag: string tag } || !Enum.TryParse(tag, out Tool tool)) return;
        CommitText();
        CancelDrag();
        ClearArea();
        _tool = tool;
        if (tool != Tool.Select) Select(null);
        Overlay.Cursor = tool switch { Tool.Text => Cursors.IBeam, Tool.Select => Cursors.Arrow, _ => Cursors.Cross };
        HintText.Text = tool switch
        {
            Tool.Select => "Click a shape to select it, drag to move, drag a handle to resize. Double-click text to edit.",
            Tool.Area => "Drag to select an area, then blur or crop it.",
            Tool.Arrow => "Drag to draw an arrow. Hold Shift for straight angles.",
            Tool.Line => "Drag to draw a line. Hold Shift for straight angles.",
            Tool.Rectangle => "Drag to draw a box. Hold Shift for a square.",
            Tool.Ellipse => "Drag to draw a circle. Hold Shift for a perfect circle.",
            Tool.Pen => "Drag to draw freehand.",
            Tool.Highlight => "Drag over an area to highlight it.",
            Tool.Text => "Click where the text should go, type, then press Enter. Click existing text to edit it.",
            Tool.Number => "Click to place numbered callouts: 1, 2, 3… Each one gets its own titled note under the general notes.",
            _ => "",
        };
    }

    private void OnColorPicked(WColor color)
    {
        _color = color;
        if (_textEditor is not null) _textEditor.Foreground = new SolidColorBrush(color);
        if (_syncingToolbar || _selected is null) return;
        ChangeSelected(m => m.Color = MarkupRenderer.ToArgb(color));
    }

    private void OnSizeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out int size)) return;
        _size = size;
        if (_syncingToolbar || _selected is null) return;
        ChangeSelected(m =>
        {
            m.StrokeWidth = StrokeFor(size);
            m.FontSize = FontFor(size);
        });
    }

    /// <summary>Shows the selected shape's color and size in the toolbar.</summary>
    private void SyncToolbarTo(MarkupItem item)
    {
        _syncingToolbar = true;
        try
        {
            foreach (RadioButton swatch in Swatches.Children)
                swatch.IsChecked = MarkupRenderer.ToArgb((WColor)swatch.Tag) == item.Color;
            _color = MarkupRenderer.ToColor(item.Color);

            bool usesFont = item.Kind is MarkupKind.Text or MarkupKind.Number;
            double value = usesFont ? item.FontSize : item.StrokeWidth;
            int best = 0;
            for (int i = 1; i < 3; i++)
                if (Math.Abs((usesFont ? FontFor(i) : StrokeFor(i)) - value) < Math.Abs((usesFont ? FontFor(best) : StrokeFor(best)) - value))
                    best = i;
            _size = best;
            foreach (var child in ToolStrip.Children)
                if (child is RadioButton { GroupName: "Size" } rb) rb.IsChecked = (string)rb.Tag == best.ToString();
        }
        finally
        {
            _syncingToolbar = false;
        }
    }

    private void SetTool(Tool tool)
    {
        foreach (var child in ToolStrip.Children)
            if (child is RadioButton { GroupName: "Tool" } rb && (string)rb.Tag == tool.ToString()) rb.IsChecked = true;
    }

    /// <summary>Keyboard shortcuts, called by the window when no text box has focus.</summary>
    public bool HandleShortcut(KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Z: OnUndo(this, new RoutedEventArgs()); return true;
                case Key.OemPlus or Key.Add: ZoomBy(+1); return true;
                case Key.OemMinus or Key.Subtract: ZoomBy(-1); return true;
                case Key.D0 or Key.NumPad0: SetZoom(null); return true;
                case Key.D1 or Key.NumPad1: SetZoom(1); return true;
            }
            return false;
        }
        if (Item is not { HasImage: true }) return false;

        if (_selected is not null)
        {
            double step = (mods == ModifierKeys.Shift ? 10 : 1) * PixelsPerUnit;
            switch (e.Key)
            {
                case Key.Delete or Key.Back: DeleteSelected(); return true;
                case Key.Left: ChangeSelected(m => m.Move(-step, 0)); return true;
                case Key.Right: ChangeSelected(m => m.Move(step, 0)); return true;
                case Key.Up: ChangeSelected(m => m.Move(0, -step)); return true;
                case Key.Down: ChangeSelected(m => m.Move(0, step)); return true;
            }
        }

        if (mods != ModifierKeys.None) return false;
        Tool? tool = e.Key switch
        {
            Key.V => Tool.Select, Key.B => Tool.Area, Key.A => Tool.Arrow, Key.L => Tool.Line, Key.R => Tool.Rectangle,
            Key.E => Tool.Ellipse, Key.P => Tool.Pen, Key.H => Tool.Highlight, Key.T => Tool.Text, Key.N => Tool.Number,
            _ => null,
        };
        if (tool is { } t) { SetTool(t); return true; }
        if (e.Key == Key.Escape)
        {
            CancelDrag();
            ClearArea();
            Select(null);
            return true;
        }
        return false;
    }

    // ================= item =================

    private void OnItemChanged(StepItem? old)
    {
        if (old is not null) old.PropertyChanged -= OnItemPropertyChanged;
        CancelText();
        CancelDrag();
        ClearArea();
        _selected = null;
        Root.DataContext = Item;
        if (Item is not null) Item.PropertyChanged += OnItemPropertyChanged;
        ApplyZoom();
        RefreshOverlay();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StepItem.Markup) or nameof(StepItem.BaseImage))
        {
            if (_selected is not null && Item?.Markup.All(m => m.Id != _selected.Id) == true) Select(null);
            else if (_selected is not null) _selected = Item?.Markup.FirstOrDefault(m => m.Id == _selected.Id);
            ApplyZoom();
            RefreshOverlay();
        }
    }

    private (int W, int H) PixelSize => Item?.PixelSize ?? (0, 0);
    private double PixelsPerUnit => Overlay.ActualWidth > 0 ? PixelSize.W / Overlay.ActualWidth : 1;
    private double ImageScale => MarkupGeometry.ScaleFor(PixelSize.W, PixelSize.H);
    private double StrokeFor(int size) => MarkupGeometry.StrokeWidths[size] * ImageScale;
    private double FontFor(int size) => MarkupGeometry.FontSizes[size] * ImageScale;

    private PointD ToPx(Point p) => new(p.X * PixelsPerUnit, p.Y * PixelsPerUnit);
    private Point ToCanvas(PointD p) => new(p.X / PixelsPerUnit, p.Y / PixelsPerUnit);
    private Rect ToCanvas(Rect r) => new(r.X / PixelsPerUnit, r.Y / PixelsPerUnit, r.Width / PixelsPerUnit, r.Height / PixelsPerUnit);

    private Point Clamp(Point p) =>
        new(Math.Clamp(p.X, 0, Overlay.ActualWidth), Math.Clamp(p.Y, 0, Overlay.ActualHeight));

    private MarkupItem NewShape(MarkupKind kind, params PointD[] points) => new()
    {
        Kind = kind,
        Points = points.ToList(),
        Color = MarkupRenderer.ToArgb(_color),
        StrokeWidth = StrokeFor(_size),
        FontSize = FontFor(_size),
    };

    // ================= drawing the overlay =================

    /// <summary>Redraws the markup layer and the selection handles.</summary>
    private void RefreshOverlay()
    {
        foreach (var a in _adorners) Overlay.Children.Remove(a);
        _adorners.Clear();

        var (w, h) = PixelSize;
        if (Item is null || w == 0)
        {
            MarkupLayer.Source = null;
            return;
        }

        var items = Item.Markup.Where(m => m != _editingText).ToList();
        if (_drawing is not null) items.Add(_drawing);
        MarkupLayer.Source = MarkupRenderer.BuildLayer(items, w, h);

        if (_selected is null || Overlay.ActualWidth <= 0) return;
        var accent = (Brush)FindResource("AccentBrush");
        var box = ToCanvas(MarkupRenderer.Bounds(_selected));
        box.Inflate(4, 4);
        var outline = new Rectangle
        {
            Width = box.Width, Height = box.Height, Stroke = accent, StrokeThickness = 1.2,
            StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false,
        };
        Canvas.SetLeft(outline, box.X);
        Canvas.SetTop(outline, box.Y);
        AddAdorner(outline);

        foreach (var hp in Handles(_selected))
        {
            var c = ToCanvas(hp);
            var handle = new Rectangle
            {
                Width = 10, Height = 10, Fill = Brushes.White, Stroke = accent, StrokeThickness = 2,
                RadiusX = 2, RadiusY = 2, IsHitTestVisible = false,
            };
            Canvas.SetLeft(handle, c.X - 5);
            Canvas.SetTop(handle, c.Y - 5);
            AddAdorner(handle);
        }
    }

    private void AddAdorner(UIElement e)
    {
        _adorners.Add(e);
        Overlay.Children.Add(e);
    }

    /// <summary>Resize handles, in image pixels: line ends, or box corners.</summary>
    private static List<PointD> Handles(MarkupItem m)
    {
        if (m.Points.Count < 2) return new();
        switch (m.Kind)
        {
            case MarkupKind.Arrow or MarkupKind.Line:
                return new() { m.Points[0], m.Points[^1] };
            case MarkupKind.Rectangle or MarkupKind.Ellipse or MarkupKind.Highlight:
            {
                var (a, b) = (m.Points[0], m.Points[^1]);
                double l = Math.Min(a.X, b.X), t = Math.Min(a.Y, b.Y), r = Math.Max(a.X, b.X), btm = Math.Max(a.Y, b.Y);
                return new() { new(l, t), new(r, t), new(r, btm), new(l, btm) };
            }
            default:
                return new();
        }
    }

    // ================= hit testing =================

    private MarkupItem? HitTest(PointD p)
    {
        if (Item is null) return null;
        double tol = 6 * PixelsPerUnit;
        for (int i = Item.Markup.Count - 1; i >= 0; i--)
        {
            var m = Item.Markup[i];
            bool hit = m.Kind switch
            {
                MarkupKind.Arrow or MarkupKind.Line when m.Points.Count >= 2 =>
                    MarkupGeometry.DistanceToSegment(p, m.Points[0], m.Points[^1]) <= m.StrokeWidth / 2 + tol,
                MarkupKind.Pen => PenDistance(m, p) <= m.StrokeWidth / 2 + tol,
                _ => Inflated(MarkupRenderer.Bounds(m), tol).Contains(new Point(p.X, p.Y)),
            };
            if (hit) return m;
        }
        return null;
    }

    private static double PenDistance(MarkupItem m, PointD p)
    {
        if (m.Points.Count == 1) return Math.Sqrt(Math.Pow(m.Points[0].X - p.X, 2) + Math.Pow(m.Points[0].Y - p.Y, 2));
        double best = double.MaxValue;
        for (int i = 1; i < m.Points.Count; i++)
            best = Math.Min(best, MarkupGeometry.DistanceToSegment(p, m.Points[i - 1], m.Points[i]));
        return best;
    }

    private static Rect Inflated(Rect r, double by)
    {
        r.Inflate(by, by);
        return r;
    }

    private int HitHandle(PointD p)
    {
        if (_selected is null) return -1;
        var handles = Handles(_selected);
        double tol = 8 * PixelsPerUnit;
        for (int i = 0; i < handles.Count; i++)
            if (Math.Abs(handles[i].X - p.X) <= tol && Math.Abs(handles[i].Y - p.Y) <= tol) return i;
        return -1;
    }

    private void Select(MarkupItem? item)
    {
        _selected = item;
        DeleteShapeButton.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        if (item is not null) SyncToolbarTo(item);
        RefreshOverlay();
    }

    // ================= mouse =================

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Item is not { HasImage: true }) return;
        var canvasPoint = Clamp(e.GetPosition(Overlay));
        var p = ToPx(canvasPoint);
        Focus();

        if (_textEditor is not null)
        {
            CommitText();
            e.Handled = true;
            return;
        }

        switch (_tool)
        {
            case Tool.Select:
            {
                int handle = HitHandle(p);
                if (handle >= 0)
                {
                    BeginDrag(Drag.Resize, p);
                    _handle = handle;
                    break;
                }
                var hit = HitTest(p);
                Select(hit);
                if (hit is null) return;
                if (e.ClickCount == 2 && hit.Kind == MarkupKind.Text)
                {
                    OpenTextEditor(hit.Points[0], hit);
                    return;
                }
                BeginDrag(Drag.Move, p);
                break;
            }

            case Tool.Area:
                ClearArea();
                _areaStart = canvasPoint;
                SelectionRect.Visibility = Visibility.Visible;
                PlaceRect(SelectionRect, canvasPoint, canvasPoint);
                BeginDrag(Drag.Area, p);
                break;

            case Tool.Text:
            {
                var hit = HitTest(p);
                if (hit?.Kind == MarkupKind.Text) OpenTextEditor(hit.Points[0], hit);
                else OpenTextEditor(p, null);
                e.Handled = true;
                return;
            }

            case Tool.Number:
            {
                var callout = NewShape(MarkupKind.Number, p);
                AddShape(callout); // its titled note appears under the general notes (StepItem.RefreshCalloutNotes)
                return;
            }

            default:
            {
                var kind = _tool switch
                {
                    Tool.Arrow => MarkupKind.Arrow, Tool.Line => MarkupKind.Line, Tool.Rectangle => MarkupKind.Rectangle,
                    Tool.Ellipse => MarkupKind.Ellipse, Tool.Highlight => MarkupKind.Highlight, _ => MarkupKind.Pen,
                };
                Select(null);
                _drawing = kind == MarkupKind.Pen ? NewShape(kind, p) : NewShape(kind, p, p);
                BeginDrag(Drag.Draw, p);
                break;
            }
        }
        Overlay.CaptureMouse();
    }

    private void BeginDrag(Drag mode, PointD start)
    {
        _drag = mode;
        _dragStart = start;
        _dragOriginal = _selected?.Clone();
        _checkpointed = false;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var canvasPoint = Clamp(e.GetPosition(Overlay));
        var p = ToPx(canvasPoint);

        if (_drag == Drag.None)
        {
            if (_tool == Tool.Select && Item is { HasImage: true })
                Overlay.Cursor = HitHandle(p) >= 0 ? Cursors.SizeAll : HitTest(p) is not null ? Cursors.Hand : Cursors.Arrow;
            return;
        }

        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (_drag)
        {
            case Drag.Area:
                PlaceRect(SelectionRect, _areaStart, canvasPoint);
                return;

            case Drag.Draw when _drawing is not null:
                if (_drawing.Kind == MarkupKind.Pen) _drawing.Points.Add(p);
                else _drawing.Points[1] = shift ? MarkupGeometry.Constrain(_drawing.Kind, _drawing.Points[0], p) : p;
                RefreshOverlay();
                return;

            case Drag.Move when _selected is not null && _dragOriginal is not null:
            {
                double dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
                if (!_checkpointed && Math.Abs(dx) + Math.Abs(dy) < 2 * PixelsPerUnit) return;
                EnsureCheckpoint();
                for (int i = 0; i < _selected.Points.Count; i++) _selected.Points[i] = _dragOriginal.Points[i].Offset(dx, dy);
                RefreshOverlay();
                return;
            }

            case Drag.Resize when _selected is not null && _dragOriginal is not null:
            {
                EnsureCheckpoint();
                var orig = _dragOriginal.Points;
                if (_selected.Kind is MarkupKind.Arrow or MarkupKind.Line)
                {
                    int moving = _handle == 0 ? 0 : orig.Count - 1;
                    var anchor = _handle == 0 ? orig[^1] : orig[0];
                    _selected.Points[moving] = shift ? MarkupGeometry.Constrain(_selected.Kind, anchor, p) : p;
                }
                else
                {
                    var corners = Handles(_dragOriginal);
                    var opposite = corners[(_handle + 2) % 4];
                    var corner = shift ? MarkupGeometry.Constrain(_selected.Kind, opposite, p) : p;
                    _selected.Points = new List<PointD> { opposite, corner };
                }
                RefreshOverlay();
                return;
            }
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag == Drag.None) return;
        var mode = _drag;
        _drag = Drag.None;
        Overlay.ReleaseMouseCapture();

        switch (mode)
        {
            case Drag.Area:
            {
                bool has = SelectionRect.Width > 4 && SelectionRect.Height > 4;
                if (!has) SelectionRect.Visibility = Visibility.Collapsed;
                BlurButton.IsEnabled = CropButton.IsEnabled = has;
                break;
            }

            case Drag.Draw when _drawing is not null:
            {
                var shape = _drawing;
                _drawing = null;
                bool tooSmall = shape.Kind != MarkupKind.Pen
                    && Math.Abs(shape.Points[1].X - shape.Points[0].X) + Math.Abs(shape.Points[1].Y - shape.Points[0].Y) < 4 * PixelsPerUnit;
                if (tooSmall) RefreshOverlay();
                else AddShape(shape);
                break;
            }

            case Drag.Move or Drag.Resize:
                if (_checkpointed) FinishEdit();
                break;
        }
        _dragOriginal = null;
        _handle = -1;
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (Item is not { HasImage: true }) return;
        CommitText();
        var hit = HitTest(ToPx(e.GetPosition(Overlay)));
        if (hit is null) return;
        SetTool(Tool.Select);
        Select(hit);

        var menu = new ContextMenu { PlacementTarget = Overlay };
        if (hit.Kind == MarkupKind.Text) menu.Items.Add(MenuItem("Edit text", () => OpenTextEditor(hit.Points[0], hit)));
        menu.Items.Add(MenuItem("Bring to front", () => Reorder(hit, toFront: true)));
        menu.Items.Add(MenuItem("Send to back", () => Reorder(hit, toFront: false)));
        menu.Items.Add(MenuItem("Delete", DeleteSelected));
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void CancelDrag()
    {
        if (_drag != Drag.None) Overlay.ReleaseMouseCapture();
        // Put a half-moved shape back where it was.
        if (_drag is Drag.Move or Drag.Resize && _selected is not null && _dragOriginal is not null && _checkpointed)
            _selected.Points = new List<PointD>(_dragOriginal.Points);
        _drag = Drag.None;
        _drawing = null;
        _dragOriginal = null;
        RefreshOverlay();
    }

    private static void PlaceRect(FrameworkElement el, Point a, Point b)
    {
        Canvas.SetLeft(el, Math.Min(a.X, b.X));
        Canvas.SetTop(el, Math.Min(a.Y, b.Y));
        el.Width = Math.Abs(a.X - b.X);
        el.Height = Math.Abs(a.Y - b.Y);
    }

    // ================= editing the model =================

    private void EnsureCheckpoint()
    {
        if (_checkpointed || Item is null) return;
        Item.Checkpoint();
        _checkpointed = true;
    }

    /// <summary>Re-renders the exported image and tells the window something changed.</summary>
    private void FinishEdit()
    {
        if (Item is null) return;
        try
        {
            MarkupGeometry.RenumberCallouts(Item.Markup);
            Item.RenderMarkup();
            Edited?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Render markup", ex);
        }
        RefreshOverlay();
    }

    private void AddShape(MarkupItem shape)
    {
        if (Item is null) return;
        Item.Checkpoint();
        Item.Markup.Add(shape);
        FinishEdit();
    }

    private void ChangeSelected(Action<MarkupItem> change)
    {
        if (Item is null || _selected is null) return;
        Item.Checkpoint();
        change(_selected);
        FinishEdit();
    }

    private void DeleteSelected()
    {
        if (Item is null || _selected is null) return;
        Item.Checkpoint();
        Item.Markup.Remove(_selected);
        Select(null);
        FinishEdit();
    }

    private void Reorder(MarkupItem item, bool toFront)
    {
        if (Item is null) return;
        Item.Checkpoint();
        Item.Markup.Remove(item);
        if (toFront) Item.Markup.Add(item);
        else Item.Markup.Insert(0, item);
        FinishEdit();
    }

    private void OnDeleteShape(object sender, RoutedEventArgs e) => DeleteSelected();

    // ================= text =================

    private void OpenTextEditor(PointD anchor, MarkupItem? existing)
    {
        CommitText();
        _textAnchor = anchor;
        _editingText = existing;
        if (existing is not null)
        {
            Select(existing);
            _color = MarkupRenderer.ToColor(existing.Color);
        }
        double fontPx = existing?.FontSize ?? FontFor(_size);
        _textEditor = new TextBox
        {
            Style = (Style)FindResource("BareTextBox"),
            Text = existing?.Text ?? "",
            Foreground = new SolidColorBrush(_color),
            Background = new SolidColorBrush(WColor.FromArgb(40, 0, 0, 0)),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold,
            FontSize = Math.Max(8, fontPx / PixelsPerUnit),
            AcceptsReturn = true,
            MinWidth = 80,
        };
        _textEditor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { CommitText(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelText(); e.Handled = true; }
        };
        _textEditor.LostKeyboardFocus += (_, _) => CommitText();
        var c = ToCanvas(anchor);
        Canvas.SetLeft(_textEditor, c.X);
        Canvas.SetTop(_textEditor, c.Y);
        Overlay.Children.Add(_textEditor);
        RefreshOverlay();
        _textEditor.Focus();
        _textEditor.SelectAll();
    }

    private void CommitText()
    {
        if (_textEditor is null || Item is null) return;
        string text = _textEditor.Text.TrimEnd();
        var existing = _editingText;
        CancelText();

        if (existing is not null)
        {
            if (text == existing.Text) return;
            Item.Checkpoint();
            if (text.Length == 0)
            {
                Item.Markup.Remove(existing);
                Select(null);
            }
            else
            {
                existing.Text = text;
            }
            FinishEdit();
        }
        else if (text.Length > 0)
        {
            var shape = NewShape(MarkupKind.Text, _textAnchor);
            shape.Text = text;
            AddShape(shape);
        }
    }

    private void CancelText()
    {
        if (_textEditor is null) return;
        var tb = _textEditor;
        _textEditor = null; // set first: removing the box raises LostKeyboardFocus
        _editingText = null;
        Overlay.Children.Remove(tb);
        RefreshOverlay();
    }

    // ================= blur, crop, undo, remove =================

    private void ClearArea()
    {
        SelectionRect.Visibility = Visibility.Collapsed;
        SelectionRect.Width = SelectionRect.Height = 0;
        BlurButton.IsEnabled = CropButton.IsEnabled = false;
    }

    private DRect? AreaInPixels()
    {
        if (Item is not { HasImage: true } || SelectionRect.Visibility != Visibility.Visible) return null;
        double k = PixelsPerUnit;
        var r = new DRect(
            (int)Math.Round(Canvas.GetLeft(SelectionRect) * k), (int)Math.Round(Canvas.GetTop(SelectionRect) * k),
            (int)Math.Round(SelectionRect.Width * k), (int)Math.Round(SelectionRect.Height * k));
        r.Intersect(new DRect(0, 0, PixelSize.W, PixelSize.H));
        return r.Width >= 8 && r.Height >= 8 ? r : null;
    }

    private void OnBlur(object sender, RoutedEventArgs e)
    {
        if (Item?.BasePng is not { } png || AreaInPixels() is not { } region) return;
        Item.Checkpoint();
        Item.ReplaceBase(Annotator.Pixelate(png, region));
        ClearArea();
        Edited?.Invoke();
    }

    private void OnCrop(object sender, RoutedEventArgs e)
    {
        if (Item?.BasePng is not { } png || AreaInPixels() is not { } region) return;
        Item.Checkpoint();
        MarkupGeometry.ShiftAll(Item.Markup, -region.X, -region.Y); // keep shapes over the same spot
        Item.ReplaceBase(Annotator.Crop(png, region));
        ClearArea();
        Select(null);
        Edited?.Invoke();
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (Item is not { CanUndoImage: true }) return;
        CancelText();
        CancelDrag();
        Item.UndoImage();
        ClearArea();
        Select(null);
        Edited?.Invoke();
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Item is null) return;
        CancelText();
        Item.Checkpoint();
        Item.ReplaceBase(null);
        Select(null);
        Edited?.Invoke();
    }

    // ================= zoom =================

    private double FitScale()
    {
        var (w, h) = PixelSize;
        if (w == 0 || h == 0) return 1;
        double vw = Math.Max(50, Scroller.ViewportWidth - 12), vh = Math.Max(50, Scroller.ViewportHeight - 12);
        if (double.IsNaN(vw) || vw <= 0) vw = Math.Max(50, Scroller.ActualWidth - 12);
        if (double.IsNaN(vh) || vh <= 0) vh = Math.Max(50, Scroller.ActualHeight - 12);
        // Screenshots are in physical pixels; at "100%" one image pixel is one screen pixel.
        double dipPerPixel = 1 / DpiScale;
        return Math.Min(dipPerPixel, Math.Min(vw / w, vh / h));
    }

    private double DpiScale => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;

    /// <summary>Current zoom as screen units per image pixel.</summary>
    private double CurrentScale => _zoom is { } z ? z / DpiScale : FitScale();

    private void ApplyZoom()
    {
        var (w, h) = PixelSize;
        if (w == 0)
        {
            ImageHost.Width = ImageHost.Height = double.NaN;
            return;
        }
        double s = CurrentScale;
        ImageHost.Width = w * s;
        ImageHost.Height = h * s;
        ZoomLabel.Content = _zoom is null ? $"Fit ({Math.Round(s * DpiScale * 100)}%)" : $"{Math.Round(_zoom.Value * 100)}%";
    }

    /// <summary>null fits the window; otherwise 1 = actual pixels.</summary>
    private void SetZoom(double? zoom, Point? keepUnderMouse = null)
    {
        CommitText();
        double oldScale = CurrentScale;
        Point anchorInHost = keepUnderMouse is { } m ? Mouse.GetPosition(ImageHost) : new Point(ImageHost.ActualWidth / 2, ImageHost.ActualHeight / 2);
        Point anchorInView = keepUnderMouse ?? new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2);

        _zoom = zoom is { } z ? Math.Clamp(z, ZoomSteps[0], ZoomSteps[^1]) : null;
        ApplyZoom();
        Scroller.UpdateLayout();

        // Keep the point under the mouse (or the center) in place.
        double ratio = CurrentScale / oldScale;
        var hostOrigin = ImageHost.TranslatePoint(new Point(0, 0), Scroller);
        double targetX = anchorInHost.X * ratio, targetY = anchorInHost.Y * ratio;
        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + hostOrigin.X + targetX - anchorInView.X);
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + hostOrigin.Y + targetY - anchorInView.Y);
    }

    private void ZoomBy(int direction, Point? mouse = null)
    {
        double current = _zoom ?? CurrentScale * DpiScale;
        double next = direction > 0
            ? ZoomSteps.FirstOrDefault(z => z > current + 0.001, ZoomSteps[^1])
            : ZoomSteps.LastOrDefault(z => z < current - 0.001, ZoomSteps[0]);
        SetZoom(next, mouse);
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => ZoomBy(+1);
    private void OnZoomOut(object sender, RoutedEventArgs e) => ZoomBy(-1);
    private void OnZoomFit(object sender, RoutedEventArgs e) => SetZoom(null);
    private void OnZoomActual(object sender, RoutedEventArgs e) => SetZoom(1);

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        ZoomBy(e.Delta > 0 ? +1 : -1, e.GetPosition(Scroller));
        e.Handled = true;
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_zoom is null) ApplyZoom();
    }
}
