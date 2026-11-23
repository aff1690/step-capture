using System.Text;
using System.Text.RegularExpressions;

namespace StepCapture.Core.Model;

/// <summary>A run of note text with one formatting.</summary>
public sealed record RichSpan(string Text, bool Bold = false, bool Italic = false, bool Underline = false)
{
    public bool SameFormat(RichSpan o) => Bold == o.Bold && Italic == o.Italic && Underline == o.Underline;
}

public enum ListKind { None, Bullet, Numbered }

/// <summary>One line of a note: an indent level, an optional list marker and formatted spans.</summary>
public sealed class RichLine
{
    public int Indent { get; set; }

    /// <summary>Bulleted or numbered list item. Numbers are worked out from the order (see <see cref="RichText.ListLabels"/>).</summary>
    public ListKind List { get; set; }
    public List<RichSpan> Spans { get; set; } = new();

    public string PlainText => string.Concat(Spans.Select(s => s.Text));
    public bool IsEmpty => Spans.All(s => s.Text.Length == 0);
}

/// <summary>
/// Step notes are stored as plain text with a small markup, so old guides and the Markdown export stay readable:
/// each line is a paragraph, leading tabs are indent levels, and **bold**, *italic* and __underline__ mark
/// formatting. A backslash escapes a marker character. Unmatched markers are kept as ordinary text.
/// A line starting with "- " is a bullet and one starting with "1. " (any number) a numbered list item;
/// the tabs before it set the list level.
/// </summary>
public static partial class RichText
{
    public const int MaxIndent = 8;

    /// <param name="trimBlankEdges">Drop blank lines at the start and end (exports); the editor keeps them.</param>
    public static List<RichLine> Parse(string? text, bool trimBlankEdges = true)
    {
        var lines = new List<RichLine>();
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            int i = 0, indent = 0, spaces = 0;
            for (; i < raw.Length; i++)
            {
                if (raw[i] == '\t') { indent++; spaces = 0; }
                else if (raw[i] == ' ') spaces++;
                else break;
            }
            indent += (spaces + 3) / 4; // older notes indented with spaces: 1-4 spaces = one level
            string rest = raw[i..];
            var list = ListKind.None;
            if (rest.StartsWith("- ") || rest == "-") { list = ListKind.Bullet; rest = rest.Length > 2 ? rest[2..] : ""; }
            else if (ListNumberRegex().Match(rest) is { Success: true } m) { list = ListKind.Numbered; rest = rest[m.Length..]; }
            lines.Add(new RichLine { Indent = Math.Min(indent, MaxIndent), List = list, Spans = ParseInline(rest) });
        }
        if (!trimBlankEdges) return lines;
        // Drop blank lines at the very start and end (like the old Trim()).
        while (lines.Count > 0 && lines[0].IsEmpty) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].IsEmpty) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static List<RichSpan> ParseInline(string s)
    {
        // Pass 1: tokens (text or a marker), resolving escapes.
        var tokens = new List<(string Text, bool Marker)>();
        var buf = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length && s[i + 1] is '*' or '_' or '\\' or '-' or '.' or ')')
            {
                buf.Append(s[++i]);
                continue;
            }
            string? marker = c == '*' ? (i + 1 < s.Length && s[i + 1] == '*' ? "**" : "*")
                : c == '_' && i + 1 < s.Length && s[i + 1] == '_' ? "__" : null;
            if (marker is null) { buf.Append(c); continue; }
            if (buf.Length > 0) { tokens.Add((buf.ToString(), false)); buf.Clear(); }
            tokens.Add((marker, true));
            i += marker.Length - 1;
        }
        if (buf.Length > 0) tokens.Add((buf.ToString(), false));

        // Pass 2: a marker toggles its format only when a matching marker follows; otherwise it's literal text.
        var spans = new List<RichSpan>();
        bool bold = false, italic = false, underline = false;
        for (int t = 0; t < tokens.Count; t++)
        {
            var (text, isMarker) = tokens[t];
            if (isMarker)
            {
                bool on = text switch { "**" => bold, "*" => italic, _ => underline };
                bool closes = on || tokens.Skip(t + 1).Any(x => x.Marker && x.Text == text);
                if (closes)
                {
                    if (text == "**") bold = !bold;
                    else if (text == "*") italic = !italic;
                    else underline = !underline;
                    continue;
                }
            }
            Add(spans, new RichSpan(text, bold, italic, underline));
        }
        return spans;
    }

    private static void Add(List<RichSpan> spans, RichSpan span)
    {
        if (span.Text.Length == 0) return;
        if (spans.Count > 0 && spans[^1].SameFormat(span))
            spans[^1] = spans[^1] with { Text = spans[^1].Text + span.Text };
        else spans.Add(span);
    }

    public static string Serialize(IEnumerable<RichLine> lines)
    {
        var list = lines.ToList();
        var labels = ListLabels(list, markdownStyle: true);
        return string.Join("\n", list.Select((l, i) =>
            new string('\t', Math.Clamp(l.Indent, 0, MaxIndent))
            + (l.List == ListKind.None ? EscapeLineStart(SerializeInline(l.Spans)) : labels[i] + " " + SerializeInline(l.Spans))));
    }

    /// <summary>Plain text that would read as a list marker gets escaped so it stays plain text.</summary>
    private static string EscapeLineStart(string s) =>
        s.StartsWith("- ") || s == "-" ? "\\" + s : ListNumberRegex().IsMatch(s) ? Regex.Replace(s, @"^(\d+)([.)])", "$1\\$2") : s;

    private static readonly string[] Bullets = { "•", "◦", "▪" };

    /// <summary>
    /// The marker shown before each list line: numbered levels go 1. → a. → i. → 1. ..., bullets • → ◦ → ▪.
    /// Numbering continues across blank lines and deeper items, and restarts after a plain line at the
    /// same or a shallower level. Empty for lines that aren't list items.
    /// </summary>
    /// <param name="markdownStyle">"1." and "-" at every level, for storage and Markdown.</param>
    public static List<string> ListLabels(IReadOnlyList<RichLine> lines, bool markdownStyle = false)
    {
        var counters = new int[MaxIndent + 2];
        var labels = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            int level = Math.Clamp(line.Indent, 0, MaxIndent);
            if (line.List == ListKind.None)
            {
                if (!line.IsEmpty) for (int k = level; k < counters.Length; k++) counters[k] = 0;
                labels.Add("");
                continue;
            }
            for (int k = level + 1; k < counters.Length; k++) counters[k] = 0;
            if (line.List == ListKind.Bullet)
            {
                counters[level] = 0;
                labels.Add(markdownStyle ? "-" : Bullets[level % Bullets.Length]);
                continue;
            }
            int n = ++counters[level];
            labels.Add(markdownStyle ? $"{n}." : NumberLabel(n, level) + ".");
        }
        return labels;
    }

    /// <summary>The list marker style for a nesting depth: 0 numbers, 1 letters, 2 roman numerals, then repeat.</summary>
    public static string NumberLabel(int n, int level) => (level % 3) switch
    {
        1 => Letters(n),
        2 => Roman(n),
        _ => n.ToString(),
    };

    private static string Letters(int n)
    {
        var sb = new StringBuilder();
        for (; n > 0; n = (n - 1) / 26) sb.Insert(0, (char)('a' + (n - 1) % 26));
        return sb.ToString();
    }

    private static string Roman(int n)
    {
        if (n <= 0 || n >= 4000) return n.ToString();
        var map = new (int V, string S)[] { (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"),
            (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i") };
        var sb = new StringBuilder();
        foreach (var (v, sym) in map) for (; n >= v; n -= v) sb.Append(sym);
        return sb.ToString();
    }

    private static string SerializeInline(IEnumerable<RichSpan> spans)
    {
        var merged = new List<RichSpan>();
        foreach (var s in spans) Add(merged, s);
        var sb = new StringBuilder();
        foreach (var s in merged)
        {
            // Keep spaces outside markers so "**bold **" doesn't happen.
            string text = s.Text;
            int lead = text.Length - text.TrimStart().Length, trail = text.Length - text.TrimEnd().Length;
            string core = text.Trim();
            if (core.Length == 0 || (!s.Bold && !s.Italic && !s.Underline)) { sb.Append(Escape(text)); continue; }
            string open = (s.Underline ? "__" : "") + (s.Bold ? "**" : "") + (s.Italic ? "*" : "");
            string close = (s.Italic ? "*" : "") + (s.Bold ? "**" : "") + (s.Underline ? "__" : "");
            sb.Append(text[..lead]).Append(open).Append(Escape(core)).Append(close).Append(text[^trail..]);
        }
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("*", "\\*").Replace("__", "\\_\\_");

    /// <summary>Plain text without markup, indented with tabs (for search, tooltips and tests).</summary>
    public static string ToPlain(string? text) =>
        string.Join("\n", Parse(text).Select(l => new string('\t', l.Indent) + (l.List == ListKind.Bullet ? "- " : l.List == ListKind.Numbered ? "# " : "") + l.PlainText));

    [GeneratedRegex(@"^\d{1,4}[.)](\s|$)")]
    private static partial Regex ListNumberRegex();

    /// <summary>Callout colors on a step's screenshot by number (the first callout with each number wins).</summary>
    public static Dictionary<int, uint> CalloutColors(Step step)
    {
        var map = new Dictionary<int, uint>();
        foreach (var m in step.Markup)
            if (m.Kind == MarkupKind.Number && int.TryParse(m.Text, out int n)) map.TryAdd(n, m.Color);
        return map;
    }
}
