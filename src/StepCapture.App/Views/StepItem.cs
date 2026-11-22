using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StepCapture.Core.Export;
using StepCapture.Core.Model;

namespace StepCapture.App.Views;

/// <summary>Bindable wrapper around a <see cref="Step"/> for the editor.</summary>
public sealed class StepItem : INotifyPropertyChanged
{
    private readonly Stack<(byte[]? Png, int NextCallout)> _imageUndo = new();
    private ImageSource? _image, _thumbnail;
    private int _number;

    public StepItem(Step model) => Model = model;

    public Step Model { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Number
    {
        get => _number;
        set { if (_number != value) { _number = value; OnChanged(); } }
    }

    public string Caption
    {
        get => Model.Caption;
        set { if (Model.Caption != value) { Model.Caption = value; OnChanged(); } }
    }

    public string Notes
    {
        get => Model.Notes;
        set { if (Model.Notes != value) { Model.Notes = value; OnChanged(); OnChanged(nameof(HasNotes)); } }
    }

    public bool HasNotes => !string.IsNullOrWhiteSpace(Model.Notes);
    public bool HasImage => Model.ImagePng is { Length: > 0 };
    public bool CanUndoImage => _imageUndo.Count > 0;

    /// <summary>Show the screenshot editor while there is an image, or a removed image to bring back.</summary>
    public bool ShowImageEditor => HasImage || CanUndoImage;

    public string KindLabel => Model.Kind switch
    {
        StepKind.Click => "Click",
        StepKind.RightClick => "Right-click",
        StepKind.MiddleClick => "Middle-click",
        StepKind.DoubleClick => "Double-click",
        StepKind.Type => "Typing",
        StepKind.KeyPress => "Key press",
        _ => "Note",
    };

    public (int Width, int Height) PixelSize => HasImage ? PngInfo.GetSize(Model.ImagePng!) : (0, 0);

    public ImageSource? Image => _image ??= Decode(Model.ImagePng, 0);
    public ImageSource? Thumbnail => _thumbnail ??= Decode(Model.ImagePng, 240);

    /// <summary>Number the next numbered callout on this screenshot will show.</summary>
    public int NextCallout { get; private set; } = 1;

    /// <summary>Replaces the screenshot, remembering the old one for <see cref="UndoImage"/>.</summary>
    public void SetImage(byte[]? png, bool usedCallout = false)
    {
        _imageUndo.Push((Model.ImagePng, NextCallout));
        if (usedCallout) NextCallout++;
        ApplyImage(png);
    }

    public void UndoImage()
    {
        if (_imageUndo.Count == 0) return;
        var (png, callout) = _imageUndo.Pop();
        NextCallout = callout;
        ApplyImage(png);
    }

    /// <summary>Re-reads the model after the recorder changed it.</summary>
    public void Refresh()
    {
        OnChanged(nameof(Caption));
        OnChanged(nameof(KindLabel));
    }

    private void ApplyImage(byte[]? png)
    {
        Model.ImagePng = png;
        _image = _thumbnail = null;
        OnChanged(nameof(Image));
        OnChanged(nameof(Thumbnail));
        OnChanged(nameof(HasImage));
        OnChanged(nameof(CanUndoImage));
        OnChanged(nameof(ShowImageEditor));
    }

    private static ImageSource? Decode(byte[]? png, int decodeWidth)
    {
        if (png is not { Length: > 0 }) return null;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(png);
        if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
