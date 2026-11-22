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
}

/// <summary>One step of a guide: a caption, optional notes and an optional annotated screenshot (PNG).</summary>
public sealed class Step
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public StepKind Kind { get; set; }
    public string Caption { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>Application the step happened in (for example "Notepad"). Informational.</summary>
    public string AppName { get; set; } = "";

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    /// <summary>Annotated PNG screenshot, or null for a text-only step.</summary>
    public byte[]? ImagePng { get; set; }

    public Step Clone() => new()
    {
        Id = Guid.NewGuid(),
        Kind = Kind,
        Caption = Caption,
        Notes = Notes,
        AppName = AppName,
        Timestamp = Timestamp,
        ImagePng = ImagePng,
    };
}
