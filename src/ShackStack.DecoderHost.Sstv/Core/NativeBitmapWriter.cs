namespace ShackStack.DecoderHost.Sstv.Core;

internal static class NativeBitmapWriter
{
    public static void SaveRgb24(string path, byte[] rgb, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var rowStride = width * 3;
        var paddedRowStride = (rowStride + 3) & ~3;
        var pixelDataSize = paddedRowStride * height;
        var fileSize = 14 + 40 + pixelDataSize;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(14 + 40);

        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(pixelDataSize);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        Span<byte> padded = paddedRowStride <= 4096
            ? stackalloc byte[paddedRowStride]
            : new byte[paddedRowStride];

        for (var y = height - 1; y >= 0; y--)
        {
            padded.Clear();
            var srcOffset = y * rowStride;
            for (var x = 0; x < width; x++)
            {
                var src = srcOffset + (x * 3);
                var dst = x * 3;
                padded[dst] = rgb[src + 2];
                padded[dst + 1] = rgb[src + 1];
                padded[dst + 2] = rgb[src];
            }

            writer.Write(padded[..paddedRowStride]);
        }
    }
}
