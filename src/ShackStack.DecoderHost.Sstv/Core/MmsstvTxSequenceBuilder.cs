namespace ShackStack.DecoderHost.Sstv.Core;

internal static class MmsstvTxSequenceBuilder
{
    private const double FreqSync = 1200.0;
    private const double FreqPorch = 1500.0;
    private const double FreqVisStart = 1900.0;
    private const double FreqVisZero = 1300.0;
    private const double FreqVisOne = 1100.0;

    public static IReadOnlyList<MmsstvTxToneSegment> BuildVisOnly(MmsstvTxConfiguration tx)
    {
        var segments = new List<MmsstvTxToneSegment>(24);
        AppendOutHead(segments, tx.Profile);
        if (tx.Profile.Family.Equals("avt", StringComparison.OrdinalIgnoreCase))
        {
            AppendAvtControl(segments, tx.Profile);
        }
        else
        {
            AppendMmsstvVisHeader(segments, tx.Profile.VisCode);
        }

        return segments;
    }

    public static IReadOnlyList<MmsstvTxToneSegment> BuildImageTones(byte[] rgb24, MmsstvTxConfiguration tx, MmsstvTxOptions? options = null)
    {
        var profile = tx.Profile;
        var segments = new List<MmsstvTxToneSegment>(profile.Height * 1000);
        AppendOutHead(segments, profile);
        if (profile.Family.Equals("avt", StringComparison.OrdinalIgnoreCase))
        {
            AppendAvtControl(segments, profile);
        }
        else
        {
            AppendMmsstvVisHeader(segments, profile.VisCode);
        }

        if (profile.Family.Equals("avt", StringComparison.OrdinalIgnoreCase))
        {
            for (var y = 0; y < tx.PictureHeight; y++)
            {
                ExtractRgbRow(rgb24, profile, y, out var red, out var green, out var blue);
                AppendAvtLine(segments, red, green, blue, profile);
            }

            AppendMmsstvFooter(segments, tx, options);
            return segments;
        }

        if (profile.Family.Equals("pd", StringComparison.OrdinalIgnoreCase))
        {
            for (var y = 0; y < tx.PictureHeight - 1; y += 2)
            {
                ExtractRgbRow(rgb24, profile, y, out var red0, out var green0, out var blue0);
                ExtractRgbRow(rgb24, profile, y + 1, out var red1, out var green1, out var blue1);
                RgbToMmsstvYrybyRows(red0, green0, blue0, red1, green1, blue1, out var y0, out var y1, out var by, out var ry);
                AppendPdPair(segments, y0, y1, by, ry, profile);
            }

            AppendMmsstvFooter(segments, tx, options);
            return segments;
        }

        for (var y = 0; y < tx.PictureHeight; y++)
        {
            ExtractTxRgbRow(rgb24, profile, y, out var red, out var green, out var blue);
            switch (profile.Family)
            {
                case "martin":
                    AppendMartinLine(segments, red, green, blue, profile);
                    break;
                case "scottie":
                    AppendScottieLine(segments, red, green, blue, profile);
                    break;
                case "robot36":
                    AppendRobot36Line(segments, red, green, blue, y, profile);
                    break;
                case "robot":
                    AppendRobotLine(segments, red, green, blue, profile);
                    break;
                default:
                    throw new InvalidOperationException($"TX sequencing is not harvested yet for {profile.Name}.");
            }
        }

        AppendMmsstvFooter(segments, tx, options);
        return segments;
    }

    private static void AppendOutHead(List<MmsstvTxToneSegment> segments, SstvModeProfile profile)
    {
        if (profile.Narrow)
        {
            segments.Add(new(FreqVisStart, 100.0));
            segments.Add(new(2300.0, 100.0));
            segments.Add(new(FreqVisStart, 100.0));
            segments.Add(new(2300.0, 100.0));
            return;
        }

        segments.Add(new(FreqVisStart, 100.0));
        segments.Add(new(FreqPorch, 100.0));
        segments.Add(new(FreqVisStart, 100.0));
        segments.Add(new(FreqPorch, 100.0));
        segments.Add(new(2300.0, 100.0));
        segments.Add(new(FreqPorch, 100.0));
        segments.Add(new(2300.0, 100.0));
        segments.Add(new(FreqPorch, 100.0));
    }

    private static void AppendMmsstvVisHeader(List<MmsstvTxToneSegment> segments, int visCode)
    {
        segments.Add(new(FreqVisStart, 300.0));
        segments.Add(new(FreqSync, 10.0));
        segments.Add(new(FreqVisStart, 300.0));
        segments.Add(new(FreqSync, 30.0));

        var fullVis = SourceVisByte(visCode);
        for (var bitIndex = 0; bitIndex < 8; bitIndex++)
        {
            var bit = (fullVis >> bitIndex) & 0x01;
            segments.Add(new(bit == 1 ? FreqVisOne : FreqVisZero, 30.0));
        }

        segments.Add(new(FreqSync, 30.0));
    }

    private static void AppendMmsstvFooter(List<MmsstvTxToneSegment> segments, MmsstvTxConfiguration tx, MmsstvTxOptions? options)
    {
        var tailMs = Math.Min(tx.TotalLineTimingMs, 500.0);
        if (tx.Profile.Narrow)
        {
            segments.Add(new(FreqVisStart, tailMs));
        }
        else
        {
            segments.Add(new(FreqPorch, tailMs));
            segments.Add(new(FreqVisStart, 100.0));
            segments.Add(new(FreqPorch, 100.0));
            segments.Add(new(FreqVisStart, 100.0));
            segments.Add(new(FreqPorch, 100.0));
        }

        if (options?.CwIdEnabled == true && !string.IsNullOrWhiteSpace(options.CwIdText))
        {
            AppendCwId(segments, options);
        }
    }

    private static void AppendCwId(List<MmsstvTxToneSegment> segments, MmsstvTxOptions options)
    {
        var dotMs = Math.Clamp(1110.0 / Math.Clamp(options.CwIdWpm, 5, 60), 18.5, 222.0);
        var frequency = Math.Clamp(options.CwIdFrequencyHz, 300, 3000);
        segments.Add(new(0.0, 250.0));
        foreach (var value in options.CwIdText ?? string.Empty)
        {
            AppendCwIdCharacter(segments, value, frequency, dotMs);
        }
    }

    private static void AppendCwIdCharacter(List<MmsstvTxToneSegment> segments, char value, int frequencyHz, double dotMs)
    {
        var encoded = ResolveMorsePattern(value);
        if (encoded < 0)
        {
            segments.Add(new(0.0, dotMs * 7.0));
            return;
        }

        if (encoded == int.MaxValue)
        {
            segments.Add(new(0.0, 250.0));
            return;
        }

        var count = encoded & 0x00ff;
        var pattern = encoded;
        for (var i = 0; i < count; i++)
        {
            segments.Add(new(frequencyHz, (pattern & 0x8000) != 0 ? dotMs : dotMs * 3.0));
            segments.Add(new(0.0, dotMs));
            pattern <<= 1;
        }

        segments.Add(new(0.0, dotMs * 2.0));
    }

    private static void AppendAvtControl(List<MmsstvTxToneSegment> segments, SstvModeProfile profile)
    {
        for (var i = 0; i < 3; i++)
        {
            AppendMmsstvVisHeader(segments, profile.VisCode);
        }

        var packet = 0x5FA0;
        for (var i = 0; i < 32; i++)
        {
            segments.Add(new(1900.0, 9.7646));
            var word = packet;
            for (var bit = 0; bit < 16; bit++)
            {
                segments.Add(new((word & 0x8000) != 0 ? 1600.0 : 2200.0, 9.7646));
                word <<= 1;
            }

            packet = ((packet & 0xff00) - 0x0100) | ((packet & 0x00ff) + 0x0001);
        }

        segments.Add(new(0.0, 0.30514375));
    }

    private static void AppendMartinLine(List<MmsstvTxToneSegment> segments, byte[] red, byte[] green, byte[] blue, SstvModeProfile profile)
    {
        segments.Add(new(FreqSync, profile.SyncMs));
        segments.Add(new(FreqPorch, profile.GapMs, 0x2000));
        AppendPixels(segments, green, profile.ScanMs, 0x2000);
        segments.Add(new(FreqPorch, profile.GapMs, 0x3000));
        AppendPixels(segments, blue, profile.ScanMs, 0x3000);
        segments.Add(new(FreqPorch, profile.GapMs, 0x1000));
        AppendPixels(segments, red, profile.ScanMs, 0x1000);
        segments.Add(new(FreqPorch, profile.GapMs));
    }

    private static void AppendScottieLine(List<MmsstvTxToneSegment> segments, byte[] red, byte[] green, byte[] blue, SstvModeProfile profile)
    {
        segments.Add(new(FreqPorch, profile.GapMs, 0x2000));
        AppendPixels(segments, green, profile.ScanMs, 0x2000);
        segments.Add(new(FreqPorch, profile.GapMs, 0x3000));
        AppendPixels(segments, blue, profile.ScanMs, 0x3000);
        segments.Add(new(FreqSync, profile.SyncMs));
        segments.Add(new(FreqPorch, profile.GapMs, 0x1000));
        AppendPixels(segments, red, profile.ScanMs, 0x1000);
    }

    private static void AppendRobot36Line(List<MmsstvTxToneSegment> segments, byte[] red, byte[] green, byte[] blue, int y, SstvModeProfile profile)
    {
        RgbToMmsstvYrybyRow(red, green, blue, out var yPixels, out var ryPixels, out var byPixels);
        segments.Add(new(FreqSync, profile.SyncMs));
        segments.Add(new(FreqPorch, profile.SyncPorchMs));
        AppendPixels(segments, yPixels, profile.ScanMs);
        segments.Add(new((y & 1) == 0 ? 1500.0 : 2300.0, profile.GapMs));
        segments.Add(new(FreqVisStart, profile.PorchMs));
        AppendPixels(segments, (y & 1) == 0 ? ryPixels : byPixels, profile.AuxScanMs);
    }

    private static void AppendRobotLine(List<MmsstvTxToneSegment> segments, byte[] red, byte[] green, byte[] blue, SstvModeProfile profile)
    {
        RgbToMmsstvYrybyRow(red, green, blue, out var yPixels, out var ryPixels, out var byPixels);
        var luminanceGapMs = profile.Id == SstvModeId.Robot24 ? 2.0 : 3.0;
        var colorMarkerMs = profile.Id == SstvModeId.Robot24 ? 3.0 : 4.5;
        var colorPorchMs = profile.Id == SstvModeId.Robot24 ? 1.0 : 1.5;
        segments.Add(new(FreqSync, profile.SyncMs));
        segments.Add(new(FreqPorch, luminanceGapMs));
        AppendPixels(segments, yPixels, profile.ScanMs);
        segments.Add(new(FreqPorch, colorMarkerMs));
        segments.Add(new(FreqVisStart, colorPorchMs));
        AppendPixels(segments, ryPixels, profile.AuxScanMs);
        segments.Add(new(2300.0, colorMarkerMs));
        segments.Add(new(FreqVisStart, colorPorchMs));
        AppendPixels(segments, byPixels, profile.AuxScanMs);
    }

    private static void AppendPdPair(
        List<MmsstvTxToneSegment> segments,
        byte[] y0,
        byte[] y1,
        byte[] by,
        byte[] ry,
        SstvModeProfile profile)
    {
        segments.Add(new(FreqSync, profile.SyncMs));
        segments.Add(new(FreqPorch, profile.PorchMs));
        var segmentMs = profile.PixelMs * profile.Width;
        AppendPixels(segments, y0, segmentMs);
        AppendPixels(segments, ry, segmentMs);
        AppendPixels(segments, by, segmentMs);
        AppendPixels(segments, y1, segmentMs);
    }

    private static void AppendAvtLine(List<MmsstvTxToneSegment> segments, byte[] red, byte[] green, byte[] blue, SstvModeProfile profile)
    {
        AppendPixels(segments, red, profile.ScanMs, 0x1000);
        AppendPixels(segments, green, profile.ScanMs, 0x2000);
        AppendPixels(segments, blue, profile.ScanMs, 0x3000);
    }

    private static void AppendPixels(List<MmsstvTxToneSegment> segments, byte[] pixels, double scanMs, int marker = 0)
    {
        var perPixelMs = scanMs / pixels.Length;
        for (var i = 0; i < pixels.Length; i++)
        {
            segments.Add(new(ColorToFrequencyHz(pixels[i]), perPixelMs, marker));
        }
    }

    private static double ColorToFrequencyHz(byte value)
        => 1500.0 + ((value * (2300 - 1500)) / 256);

    private static int SourceVisByte(int visCode)
    {
        if ((visCode & 0x80) != 0)
        {
            return visCode & 0xff;
        }

        var data = visCode & 0x7f;
        var ones = 0;
        for (var i = 0; i < 7; i++)
        {
            ones += (data >> i) & 0x01;
        }

        if ((ones & 0x01) != 0)
        {
            data |= 0x80;
        }

        return data;
    }

    private static int ResolveMorsePattern(char value)
    {
        ReadOnlySpan<ushort> morseTable =
        [
            0x0005, 0x8005, 0xc005, 0xe005, 0xf005, 0xf805, 0x7805, 0x3805,
            0x1805, 0x0805, 0x0000, 0x0000, 0x0000, 0x7005, 0xA805, 0xcc06,
            0x0000, 0x8002, 0x7004, 0x5004, 0x6003, 0x8001, 0xd004, 0x2003,
            0xf004, 0xc002, 0x8004, 0x4003, 0xb004, 0x0002, 0x4002, 0x0003,
            0x9004, 0x2004, 0xa003, 0xe003, 0x0001, 0xc003, 0xe004, 0x8003,
            0x6004, 0x4004, 0x3004, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
        ];

        var upper = char.ToUpperInvariant(value);
        upper &= (char)0x7f;
        if (upper == '.')
        {
            upper = 'R';
        }

        if (upper == '/')
        {
            return 0x6805;
        }

        if (upper == '@')
        {
            return int.MaxValue;
        }

        if (upper is >= '0' and <= 'Z')
        {
            return morseTable[upper - '0'];
        }

        return -1;
    }

    private static void ExtractRgbRow(byte[] rgb24, SstvModeProfile profile, int y, out byte[] red, out byte[] green, out byte[] blue)
    {
        var rowOffset = y * profile.Width * 3;
        red = new byte[profile.Width];
        green = new byte[profile.Width];
        blue = new byte[profile.Width];
        for (var x = 0; x < profile.Width; x++)
        {
            var src = rowOffset + (x * 3);
            red[x] = rgb24[src];
            green[x] = rgb24[src + 1];
            blue[x] = rgb24[src + 2];
        }
    }

    private static void ExtractTxRgbRow(byte[] rgb24, SstvModeProfile profile, int y, out byte[] red, out byte[] green, out byte[] blue)
    {
        var sourceY = profile.Id == SstvModeId.Robot24
            ? Math.Clamp(y * 2, 0, profile.Height - 1)
            : y;
        ExtractRgbRow(rgb24, profile, sourceY, out red, out green, out blue);
    }

    private static void RgbToMmsstvYrybyRow(
        byte[] red,
        byte[] green,
        byte[] blue,
        out byte[] y,
        out byte[] ry,
        out byte[] by)
    {
        y = new byte[red.Length];
        ry = new byte[red.Length];
        by = new byte[red.Length];

        for (var x = 0; x < red.Length; x++)
        {
            var r = red[x];
            var g = green[x];
            var b = blue[x];
            y[x] = ClampByte(16.0 + (0.256773 * r) + (0.504097 * g) + (0.097900 * b));
            ry[x] = ClampByte(128.0 + (0.439187 * r) - (0.367766 * g) - (0.071421 * b));
            by[x] = ClampByte(128.0 + (-0.148213 * r) - (0.290974 * g) + (0.439187 * b));
        }
    }

    private static void RgbToMmsstvYrybyRows(
        byte[] red0,
        byte[] green0,
        byte[] blue0,
        byte[] red1,
        byte[] green1,
        byte[] blue1,
        out byte[] y0,
        out byte[] y1,
        out byte[] by,
        out byte[] ry)
    {
        RgbToMmsstvYrybyRow(red0, green0, blue0, out y0, out ry, out by);
        RgbToMmsstvYrybyRow(red1, green1, blue1, out y1, out _, out _);
    }

    private static byte ClampByte(double value)
        => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
