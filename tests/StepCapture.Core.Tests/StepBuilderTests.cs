using StepCapture.Core.Model;
using StepCapture.Core.Recording;

namespace StepCapture.Core.Tests;

public class StepBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly ElementInfo SaveButton = new() { Name = "Save", ControlType = "button", AppName = "Notepad" };
    private static readonly ElementInfo NameField = new() { Name = "File name", ControlType = "edit" };
    private static readonly ElementInfo PasswordField = new() { Name = "Password", ControlType = "edit", IsPassword = true };

    private sealed class Recorder
    {
        public readonly StepBuilder Builder = new();
        public readonly List<Step> Steps = new();
        public Recorder()
        {
            Builder.StepAdded += s => Steps.Add(s);
            Builder.StepRemoved += s => Steps.Remove(s);
        }
        public void Type(string text, ElementInfo? field = null, int ms = 0)
        {
            foreach (var c in text)
                Builder.OnKey(new KeyInput(T0.AddMilliseconds(ms), new KeyAction(KeyActionKind.Text, c.ToString()), field, null));
        }
        public void Key(KeyActionKind kind, string text = "") =>
            Builder.OnKey(new KeyInput(T0, new KeyAction(kind, text), null, null));
        public void Click(int x, int y, int ms, MouseButton b = MouseButton.Left, ElementInfo? e = null) =>
            Builder.OnClick(new ClickInput(T0.AddMilliseconds(ms), x, y, b, e, null));
    }

    [Fact]
    public void ClickCreatesCaptionedStep()
    {
        var r = new Recorder();
        r.Click(10, 10, 0, e: SaveButton);
        var step = Assert.Single(r.Steps);
        Assert.Equal(StepKind.Click, step.Kind);
        Assert.Equal("Click the \"Save\" button", step.Caption);
        Assert.Equal("Notepad", step.AppName);
    }

    [Fact]
    public void ConsecutiveCharactersBecomeOneTypingStep()
    {
        var r = new Recorder();
        r.Type("hello world", NameField);
        var step = Assert.Single(r.Steps);
        Assert.Equal(StepKind.Type, step.Kind);
        Assert.Equal("Type \"hello world\" in the \"File name\" field", step.Caption);
    }

    [Fact]
    public void BackspaceRemovesLastCharacter()
    {
        var r = new Recorder();
        r.Type("helo");
        r.Key(KeyActionKind.Backspace);
        r.Type("lo");
        Assert.Equal("Type \"hello\"", Assert.Single(r.Steps).Caption);
    }

    [Fact]
    public void TypingStepThatIsFullyDeletedIsRemoved()
    {
        var r = new Recorder();
        r.Type("ab");
        r.Key(KeyActionKind.Backspace);
        r.Key(KeyActionKind.Backspace);
        r.Builder.Flush();
        Assert.Empty(r.Steps);
    }

    [Fact]
    public void PasswordTextIsNeverStored()
    {
        var r = new Recorder();
        r.Type("hunter2", PasswordField);
        var step = Assert.Single(r.Steps);
        Assert.Equal("Type your password in the \"Password\" field", step.Caption);
        Assert.DoesNotContain("hunter2", step.Caption);
    }

    [Fact]
    public void PressEndsTypingAndAddsItsOwnStep()
    {
        var r = new Recorder();
        r.Type("abc");
        r.Key(KeyActionKind.Press, "Enter");
        r.Type("d");
        Assert.Collection(r.Steps,
            s => Assert.Equal("Type \"abc\"", s.Caption),
            s => Assert.Equal("Press Enter", s.Caption),
            s => Assert.Equal("Type \"d\"", s.Caption));
    }

    [Fact]
    public void QuickSecondClickAtSameSpotBecomesDoubleClick()
    {
        var r = new Recorder();
        r.Click(100, 100, 0, e: SaveButton);
        r.Click(101, 102, 200);
        var step = Assert.Single(r.Steps);
        Assert.Equal(StepKind.DoubleClick, step.Kind);
        Assert.Equal("Double-click the \"Save\" button", step.Caption);
    }

    [Fact]
    public void SlowOrDistantSecondClickIsSeparate()
    {
        var r = new Recorder();
        r.Click(100, 100, 0);
        r.Click(100, 100, 900);
        r.Click(300, 100, 1000);
        Assert.Equal(3, r.Steps.Count);
        Assert.All(r.Steps, s => Assert.Equal(StepKind.Click, s.Kind));
    }

    [Fact]
    public void TripleClickIsDoubleClickPlusClick()
    {
        var r = new Recorder();
        r.Click(5, 5, 0);
        r.Click(5, 5, 100);
        r.Click(5, 5, 200);
        Assert.Collection(r.Steps,
            s => Assert.Equal(StepKind.DoubleClick, s.Kind),
            s => Assert.Equal(StepKind.Click, s.Kind));
    }

    [Fact]
    public void RightClickIsNeverMergedIntoDoubleClick()
    {
        var r = new Recorder();
        r.Click(5, 5, 0, MouseButton.Right);
        r.Click(5, 5, 100, MouseButton.Right);
        Assert.Equal(2, r.Steps.Count);
        Assert.All(r.Steps, s => Assert.StartsWith("Right-click", s.Caption));
    }
}
