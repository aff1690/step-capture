using StepCapture.Core.Model;

namespace StepCapture.Core.Recording;

/// <summary>Writes plain-English captions for recorded steps.</summary>
public static class CaptionBuilder
{
    // Long names wrap in exports; this only guards against absurd window titles.
    private const int MaxNameLength = 200;

    public static string ForClick(StepKind kind, ElementInfo? element)
    {
        string verb = kind switch
        {
            StepKind.RightClick => "Right-click",
            StepKind.DoubleClick => "Double-click",
            StepKind.MiddleClick => "Middle-click",
            _ => "Click",
        };

        if (element is null) return verb + " here";

        string name = Clean(element.Name);
        string type = FriendlyControlType(element.ControlType);

        if (name.Length > 0)
            return type.Length > 0 ? $"{verb} the \"{name}\" {type}" : $"{verb} \"{name}\"";

        string where = Clean(element.WindowTitle);
        if (where.Length == 0) where = Clean(element.AppName);
        if (type.Length > 0 && where.Length > 0) return $"{verb} the {type} in \"{where}\"";
        if (where.Length > 0) return $"{verb} in \"{where}\"";
        if (type.Length > 0) return $"{verb} the {type}";
        return verb + " here";
    }

    public static string ForTyping(string typed, bool isPassword, ElementInfo? field)
    {
        string fieldName = Clean(field?.Name ?? "");
        string target = fieldName.Length > 0 ? $" in the \"{fieldName}\" field" : "";
        if (isPassword) return "Type your password" + target;
        return $"Type \"{typed}\"" + target;
    }

    public static string ForKeyPress(string keys) => $"Press {keys}";

    /// <summary>Caption for a screenshot taken without a click, for example "Overview of Notepad".</summary>
    public static string ForScreenshot(string? appName) =>
        string.IsNullOrWhiteSpace(appName) ? "Overview" : $"Overview of {Clean(appName)}";

    internal static string FriendlyControlType(string controlType)
    {
        string t = (controlType ?? "").Trim().ToLowerInvariant();
        return t switch
        {
            "edit" => "text box",
            "pane" or "window" or "custom" or "group" or "" => "",
            "document" => "document",
            "list item" => "item",
            "tree item" => "item",
            "data item" => "item",
            "hyperlink" => "link",
            _ => t,
        };
    }

    internal static string Clean(string s)
    {
        s = (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Length > MaxNameLength ? s[..(MaxNameLength - 1)].TrimEnd() + "…" : s;
    }
}
