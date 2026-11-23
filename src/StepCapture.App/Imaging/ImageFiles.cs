using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StepCapture.App.Imaging;

/// <summary>Turns pictures from files or the clipboard into the PNG bytes a step stores.</summary>
internal static class ImageFiles
{
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    public static bool IsImage(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static byte[] LoadAsPng(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad; // read now so the file isn't kept open
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.UriSource = new Uri(Path.GetFullPath(path));
        bmp.EndInit();
        bmp.Freeze();
        return EncodePng(bmp);
    }

    /// <summary>Encodes as PNG at 96 DPI so exports size the picture by its pixels, like captured screenshots.</summary>
    public static byte[] EncodePng(BitmapSource source)
    {
        BitmapSource bmp = source;
        if (bmp.Format != PixelFormats.Bgra32)
            bmp = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        if (Math.Abs(bmp.DpiX - 96) > 0.5 || Math.Abs(bmp.DpiY - 96) > 0.5)
        {
            int stride = bmp.PixelWidth * 4;
            var pixels = new byte[stride * bmp.PixelHeight];
            bmp.CopyPixels(pixels, stride, 0);
            bmp = BitmapSource.Create(bmp.PixelWidth, bmp.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
