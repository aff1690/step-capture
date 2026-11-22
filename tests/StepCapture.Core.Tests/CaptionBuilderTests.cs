using StepCapture.Core.Model;
using StepCapture.Core.Recording;

namespace StepCapture.Core.Tests;

public class CaptionBuilderTests
{
    [Fact]
    public void UnnamedElementFallsBackToWindowTitle() =>
        Assert.Equal("Click in \"Untitled - Notepad\"",
            CaptionBuilder.ForClick(StepKind.Click, new ElementInfo { ControlType = "pane", WindowTitle = "Untitled - Notepad" }));

    [Fact]
    public void EditIsCalledTextBox() =>
        Assert.Equal("Click the \"Search\" text box",
            CaptionBuilder.ForClick(StepKind.Click, new ElementInfo { Name = "Search", ControlType = "edit" }));

    [Fact]
    public void LongNamesAreShortened()
    {
        string caption = CaptionBuilder.ForClick(StepKind.Click, new ElementInfo { Name = new string('x', 200), ControlType = "button" });
        Assert.Contains("…", caption);
        Assert.True(caption.Length < 90);
    }

    [Fact]
    public void NoElementInfoStillProducesCaption() =>
        Assert.Equal("Right-click here", CaptionBuilder.ForClick(StepKind.RightClick, null));
}
