using System.IO.Compression;
using System.Buffers.Binary;
using StepCapture.Core.Model;

namespace StepCapture.Core.Tests;

internal static class TestData
{
    /// <summary>Builds a valid solid-color RGB PNG of the given size.</summary>
    public static byte[] Png(int width, int height, byte r = 200, byte g = 220, byte b = 255)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; ihdr[9] = 2; // 8-bit RGB
        Chunk(ms, "IHDR", ihdr);

        var raw = new byte[(width * 3 + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int row = y * (width * 3 + 1);
            for (int x = 0; x < width; x++)
            {
                raw[row + 1 + x * 3] = r; raw[row + 2 + x * 3] = g; raw[row + 3 + x * 3] = b;
            }
        }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Fastest, leaveOpen: true)) zs.Write(raw);
        Chunk(ms, "IDAT", z.ToArray());
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc32(typeBytes.Concat(data).ToArray());
        Span<byte> c = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    public static Guide SampleGuide() => new()
    {
        Title = "Save a file in Notepad",
        Description = "How to save your work.",
        Steps =
        {
            new Step { Kind = StepKind.Click, Caption = "Click the \"File\" menu item", ImagePng = Png(320, 200) },
            new Step { Kind = StepKind.Type, Caption = "Type \"notes <draft> & more\"", Notes = "Use any name.", ImagePng = Png(1920, 1080) },
            new Step { Kind = StepKind.KeyPress, Caption = "Press Enter" },
        },
    };
}
