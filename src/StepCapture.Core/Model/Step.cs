namespace StepCapture.Core.Model;

public enum StepKind
{
    Click,
    RightClick,
    MiddleClick,
    DoubleClick,
    Type,
    KeyPress,
    Note,
    /// <summary>A screenshot taken without a click (camera button, hotkey, or added in the editor).</summary>
    Screenshot,
}

/// <summary>One step of a guide: a caption, optional notes and an optional annotated screenshot (PNG).</summary>
public sealed class Step
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public StepKind Kind { get; set; }
    public string Caption { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>
    /// An overview step (for example "The main screen") shown with a heading instead of a step number.
    /// It is not counted in the step numbering.
    /// </summary>
    public bool IsOverview { get; set; }

    /// <summary>Application the step happened in (for example "Notepad"). Informational.</summary>
    public string AppName { get; set; } = "";

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// The screenshot as it appears in exports: <see cref="BaseImagePng"/> with <see cref="Markup"/> drawn on top.
    /// Null for a text-only step.
    /// </summary>
    public byte[]? ImagePng { get; set; }

    /// <summary>
    /// The screenshot without the editable markup, or null when there is no markup
    /// (then <see cref="ImagePng"/> is the plain screenshot).
    /// </summary>
    public byte[]? BaseImagePng { get; set; }

    /// <summary>Editable shapes drawn on the screenshot (arrows, boxes, text...).</summary>
    public List<MarkupItem> Markup { get; set; } = new();

    public Step Clone() => new()
    {
        Id = Guid.NewGuid(),
        Kind = Kind,
        Caption = Caption,
        Notes = Notes,
        IsOverview = IsOverview,
        AppName = AppName,
        Timestamp = Timestamp,
        ImagePng = ImagePng,
        BaseImagePng = BaseImagePng,
        Markup = Markup.Select(m => m.Clone()).ToList(),
    };
}
