using System.Buffers.Binary;

namespace StepCapture.Core.Export;

public static class PngInfo
{
    /// <summary>Reads width and height from a PNG header without decoding the image.</summary>
    public static (int Width, int Height) GetSize(byte[] png)
    {
        if (png.Length < 24 || png[0] != 0x89 || png[1] != (byte)'P' || png[2] != (byte)'N' || png[3] != (byte)'G')
            throw new FormatException("Not a PNG image.");
        int w = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        int h = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        return (w, h);
    }
}
