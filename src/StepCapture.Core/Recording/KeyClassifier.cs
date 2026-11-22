namespace StepCapture.Core.Recording;

public enum KeyActionKind
{
    /// <summary>Not recorded (modifier on its own, arrow keys, lock keys...).</summary>
    Ignore,
    /// <summary>Produces text; grouped into a "Type ..." step.</summary>
    Text,
    /// <summary>Deletes the last typed character of the current "Type ..." step.</summary>
    Backspace,
    /// <summary>A key or shortcut that becomes its own "Press ..." step.</summary>
    Press,
}

public readonly record struct KeyAction(KeyActionKind Kind, string Text = "")
{
    public static readonly KeyAction Ignored = new(KeyActionKind.Ignore);
}

/// <summary>Decides what a single key-down means for the guide.</summary>
public static class KeyClassifier
{
    private static bool IsModifierKey(int vk) =>
        vk is 0x10 or 0x11 or 0x12 or (>= 0xA0 and <= 0xA5) or 0x5B or 0x5C;

    private static bool IsNavigationOrLockKey(int vk) =>
        vk is (>= 0x21 and <= 0x28) or 0x2D or 0x14 or 0x90 or 0x91 or 0x13 or 0x2C
            or (>= 0xA6 and <= 0xB7); // browser/media/volume keys

    private static bool IsStandaloneKey(int vk) =>
        vk is 0x09 or 0x0D or 0x1B or 0x2E or (>= 0x70 and <= 0x87);

    /// <param name="vk">Virtual-key code.</param>
    /// <param name="mods">Modifiers held at the time.</param>
    /// <param name="text">Text the keyboard layout produces for this key, or null.</param>
    /// <param name="layoutChar">Unshifted character on the key for naming shortcuts, or null.</param>
    public static KeyAction Classify(int vk, Modifiers mods, string? text, char? layoutChar = null)
    {
        if (IsModifierKey(vk)) return KeyAction.Ignored;

        bool hasText = !string.IsNullOrEmpty(text) && !text.Any(char.IsControl);

        // AltGr is reported as Ctrl+Alt. If it produced a character, it's typing, not a shortcut.
        bool altGr = mods.HasFlag(Modifiers.Ctrl) && mods.HasFlag(Modifiers.Alt) && !mods.HasFlag(Modifiers.Win);
        if (altGr && hasText) return new KeyAction(KeyActionKind.Text, text!);

        if ((mods & (Modifiers.Ctrl | Modifiers.Alt | Modifiers.Win)) != 0)
            return new KeyAction(KeyActionKind.Press, KeyNames.WithModifiers(mods, KeyNames.Get(vk, layoutChar)));

        if (vk == 0x08) return new KeyAction(KeyActionKind.Backspace);
        if (IsStandaloneKey(vk))
            return new KeyAction(KeyActionKind.Press, KeyNames.WithModifiers(mods & Modifiers.Shift, KeyNames.Get(vk)));
        if (IsNavigationOrLockKey(vk)) return KeyAction.Ignored;
        if (hasText) return new KeyAction(KeyActionKind.Text, text!);
        return KeyAction.Ignored;
    }
}
