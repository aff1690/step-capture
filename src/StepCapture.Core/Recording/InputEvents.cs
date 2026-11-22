using StepCapture.Core.Model;

namespace StepCapture.Core.Recording;

public enum MouseButton { Left, Right, Middle }

/// <summary>A mouse button press captured during recording.</summary>
public sealed record ClickInput(
    DateTimeOffset Time,
    int ScreenX,
    int ScreenY,
    MouseButton Button,
    ElementInfo? Element,
    byte[]? ImagePng);

/// <summary>A key press captured during recording, already classified.</summary>
public sealed record KeyInput(
    DateTimeOffset Time,
    KeyAction Action,
    ElementInfo? FocusedElement,
    byte[]? ImagePng);
