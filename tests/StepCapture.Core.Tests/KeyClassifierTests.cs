using StepCapture.Core.Recording;

namespace StepCapture.Core.Tests;

public class KeyClassifierTests
{
    [Fact]
    public void PlainLetterIsText() =>
        Assert.Equal(new KeyAction(KeyActionKind.Text, "a"), KeyClassifier.Classify(0x41, Modifiers.None, "a"));

    [Fact]
    public void ShiftedLetterIsText() =>
        Assert.Equal(new KeyAction(KeyActionKind.Text, "A"), KeyClassifier.Classify(0x41, Modifiers.Shift, "A"));

    [Fact]
    public void CtrlLetterIsShortcut() =>
        Assert.Equal(new KeyAction(KeyActionKind.Press, "Ctrl + S"), KeyClassifier.Classify(0x53, Modifiers.Ctrl, "\u0013"));

    [Fact]
    public void CtrlShiftShortcutListsModifiersInOrder() =>
        Assert.Equal("Ctrl + Shift + N", KeyClassifier.Classify(0x4E, Modifiers.Ctrl | Modifiers.Shift, null).Text);

    [Fact]
    public void AltGrCharacterIsText() =>
        Assert.Equal(new KeyAction(KeyActionKind.Text, "@"), KeyClassifier.Classify(0x51, Modifiers.Ctrl | Modifiers.Alt, "@"));

    [Theory]
    [InlineData(0x10)] [InlineData(0xA2)] [InlineData(0x5B)] // Shift, LCtrl, LWin
    [InlineData(0x25)] [InlineData(0x28)] [InlineData(0x14)] // Left, Down, Caps Lock
    public void ModifiersAndNavigationAreIgnored(int vk) =>
        Assert.Equal(KeyActionKind.Ignore, KeyClassifier.Classify(vk, Modifiers.None, null).Kind);

    [Theory]
    [InlineData(0x0D, "Enter")] [InlineData(0x09, "Tab")] [InlineData(0x1B, "Esc")]
    [InlineData(0x2E, "Delete")] [InlineData(0x74, "F5")]
    public void StandaloneKeysArePresses(int vk, string name) =>
        Assert.Equal(new KeyAction(KeyActionKind.Press, name), KeyClassifier.Classify(vk, Modifiers.None, vk == 0x0D ? "\r" : null));

    [Fact]
    public void ShiftTabKeepsShift() =>
        Assert.Equal("Shift + Tab", KeyClassifier.Classify(0x09, Modifiers.Shift, "\t").Text);

    [Fact]
    public void BackspaceIsBackspace() =>
        Assert.Equal(KeyActionKind.Backspace, KeyClassifier.Classify(0x08, Modifiers.None, "\b").Kind);

    [Fact]
    public void PunctuationShortcutUsesLayoutCharacter() =>
        Assert.Equal("Ctrl + .", KeyClassifier.Classify(0xBE, Modifiers.Ctrl, null, '.').Text);
}
