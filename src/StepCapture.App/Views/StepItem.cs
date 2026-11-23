using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StepCapture.App.Imaging;
using StepCapture.Core.Export;
using StepCapture.Core.Model;

namespace StepCapture.App.Views;

/// <summary>Bindable wrapper around a <see cref="Step"/> for the editor.</summary>
public sealed class StepItem : INotifyPropertyChanged
{
    private sealed record Snapshot(byte[]? Image, byte[]? Base, List<MarkupItem> Markup);

    private readonly Stack<Snapshot> _undo = new();
    private ImageSource? _image, _thumbnail, _baseImage;
    private int _number;

    public StepItem(Step model) => Model = model;

    public Step Model { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Number
    {
        get => _number;
        set { if (_number != value) { _number = value; OnChanged(); OnChanged(nameof(BadgeText)); } }
    }

    /// <summary>Overview steps show a heading instead of a number and are left out of the numbering.</summary>
    public bool IsOverview
    {
        get => Model.IsOverview;
        set
        {
            if (Model.IsOverview == value) return;
            Model.IsOverview = value;
            OnChanged();
            OnChanged(nameof(BadgeText));
            OnChanged(nameof(KindLabel));
        }
    }

    /// <summary>Text in the list badge: the step number, or a star for an overview.</summary>
    public string BadgeText => IsOverview ? "\u2605" : _number.ToString();

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
    public bool CanUndoImage => _undo.Count > 0;

    /// <summary>Show the screenshot editor while there is an image, or a removed image to bring back.</summary>
    public bool ShowImageEditor => HasImage || CanUndoImage;

    public string KindLabel => IsOverview ? "Overview" : Model.Kind switch
    {
        StepKind.Screenshot => "Screenshot",
        StepKind.Click => "Click",
        StepKind.RightClick => "Right-click",
        StepKind.MiddleClick => "Middle-click",
        StepKind.DoubleClick => "Double-click",
        StepKind.Type => "Typing",
        StepKind.KeyPress => "Key press",
        _ => "Note",
    };

    /// <summary>The screenshot without markup.</summary>
    public byte[]? BasePng => Model.BaseImagePng ?? Model.ImagePng;

    /// <summary>Editable shapes on the screenshot. Call <see cref="Checkpoint"/> before and <see cref="RenderMarkup"/> after changing them.</summary>
    public List<MarkupItem> Markup => Model.Markup;

    public (int Width, int Height) PixelSize => BasePng is { } png ? PngInfo.GetSize(png) : (0, 0);

    /// <summary>Screenshot with markup (as exported).</summary>
    public ImageSource? Image => _image ??= Decode(Model.ImagePng, 0);
    public ImageSource? Thumbnail => _thumbnail ??= Decode(Model.ImagePng, 240);

    /// <summary>Screenshot without markup, shown under the editable shapes.</summary>
    public ImageSource? BaseImage => _baseImage ??= Decode(BasePng, 0);

    /// <summary>Remembers the current screenshot and markup so the next change can be undone.</summary>
    public void Checkpoint()
    {
        _undo.Push(new Snapshot(Model.ImagePng, Model.BaseImagePng, Markup.Select(m => m.Clone()).ToList()));
        OnChanged(nameof(CanUndoImage));
        OnChanged(nameof(ShowImageEditor));
    }

    /// <summary>Re-draws the exported image after the markup changed.</summary>
    public void RenderMarkup()
    {
        var basePng = BasePng;
        if (basePng is null)
        {
            Model.ImagePng = Model.BaseImagePng = null;
            Markup.Clear();
        }
        else if (Markup.Count == 0)
        {
            Model.ImagePng = basePng;
            Model.BaseImagePng = null;
        }
        else
        {
            Model.BaseImagePng = basePng;
            Model.ImagePng = MarkupRenderer.Render(basePng, Markup);
        }
        _image = _thumbnail = null;
        NotifyImages();
    }

    /// <summary>Replaces the screenshot underneath the markup (blur, crop) or removes it (null).</summary>
    public void ReplaceBase(byte[]? png)
    {
        if (png is null)
        {
            Markup.Clear();
            Model.BaseImagePng = Model.ImagePng = null;
        }
        else
        {
            Model.BaseImagePng = png;
            Model.ImagePng = png;
        }
        _baseImage = null;
        RenderMarkup();
    }

    public void UndoImage()
    {
        if (_undo.Count == 0) return;
        var s = _undo.Pop();
        Model.ImagePng = s.Image;
        Model.BaseImagePng = s.Base;
        Model.Markup = s.Markup;
        _image = _thumbnail = _baseImage = null;
        NotifyImages();
    }

    /// <summary>Re-reads the model after the recorder changed it.</summary>
    public void Refresh()
    {
        OnChanged(nameof(Caption));
        OnChanged(nameof(KindLabel));
    }

    private void NotifyImages()
    {
        OnChanged(nameof(Image));
        OnChanged(nameof(Thumbnail));
        OnChanged(nameof(BaseImage));
        OnChanged(nameof(Markup));
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
