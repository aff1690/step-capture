namespace StepCapture.Core.Recording;

/// <summary>Human-readable names for Windows virtual-key codes.</summary>
public static class KeyNames
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause",
        [0x14] = "Caps Lock", [0x1B] = "Esc", [0x20] = "Space", [0x21] = "Page Up",
        [0x22] = "Page Down", [0x23] = "End", [0x24] = "Home", [0x25] = "Left",
        [0x26] = "Up", [0x27] = "Right", [0x28] = "Down", [0x2C] = "Print Screen",
        [0x2D] = "Insert", [0x2E] = "Delete", [0x5D] = "Menu",
        [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num -", [0x6E] = "Num .", [0x6F] = "Num /",
        [0x90] = "Num Lock", [0x91] = "Scroll Lock",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/",
        [0xC0] = "`", [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
    };

    /// <param name="vk">Virtual-key code.</param>
    /// <param name="layoutChar">Character the current keyboard layout maps the key to, if known.
    /// Used for punctuation keys whose meaning depends on the layout.</param>
    public static string Get(int vk, char? layoutChar = null)
    {
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();          // A-Z
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();          // 0-9
        if (vk is >= 0x60 and <= 0x69) return "Num " + (vk - 0x60);          // numpad digits
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);              // F1-F24
        if (vk is >= 0xBA and <= 0xDF && layoutChar is { } c && !char.IsControl(c))
            return char.ToUpperInvariant(c).ToString();
        return Names.TryGetValue(vk, out var name) ? name : $"Key 0x{vk:X2}";
    }

    public static string WithModifiers(Modifiers mods, string key)
    {
        var parts = new List<string>(5);
        if (mods.HasFlag(Modifiers.Ctrl)) parts.Add("Ctrl");
        if (mods.HasFlag(Modifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(Modifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(Modifiers.Win)) parts.Add("Win");
        parts.Add(key);
        return string.Join(" + ", parts);
    }
}
