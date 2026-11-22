using System.Text;
using StepCapture.Core.Model;

namespace StepCapture.Core.Recording;

/// <summary>
/// Turns a stream of raw clicks and key presses into guide steps.
/// Consecutive characters are merged into one "Type ..." step, a second click at the
/// same spot within the double-click time upgrades the previous step to a double-click,
/// and passwords are never stored.
/// Not thread-safe: feed it from a single thread.
/// </summary>
public sealed class StepBuilder
{
    private readonly int _doubleClickMs;
    private readonly int _doubleClickDistance;

    private Step? _typingStep;
    private readonly StringBuilder _typed = new();
    private bool _typingIsPassword;
    private int _passwordLength;
    private ElementInfo? _typingField;

    private Step? _lastClickStep;
    private ClickInput? _lastClick;

    public StepBuilder(int doubleClickMs = 500, int doubleClickDistance = 4)
    {
        _doubleClickMs = doubleClickMs;
        _doubleClickDistance = doubleClickDistance;
    }

    public event Action<Step>? StepAdded;
    public event Action<Step>? StepUpdated;
    public event Action<Step>? StepRemoved;

    /// <summary>True while characters are being merged into a "Type ..." step.</summary>
    public bool IsTyping => _typingStep is not null;

    public void OnClick(ClickInput click)
    {
        EndTyping();

        if (IsDoubleClick(click))
        {
            var step = _lastClickStep!;
            step.Kind = StepKind.DoubleClick;
            step.Caption = CaptionBuilder.ForClick(StepKind.DoubleClick, _lastClick!.Element ?? click.Element);
            _lastClick = null; // a third click starts over
            _lastClickStep = null;
            StepUpdated?.Invoke(step);
            return;
        }

        var kind = click.Button switch
        {
            MouseButton.Right => StepKind.RightClick,
            MouseButton.Middle => StepKind.MiddleClick,
            _ => StepKind.Click,
        };

        var newStep = new Step
        {
            Kind = kind,
            Caption = CaptionBuilder.ForClick(kind, click.Element),
            AppName = click.Element?.AppName ?? "",
            Timestamp = click.Time,
            ImagePng = click.ImagePng,
        };
        _lastClick = click;
        _lastClickStep = newStep;
        StepAdded?.Invoke(newStep);
    }

    public void OnKey(KeyInput key)
    {
        switch (key.Action.Kind)
        {
            case KeyActionKind.Ignore:
                return;

            case KeyActionKind.Text:
                _lastClick = null;
                if (_typingStep is null) StartTyping(key);
                if (_typingIsPassword) _passwordLength += key.Action.Text.Length;
                else _typed.Append(key.Action.Text);
                RefreshTypingCaption();
                return;

            case KeyActionKind.Backspace:
                if (_typingStep is null) return;
                if (_typingIsPassword) _passwordLength = Math.Max(0, _passwordLength - 1);
                else if (_typed.Length > 0) _typed.Length--;
                RefreshTypingCaption();
                return;

            case KeyActionKind.Press:
                EndTyping();
                _lastClick = null;
                StepAdded?.Invoke(new Step
                {
                    Kind = StepKind.KeyPress,
                    Caption = CaptionBuilder.ForKeyPress(key.Action.Text),
                    AppName = key.FocusedElement?.AppName ?? "",
                    Timestamp = key.Time,
                    ImagePng = key.ImagePng,
                });
                return;
        }
    }

    /// <summary>Closes any open "Type ..." step. Call when recording stops or pauses.</summary>
    public void Flush() => EndTyping();

    private bool IsDoubleClick(ClickInput click) =>
        _lastClick is { } prev && _lastClickStep is not null
        && prev.Button == MouseButton.Left && click.Button == MouseButton.Left
        && (click.Time - prev.Time).TotalMilliseconds <= _doubleClickMs
        && Math.Abs(click.ScreenX - prev.ScreenX) <= _doubleClickDistance
        && Math.Abs(click.ScreenY - prev.ScreenY) <= _doubleClickDistance;

    private void StartTyping(KeyInput key)
    {
        _typed.Clear();
        _passwordLength = 0;
        _typingField = key.FocusedElement;
        _typingIsPassword = key.FocusedElement?.IsPassword ?? false;
        _typingStep = new Step
        {
            Kind = StepKind.Type,
            AppName = key.FocusedElement?.AppName ?? "",
            Timestamp = key.Time,
            ImagePng = key.ImagePng,
        };
        _typingStep.Caption = CaptionBuilder.ForTyping("", _typingIsPassword, _typingField);
        StepAdded?.Invoke(_typingStep);
    }

    private void RefreshTypingCaption()
    {
        if (_typingStep is null) return;
        _typingStep.Caption = CaptionBuilder.ForTyping(_typed.ToString(), _typingIsPassword, _typingField);
        StepUpdated?.Invoke(_typingStep);
    }

    private void EndTyping()
    {
        if (_typingStep is null) return;
        var step = _typingStep;
        bool empty = _typingIsPassword ? _passwordLength == 0 : _typed.Length == 0;
        _typingStep = null;
        _typed.Clear();
        _passwordLength = 0;
        if (empty) StepRemoved?.Invoke(step); // everything typed was deleted again
    }
}
