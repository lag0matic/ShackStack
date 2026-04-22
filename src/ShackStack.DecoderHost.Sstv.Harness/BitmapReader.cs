namespace ShackStack.DecoderHost.Sstv.Harness;

internal static class BitmapReader
{
    public static byte[] LoadRgb24(string path, int expectedWidth, int expectedHeight)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        var signature = new string(reader.ReadChars(2));
        if (!string.Equals(signature, "BM", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Not a BMP file.");
        }

        reader.ReadInt32();
        reader.ReadInt32();
        var pixelOffset = reader.ReadInt32();
        var headerSize = reader.ReadInt32();
        if (headerSize < 40)
        {
            throw new InvalidDataException("Unsupported BMP header.");
        }

        var width = reader.ReadInt32();
        var rawHeight = reader.ReadInt32();
        reader.ReadInt16();
        var bitsPerPixel = reader.ReadInt16();
        var compression = reader.ReadInt32();
        var height = Math.Abs(rawHeight);
        if (width != expectedWidth
            || height != expectedHeight
            || (bitsPerPixel != 24 && bitsPerPixel != 32)
            || compression != 0)
        {
            throw new InvalidDataException("Unexpected BMP format.");
        }

        stream.Position = pixelOffset;
        var bytesPerPixel = bitsPerPixel / 8;
        var rowStride = width * bytesPerPixel;
        var paddedRowStride = (rowStride + 3) & ~3;
        var rgb = new byte[width * height * 3];
        var row = new byte[paddedRowStride];

        for (var rowIndex = 0; rowIndex < height; rowIndex++)
        {
            stream.ReadExactly(row);
            var y = rawHeight > 0 ? height - 1 - rowIndex : rowIndex;
            for (var x = 0; x < width; x++)
            {
                var src = x * bytesPerPixel;
                var dst = ((y * width) + x) * 3;
                rgb[dst] = row[src + 2];
                rgb[dst + 1] = row[src + 1];
                rgb[dst + 2] = row[src];
            }
        }

        return rgb;
    }
}
