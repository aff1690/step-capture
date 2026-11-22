namespace StepCapture.Core.Model;

/// <summary>What sits under the mouse or has keyboard focus, as reported by Windows UI Automation.</summary>
public sealed record ElementInfo
{
    /// <summary>Accessible name, such as "Save" or "File name".</summary>
    public string Name { get; init; } = "";

    /// <summary>Localized control type, such as "button", "menu item" or "edit".</summary>
    public string ControlType { get; init; } = "";

    /// <summary>Title of the top-level window that owns the element.</summary>
    public string WindowTitle { get; init; } = "";

    /// <summary>Friendly application name, such as "Notepad".</summary>
    public string AppName { get; init; } = "";

    public bool IsPassword { get; init; }
}
