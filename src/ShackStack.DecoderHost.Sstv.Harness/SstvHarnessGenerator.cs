using ShackStack.DecoderHost.Sstv.Core;

namespace ShackStack.DecoderHost.Sstv.Harness;

internal static class SstvHarnessGenerator
{
    private const double FreqBlack = 1500.0;
    private const double FreqWhite = 2300.0;
    private const double FreqSync = 1200.0;
    private const double FreqVisStart = 1900.0;
    private const double FreqVisZero = 1300.0;
    private const double FreqVisOne = 1100.0;
    private const double FreqPorch = 1500.0;

    public static float[] GenerateAudio(byte[] rgb24, SstvModeProfile profile, int sampleRate)
    {
        if (!profile.Family.Equals("martin", StringComparison.OrdinalIgnoreCase)
            && !profile.Family.Equals("scottie", StringComparison.OrdinalIgnoreCase)
            && !profile.Family.Equals("robot36", StringComparison.OrdinalIgnoreCase)
            && !profile.Family.Equals("pd", StringComparison.OrdinalIgnoreCase)
            && !profile.Family.Equals("avt", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Harness currently generates common MMSSTV color families only.");
        }

        if (!string.Equals(Environment.GetEnvironmentVariable("SHACKSTACK_HARNESS_USE_NATIVE_TX"), "0", StringComparison.Ordinal))
        {
            var tx = MmsstvTxConfiguration.Create(profile, sampleRate);
            var tonePlan = MmsstvTxSequenceBuilder.BuildImageTones(rgb24, tx);
            var modulator = new MmsstvTxModulator(sampleRate);
            var payload = modulator.RenderQueuedPcm(tonePlan, tx);

            var nativeSamples = new List<float>(sampleRate * 120);
            AppendSilence(nativeSamples, sampleRate, 0.15);
            nativeSamples.AddRange(payload);
            AppendSilence(nativeSamples, sampleRate, 0.25);
            return [.. nativeSamples];
        }

        _phase = 0.0;
        var samples = new List<float>(sampleRate * 120);
        AppendSilence(samples, sampleRate, 0.15);
        if (profile.Family.Equals("avt", StringComparison.OrdinalIgnoreCase))
        {
            AppendAvtControl(samples, profile, sampleRate);
        }
        else
        {
            AppendMmsstvVisHeader(samples, profile.VisCode, sampleRate);
        }

        if (profile.Family.Equals("avt", StringComparison.OrdinalIgnoreCase))
        {
            for (var y = 0; y < profile.Height; y++)
            {
                ExtractRgbRow(rgb24, profile, y, out var red, out var green, out var blue);
                AppendAvtLine(samples, red, green, blue, profile, sampleRate);
            }
        }
        else if (profile.Family.Equals("pd", StringComparison.OrdinalIgnoreCase))
        {
            for (var y = 0; y < profile.Height - 1; y += 2)
            {
                ExtractRgbRow(rgb24, profile, y, out var red0, out var green0, out var blue0);
                ExtractRgbRow(rgb24, profile, y + 1, out var red1, out var green1, out var blue1);
                RgbToMmsstvYrybyRows(red0, green0, blue0, red1, green1, blue1, out var y0, out var y1, out var by, out var ry);
                AppendPdPair(samples, y0, y1, by, ry, profile, sampleRate);
            }
        }
        else
        {
            for (var y = 0; y < profile.Height; y++)
            {
                ExtractRgbRow(rgb24, profile, y, out var red, out var green, out var blue);

                if (profile.Family.Equals("martin", StringComparison.OrdinalIgnoreCase))
                {
                    AppendMartinLine(samples, red, green, blue, profile, sampleRate);
                }
                else if (profile.Family.Equals("scottie", StringComparison.OrdinalIgnoreCase))
                {
                    AppendScottieLine(samples, red, green, blue, profile, sampleRate);
                }
                else
                {
                    AppendRobot36Line(samples, red, green, blue, y, profile, sampleRate);
                }
            }
        }

        AppendSilence(samples, sampleRate, 0.25);
        return [.. samples];
    }

    public static float[] GenerateAvtControlAudio(SstvModeProfile profile, int sampleRate)
    {
        _phase = 0.0;
        var samples = new List<float>(sampleRate * 8);
        AppendSilence(samples, sampleRate, 0.15);
        AppendAvtControl(samples, profile, sampleRate);
        AppendSilence(samples, sampleRate, 0.15);
        return [.. samples];
    }

    public static float[] GenerateVisOnlyAudio(SstvModeProfile profile, int sampleRate)
    {
        _phase = 0.0;
        var samples = new List<float>(sampleRate * 2);
        AppendSilence(samples, sampleRate, 0.15);
        AppendMmsstvVisHeader(samples, profile.VisCode, sampleRate);
        AppendSilence(samples, sampleRate, 0.15);
        return [.. samples];
    }

    private static void AppendAvtControl(List<float> samples, SstvModeProfile profile, int sampleRate)
    {
        // MMSSTV transmits the standard VIS burst three times for AVT.
        for (var i = 0; i < 3; i++)
        {
            AppendMmsstvVisHeader(samples, profile.VisCode, sampleRate);
        }

        var packet = 0x5FA0;
        for (var i = 0; i < 32; i++)
        {
            AppendTone(samples, 1900.0, 9.7646, sampleRate);
            var word = packet;
            for (var bit = 0; bit < 16; bit++)
            {
                AppendTone(samples, (word & 0x8000) != 0 ? 1600.0 : 2200.0, 9.7646, sampleRate);
                word <<= 1;
            }

            packet = ((packet & 0xff00) - 0x0100) | ((packet & 0x00ff) + 0x0001);
        }

        AppendTone(samples, 0.0, 0.30514375, sampleRate);
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

    private static void AppendMmsstvVisHeader(List<float> samples, int visCode, int sampleRate)
    {
        // Source-faithful to MMSSTV's non-narrow VIS path in Main.cpp.
        AppendTone(samples, FreqVisStart, 300.0, sampleRate);
        AppendTone(samples, FreqSync, 10.0, sampleRate);
        AppendTone(samples, FreqVisStart, 300.0, sampleRate);
        AppendTone(samples, FreqSync, 30.0, sampleRate);

        var fullVis = AddEvenParity(visCode);
        for (var bitIndex = 0; bitIndex < 8; bitIndex++)
        {
            var bit = (fullVis >> bitIndex) & 0x01;
            AppendTone(samples, bit == 1 ? FreqVisOne : FreqVisZero, 30.0, sampleRate);
        }

        AppendTone(samples, FreqSync, 30.0, sampleRate);
    }

    private static int AddEvenParity(int visCode)
    {
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

    private static void AppendMartinLine(List<float> samples, byte[] red, byte[] green, byte[] blue, SstvModeProfile profile, int sampleRate)
    {
        AppendTone(samples, FreqSync, profile.SyncMs, sampleRate);
        AppendTone(samples, FreqPorch, profile.GapMs, sampleRate);
        AppendPixels(samples, green, profile.ScanMs, sampleRate);
        AppendTone(samples, FreqPorch, profile.GapMs, sampleRate);
        AppendPixels(samples, blue, profile.ScanMs, sampleRate);
        AppendTone(samples, FreqPorch, profile.GapMs, sampleRate);
        AppendPixels(samples, red, profile.ScanMs, sampleRate);
        AppendTone(samples, FreqPorch, profile.GapMs, sampleRate);
    }

    private static void AppendScottieLine(List<float> samples, byte[] red, byte[] green, byte[] blue, SstvModeProfile profile, int sampleRate)
    {
        AppendTone(samples, FreqPorch, profile.GapMs, sampleRate);
        AppendPixels(samples, green, profile.ScanMs, sampleRate);
        AppendTone(samples, FreqPorch, profile.GapMs, sampleRate);
        AppendPixels(samples, blue, profile.ScanMs, sampleRate);
        AppendTone(samples, FreqSync, profile.SyncMs, sampleRate);
        AppendTone(samples, FreqPorch, profile.GapMs, sampleRate);
        AppendPixels(samples, red, profile.ScanMs, sampleRate);
    }

    private static void AppendRobot36Line(List<float> samples, byte[] red, byte[] green, byte[] blue, int y, SstvModeProfile profile, int sampleRate)
    {
        RgbToMmsstvYrybyRow(red, green, blue, out var yPixels, out var ryPixels, out var byPixels);
        AppendTone(samples, FreqSync, profile.SyncMs, sampleRate);
        AppendTone(samples, FreqPorch, profile.SyncPorchMs, sampleRate);
        AppendPixels(samples, yPixels, profile.ScanMs, sampleRate);
        AppendTone(samples, (y & 1) == 0 ? 1500.0 : 2300.0, profile.GapMs, sampleRate);
        AppendTone(samples, FreqVisStart, profile.PorchMs, sampleRate);
        AppendPixels(samples, (y & 1) == 0 ? ryPixels : byPixels, profile.AuxScanMs, sampleRate);
    }

    private static void AppendPdPair(
        List<float> samples,
        byte[] y0,
        byte[] y1,
        byte[] by,
        byte[] ry,
        SstvModeProfile profile,
        int sampleRate)
    {
        AppendTone(samples, FreqSync, profile.SyncMs, sampleRate);
        AppendTone(samples, FreqPorch, profile.PorchMs, sampleRate);
        var segmentMs = profile.PixelMs * profile.Width;
        AppendPixels(samples, y0, segmentMs, sampleRate);
        AppendPixels(samples, ry, segmentMs, sampleRate);
        AppendPixels(samples, by, segmentMs, sampleRate);
        AppendPixels(samples, y1, segmentMs, sampleRate);
    }

    private static void AppendAvtLine(List<float> samples, byte[] red, byte[] green, byte[] blue, SstvModeProfile profile, int sampleRate)
    {
        AppendPixels(samples, red, profile.ScanMs, sampleRate);
        AppendPixels(samples, green, profile.ScanMs, sampleRate);
        AppendPixels(samples, blue, profile.ScanMs, sampleRate);
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
        y0 = new byte[red0.Length];
        y1 = new byte[red0.Length];
        by = new byte[red0.Length];
        ry = new byte[red0.Length];

        for (var x = 0; x < red0.Length; x++)
        {
            var r0 = red0[x];
            var g0 = green0[x];
            var b0 = blue0[x];
            var r1 = red1[x];
            var g1 = green1[x];
            var b1 = blue1[x];
            y0[x] = ClampByte(16.0 + (0.256773 * r0) + (0.504097 * g0) + (0.097900 * b0));
            y1[x] = ClampByte(16.0 + (0.256773 * r1) + (0.504097 * g1) + (0.097900 * b1));

            ry[x] = ClampByte(128.0 + (0.439187 * r0) - (0.367766 * g0) - (0.071421 * b0));
            by[x] = ClampByte(128.0 + (-0.148213 * r0) - (0.290974 * g0) + (0.439187 * b0));
        }
    }

    private static byte ClampByte(double value)
        => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static void AppendPixels(List<float> samples, byte[] channel, double durationMs, int sampleRate)
    {
        var totalSamples = Math.Max(1, (int)Math.Round(durationMs * sampleRate / 1000.0));
        for (var sampleIndex = 0; sampleIndex < totalSamples; sampleIndex++)
        {
            var position = sampleIndex * (channel.Length - 1) / (double)Math.Max(1, totalSamples - 1);
            var left = (int)Math.Floor(position);
            var right = Math.Min(channel.Length - 1, left + 1);
            var fraction = position - left;
            var luma = channel[left] + ((channel[right] - channel[left]) * fraction);
            var freq = FreqBlack + ((luma / 255.0) * (FreqWhite - FreqBlack));
            AppendSample(samples, freq, sampleRate);
        }
    }

    private static void AppendTone(List<float> samples, double freqHz, double durationMs, int sampleRate)
    {
        var count = Math.Max(1, (int)Math.Round(durationMs * sampleRate / 1000.0));
        for (var i = 0; i < count; i++)
        {
            AppendSample(samples, freqHz, sampleRate);
        }
    }

    private static void AppendSilence(List<float> samples, int sampleRate, double seconds)
    {
        var count = Math.Max(1, (int)Math.Round(seconds * sampleRate));
        for (var i = 0; i < count; i++)
        {
            samples.Add(0.0f);
        }
    }

    private static double _phase;

    public static string ProbeMmsstvVis(float[] audio, int sampleRate)
    {
        var offset = (int)Math.Round(0.15 * sampleRate);
        var frames = new (string Label, int DurationMs, double ExpectedFreq)[]
        {
            ("lead1", 300, FreqVisStart),
            ("break1", 10, FreqSync),
            ("lead2", 300, FreqVisStart),
            ("break2", 30, FreqSync),
            ("bit0", 30, FreqVisZero),
            ("bit1", 30, FreqVisZero),
            ("bit2", 30, FreqVisOne),
            ("bit3", 30, FreqVisOne),
            ("bit4", 30, FreqVisZero),
            ("bit5", 30, FreqVisOne),
            ("bit6", 30, FreqVisZero),
            ("bit7", 30, FreqVisOne),
            ("stop", 30, FreqSync),
        };

        var parts = new List<string>();
        foreach (var frame in frames)
        {
            var count = Math.Min(audio.Length - offset, (int)Math.Round(frame.DurationMs * sampleRate / 1000.0));
            if (count <= 0)
            {
                parts.Add($"{frame.Label}=eof");
                break;
            }

            var block = new ReadOnlySpan<float>(audio, offset, count);
            var expected = SstvAudioMath.TonePower(block, sampleRate, frame.ExpectedFreq);
            var alt1100 = SstvAudioMath.TonePower(block, sampleRate, 1100.0);
            var alt1200 = SstvAudioMath.TonePower(block, sampleRate, 1200.0);
            var alt1300 = SstvAudioMath.TonePower(block, sampleRate, 1300.0);
            var alt1900 = SstvAudioMath.TonePower(block, sampleRate, 1900.0);
            var total = alt1100 + alt1200 + alt1300 + alt1900;
            var ratio = total > 0.0 ? expected / total : 0.0;
            parts.Add($"{frame.Label}:{frame.ExpectedFreq:0}/{ratio:0.00}");
            offset += count;
        }

        return string.Join(" ", parts);
    }

    private static void AppendSample(List<float> samples, double freqHz, int sampleRate)
    {
        _phase += (2.0 * Math.PI * freqHz) / sampleRate;
        if (_phase > Math.PI * 2.0)
        {
            _phase -= Math.PI * 2.0;
        }

        samples.Add((float)(Math.Sin(_phase) * 0.85));
    }
}
