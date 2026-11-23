using System.Text;
using System.Text.RegularExpressions;

namespace StepCapture.Core.Model;

/// <summary>A run of note text with one formatting.</summary>
public sealed record RichSpan(string Text, bool Bold = false, bool Italic = false, bool Underline = false)
{
    public bool SameFormat(RichSpan o) => Bold == o.Bold && Italic == o.Italic && Underline == o.Underline;
}

/// <summary>One line of a note: an indent level and formatted spans.</summary>
public sealed class RichLine
{
    public int Indent { get; set; }
    public List<RichSpan> Spans { get; set; } = new();

    public string PlainText => string.Concat(Spans.Select(s => s.Text));
    public bool IsEmpty => Spans.All(s => s.Text.Length == 0);
}

/// <summary>
/// Step notes are stored as plain text with a small markup, so old guides and the Markdown export stay readable:
/// each line is a paragraph, leading tabs are indent levels, and **bold**, *italic* and __underline__ mark
/// formatting. A backslash escapes a marker character. Unmatched markers are kept as ordinary text.
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
            lines.Add(new RichLine { Indent = Math.Min(indent, MaxIndent), Spans = ParseInline(raw[i..]) });
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
            if (c == '\\' && i + 1 < s.Length && s[i + 1] is '*' or '_' or '\\')
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

    public static string Serialize(IEnumerable<RichLine> lines) =>
        string.Join("\n", lines.Select(l => new string('\t', Math.Clamp(l.Indent, 0, MaxIndent)) + SerializeInline(l.Spans)));

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
        string.Join("\n", Parse(text).Select(l => new string('\t', l.Indent) + l.PlainText));

    /// <summary>
    /// If the line starts with a number such as "2." or "2)", returns it and the line with the number removed.
    /// Used to show numbered notes with the same badge as the matching callout on the screenshot.
    /// </summary>
    public static bool TryLeadingNumber(RichLine line, out int number, out RichLine rest)
    {
        number = 0;
        rest = line;
        if (line.Spans.Count == 0) return false;
        var m = LeadingNumberRegex().Match(line.Spans[0].Text);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out number)) return false;
        var spans = new List<RichSpan>(line.Spans);
        spans[0] = spans[0] with { Text = spans[0].Text[m.Length..] };
        if (spans[0].Text.Length == 0) spans.RemoveAt(0);
        rest = new RichLine { Indent = line.Indent, Spans = spans };
        return true;
    }

    [GeneratedRegex(@"^\s*(\d{1,3})[.)](\s+|$)")]
    private static partial Regex LeadingNumberRegex();

    /// <summary>
    /// Adds a "n. " line for a new numbered callout unless the notes already have one, so the
    /// description of each callout only needs typing in.
    /// </summary>
    public static string EnsureNumberedLine(string? notes, int number)
    {
        var lines = Parse(notes);
        if (lines.Any(l => TryLeadingNumber(l, out int n, out _) && n == number)) return notes ?? "";
        string trimmed = (notes ?? "").TrimEnd('\n', '\r', ' ', '\t');
        return (trimmed.Length == 0 ? "" : trimmed + "\n") + $"{number}. ";
    }

    /// <summary>Callout colors on a step's screenshot by number (the first callout with each number wins).</summary>
    public static Dictionary<int, uint> CalloutColors(Step step)
    {
        var map = new Dictionary<int, uint>();
        foreach (var m in step.Markup)
            if (m.Kind == MarkupKind.Number && int.TryParse(m.Text, out int n)) map.TryAdd(n, m.Color);
        return map;
    }
}
