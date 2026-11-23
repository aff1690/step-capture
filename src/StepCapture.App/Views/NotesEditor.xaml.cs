using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using StepCapture.Core.Model;

namespace StepCapture.App.Views;

/// <summary>
/// Notes box with bold, italic, underline and indent levels. The text is kept as the small markup described
/// in <see cref="RichText"/> (tabs for indents, **bold**, *italic*, __underline__) and bound through <see cref="Text"/>.
/// </summary>
public partial class NotesEditor : UserControl
{
    private const double IndentStep = 24;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(NotesEditor),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextPropertyChanged)
        {
            DefaultUpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged,
        });

    private bool _loading;   // rebuilding the document from Text
    private bool _syncing;   // writing Text from the document

    /// <summary>Raised when the user changes the notes (not when a new step is shown).</summary>
    public event Action? UserEdited;

    public NotesEditor()
    {
        InitializeComponent();
        DataObject.AddPastingHandler(Box, OnPaste);
        Load("");
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (NotesEditor)d;
        if (!editor._syncing) editor.Load(e.NewValue as string ?? "");
    }

    /// <summary>Puts the caret at the end of the notes and focuses them (after a callout added a line).</summary>
    public void FocusEnd()
    {
        Box.Focus();
        Box.CaretPosition = Box.Document.ContentEnd;
        Box.ScrollToEnd();
    }

    // ---------------- markup <-> document ----------------

    private void Load(string text)
    {
        _loading = true;
        try
        {
            var doc = new FlowDocument { PagePadding = new Thickness(0) };
            // Paragraphs (including pasted ones) sit tight like lines in a text box.
            var tight = new Style(typeof(Paragraph));
            tight.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
            doc.Resources.Add(typeof(Paragraph), tight);
            var lines = RichText.Parse(text, trimBlankEdges: false);
            // Open lists, innermost last, with the indent level each one sits at.
            var stack = new List<(List List, int Level)>();
            foreach (var line in lines)
            {
                var p = new Paragraph();
                foreach (var span in line.Spans)
                {
                    var run = new Run(span.Text);
                    if (span.Bold) run.FontWeight = FontWeights.Bold;
                    if (span.Italic) run.FontStyle = FontStyles.Italic;
                    if (span.Underline) run.TextDecorations = TextDecorations.Underline;
                    p.Inlines.Add(run);
                }
                if (line.List == ListKind.None)
                {
                    stack.Clear();
                    p.Margin = new Thickness(line.Indent * IndentStep, 0, 0, 0);
                    doc.Blocks.Add(p);
                    continue;
                }

                int level = line.Indent;
                var marker = MarkerFor(line.List, level);
                while (stack.Count > 0 && stack[^1].Level > level) stack.RemoveAt(stack.Count - 1);
                if (stack.Count > 0 && stack[^1].Level == level && IsNumbered(stack[^1].List.MarkerStyle) != (line.List == ListKind.Numbered))
                    stack.RemoveAt(stack.Count - 1); // bullets after numbers (or the reverse) start a new list
                if (stack.Count == 0 || stack[^1].Level < level)
                {
                    var list = NewList(marker);
                    if (stack.Count == 0)
                    {
                        list.Margin = new Thickness(level * IndentStep, 0, 0, 0);
                        doc.Blocks.Add(list);
                    }
                    else
                    {
                        var parent = stack[^1].List;
                        if (parent.ListItems.Count == 0) parent.ListItems.Add(new ListItem());
                        parent.ListItems.LastListItem.Blocks.Add(list);
                    }
                    stack.Add((list, level));
                }
                stack[^1].List.ListItems.Add(new ListItem(p));
            }
            FixLists(doc);
            if (doc.Blocks.Count == 0) doc.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
            Box.Document = doc;
        }
        finally
        {
            _loading = false;
        }
    }

    private string Serialize()
    {
        var lines = new List<RichLine>();
        foreach (var block in Box.Document.Blocks) AddBlock(lines, block, 0);
        // A document always holds at least one (maybe empty) paragraph; an empty one means no notes.
        while (lines.Count > 0 && lines[^1].IsEmpty) lines.RemoveAt(lines.Count - 1);
        return RichText.Serialize(lines);
    }

    private static void AddBlock(List<RichLine> lines, Block block, int extraIndent)
    {
        switch (block)
        {
            case Paragraph p:
            {
                int indent = extraIndent + Level(p);
                var line = new RichLine { Indent = Math.Min(indent, RichText.MaxIndent) };
                lines.Add(line);
                foreach (var inline in p.Inlines) AddInline(lines, inline, ref line);
                break;
            }
            case List list:
            {
                // A top-level list keeps its indent in its margin; a nested list is one level deeper than its parent.
                int level = Math.Min(extraIndent + (list.Parent is ListItem ? 0 : LevelOf(list.Margin)), RichText.MaxIndent);
                var kind = IsNumbered(list.MarkerStyle) ? ListKind.Numbered : ListKind.Bullet;
                foreach (var item in list.ListItems)
                {
                    bool first = true;
                    foreach (var b in item.Blocks)
                    {
                        if (b is List nested) { AddBlock(lines, nested, level + 1); continue; }
                        int before = lines.Count;
                        AddBlock(lines, b, level + (first ? 0 : 1));
                        if (first && lines.Count > before && b is Paragraph) lines[before].List = kind;
                        first = false;
                    }
                    if (first) lines.Add(new RichLine { Indent = level, List = kind }); // empty item
                }
                break;
            }
            case Section section:
                foreach (var b in section.Blocks) AddBlock(lines, b, extraIndent);
                break;
            default:
            {
                string text = new TextRange(block.ContentStart, block.ContentEnd).Text.TrimEnd('\r', '\n');
                foreach (var t in text.Replace("\r\n", "\n").Split('\n'))
                    lines.Add(new RichLine { Indent = extraIndent, Spans = { new RichSpan(t) } });
                break;
            }
        }
    }

    private static void AddInline(List<RichLine> lines, Inline inline, ref RichLine line)
    {
        switch (inline)
        {
            case Run run when run.Text.Length > 0:
                line.Spans.Add(new RichSpan(run.Text.Replace('\t', ' '),
                    Bold: run.FontWeight.ToOpenTypeWeight() >= 600,
                    Italic: run.FontStyle != FontStyles.Normal,
                    Underline: IsUnderlined(run)));
                break;
            case LineBreak:
                line = new RichLine { Indent = line.Indent };
                lines.Add(line);
                break;
            case Span span:
                foreach (var child in span.Inlines) AddInline(lines, child, ref line);
                break;
        }
    }

    /// <summary>Underline is set on the run or on an enclosing Underline/Span (TextDecorations isn't inherited).</summary>
    private static int Level(Paragraph p) => p.Parent is ListItem ? 0 : LevelOf(p.Margin);

    private static int LevelOf(Thickness margin) =>
        double.IsNaN(margin.Left) ? 0 : Math.Clamp((int)Math.Round(margin.Left / IndentStep), 0, RichText.MaxIndent);

    // ---------------- lists ----------------

    private static bool IsNumbered(TextMarkerStyle m) =>
        m is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.LowerRoman
            or TextMarkerStyle.UpperLatin or TextMarkerStyle.UpperRoman;

    /// <summary>Same markers as the exports: 1. a. i. for numbers and • ◦ ▪ for bullets, by level.</summary>
    private static TextMarkerStyle MarkerFor(ListKind kind, int level) => kind == ListKind.Numbered
        ? (level % 3) switch { 1 => TextMarkerStyle.LowerLatin, 2 => TextMarkerStyle.LowerRoman, _ => TextMarkerStyle.Decimal }
        : (level % 3) switch { 1 => TextMarkerStyle.Circle, 2 => TextMarkerStyle.Square, _ => TextMarkerStyle.Disc };

    private static List NewList(TextMarkerStyle marker) =>
        new() { MarkerStyle = marker, Margin = new Thickness(0), Padding = new Thickness(IndentStep, 0, 0, 0) };

    /// <summary>
    /// Sets each list's marker style for its level and its start number, so numbering continues across
    /// blank lines and deeper items exactly as in the exports (see RichText.ListLabels).
    /// </summary>
    private static void FixLists(FlowDocument doc)
    {
        var counters = new int[RichText.MaxIndent + 2];
        void Reset(int from) { for (int k = Math.Max(0, from); k < counters.Length; k++) counters[k] = 0; }

        void Walk(IEnumerable<Block> blocks, int extra)
        {
            foreach (var b in blocks)
            {
                switch (b)
                {
                    case Paragraph p:
                        bool empty = new TextRange(p.ContentStart, p.ContentEnd).Text.Trim().Length == 0;
                        if (!empty) Reset(extra + Level(p));
                        break;
                    case Section sec:
                        Walk(sec.Blocks, extra);
                        break;
                    case List list:
                    {
                        int level = Math.Min(extra + (list.Parent is ListItem ? 0 : LevelOf(list.Margin)), RichText.MaxIndent);
                        bool numbered = IsNumbered(list.MarkerStyle);
                        var marker = MarkerFor(numbered ? ListKind.Numbered : ListKind.Bullet, level);
                        if (list.MarkerStyle != marker) list.MarkerStyle = marker;
                        if (!numbered) counters[level] = 0;
                        int start = counters[level] + 1;
                        if (numbered && list.StartIndex != start) list.StartIndex = start;
                        if (list.Padding.Left != IndentStep) list.Padding = new Thickness(IndentStep, 0, 0, 0);
                        foreach (var item in list.ListItems)
                        {
                            Reset(level + 1);
                            if (numbered) counters[level]++;
                            foreach (var ib in item.Blocks)
                                if (ib is List nested) Walk(new[] { nested }, level + 1);
                        }
                        break;
                    }
                }
            }
        }
        Walk(doc.Blocks, 0);
    }

    private static bool IsUnderlined(Inline inline)
    {
        for (DependencyObject? o = inline; o is Inline i; o = i.Parent)
            if (i.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true) return true;
        return false;
    }

    private bool _fixing;

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _fixing) return;
        _fixing = true;
        try { FixLists(Box.Document); }
        finally { _fixing = false; }
        Sync();
    }

    private void Sync()
    {
        string text = Serialize();
        if (text == Text) return;
        _syncing = true;
        try { Text = text; }
        finally { _syncing = false; }
        UserEdited?.Invoke();
    }

    // ---------------- formatting ----------------

    private void OnBold(object sender, RoutedEventArgs e) => EditingCommands.ToggleBold.Execute(null, Box);
    private void OnItalic(object sender, RoutedEventArgs e) => EditingCommands.ToggleItalic.Execute(null, Box);
    private void OnUnderline(object sender, RoutedEventArgs e) => EditingCommands.ToggleUnderline.Execute(null, Box);
    private void OnIndent(object sender, RoutedEventArgs e) => ChangeIndent(+1);
    private void OnOutdent(object sender, RoutedEventArgs e) => ChangeIndent(-1);
    private void OnBullets(object sender, RoutedEventArgs e) => EditingCommands.ToggleBullets.Execute(null, Box);
    private void OnNumbering(object sender, RoutedEventArgs e) => EditingCommands.ToggleNumbering.Execute(null, Box);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || (Keyboard.Modifiers & ~ModifierKeys.Shift) != 0) return;
        ChangeIndent(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : +1);
        e.Handled = true;
    }

    /// <summary>Indents or outdents every paragraph touched by the selection.</summary>
    private void ChangeIndent(int delta)
    {
        // Inside a list, Tab nests the item one level deeper (1. -> a. -> i.) and Shift+Tab brings it back.
        if (Box.Selection.Start.Paragraph?.Parent is ListItem)
        {
            (delta > 0 ? EditingCommands.IncreaseIndentation : EditingCommands.DecreaseIndentation).Execute(null, Box);
            return;
        }
        var start = Box.Selection.Start.Paragraph;
        var end = Box.Selection.End.Paragraph;
        bool inRange = false;
        foreach (var p in AllParagraphs(Box.Document.Blocks))
        {
            if (p == start) inRange = true;
            if (inRange || (start is null && p == end))
            {
                int level = Level(p) + delta;
                p.Margin = new Thickness(Math.Clamp(level, 0, RichText.MaxIndent) * IndentStep, 0, 0, 0);
            }
            if (p == end) break;
        }
        Sync();
    }

    private static IEnumerable<Paragraph> AllParagraphs(BlockCollection blocks)
    {
        foreach (var b in blocks)
        {
            if (b is Paragraph p) yield return p;
            else if (b is Section s) foreach (var x in AllParagraphs(s.Blocks)) yield return x;
            else if (b is List l) foreach (var item in l.ListItems) foreach (var x in AllParagraphs(item.Blocks)) yield return x;
        }
    }

    /// <summary>Pastes as plain text so styles from Word or web pages don't come along.</summary>
    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is string text)
        {
            var plain = new DataObject();
            plain.SetData(DataFormats.UnicodeText, text);
            e.DataObject = plain;
        }
        else e.CancelCommand();
    }
}
