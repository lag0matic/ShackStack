namespace ShackStack.DecoderHost.Sstv.Harness;

internal static class TestCardFactory
{
    public static byte[] Create(int width, int height)
    {
        var rgb = new byte[width * height * 3];
        var margin = Math.Max(8, width / 32);
        var grid = Math.Max(12, width / 16);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 3;
                var color = BaseColor(width, height, x, y);

                if (x < margin || x >= width - margin || y < margin || y >= height - margin)
                {
                    color = (255, 255, 255);
                }

                if ((x % grid) == 0 || (y % grid) == 0)
                {
                    color = (0, 0, 0);
                }

                if (x == y || x == (width - y - 1))
                {
                    color = (255, 255, 0);
                }

                rgb[offset] = color.R;
                rgb[offset + 1] = color.G;
                rgb[offset + 2] = color.B;
            }
        }

        PaintBars(rgb, width, height);
        return rgb;
    }

    private static (byte R, byte G, byte B) BaseColor(int width, int height, int x, int y)
    {
        if (y < height / 4)
        {
            var third = width / 3;
            if (x < third)
            {
                return (255, 0, 0);
            }

            if (x < third * 2)
            {
                return (0, 255, 0);
            }

            return (0, 0, 255);
        }

        var luma = (byte)Math.Clamp((int)Math.Round((x / (double)(width - 1)) * 255.0), 0, 255);
        return (luma, luma, luma);
    }

    private static void PaintBars(byte[] rgb, int width, int height)
    {
        var barTop = (height * 3) / 4;
        var barHeight = Math.Max(12, height / 16);
        var colors = new (byte R, byte G, byte B)[]
        {
            (255,255,255),
            (255,255,0),
            (0,255,255),
            (0,255,0),
            (255,0,255),
            (255,0,0),
            (0,0,255),
            (0,0,0),
        };

        var barWidth = width / colors.Length;
        for (var idx = 0; idx < colors.Length; idx++)
        {
            var color = colors[idx];
            var startX = idx * barWidth;
            var endX = idx == colors.Length - 1 ? width : startX + barWidth;
            for (var y = barTop; y < Math.Min(height, barTop + barHeight); y++)
            {
                for (var x = startX; x < endX; x++)
                {
                    var offset = ((y * width) + x) * 3;
                    rgb[offset] = color.R;
                    rgb[offset + 1] = color.G;
                    rgb[offset + 2] = color.B;
                }
            }
        }
    }
}
