namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Source-shaped landing zone for CSSTVMOD.
/// This now owns a ring buffer, write/read counters, and sample-by-sample
/// output so later TX transport work can consume it like the original modulator.
/// </summary>
internal sealed class MmsstvTxModulator
{
    private const double BlackFrequencyHz = 1100.0;
    private const double WhiteFrequencyHz = 2300.0;
    private const int DefaultIdleFrequencyHz = 1500;
    private const int DefaultFskSpaceHz = 2100;
    private const double DefaultFskIntervalMs = 22.0;
    private readonly MmsstvVco _vco;
    private readonly MmsstvSmoother _avgLpf = new();
    private int[] _txBuffer = [];

    public MmsstvTxModulator(int sampleRate)
    {
        SampleRate = sampleRate;
        _vco = new MmsstvVco(sampleRate);
        _vco.SetFreeFrequency(BlackFrequencyHz);
        _vco.SetGain(WhiteFrequencyHz - BlackFrequencyHz);
        _avgLpf.SetCount(1.0);
        _avgLpf.SetData(0.0);
        OutputGain = 0.82;
        VariGainRed = 1.0;
        VariGainGreen = 1.0;
        VariGainBlue = 1.0;
        OpenTxBuffer(8);
        InitTxBuffer();
    }

    public int SampleRate { get; }
    public int WritePoint { get; private set; }
    public int ReadPoint { get; private set; }
    public int BufferedCount { get; private set; }
    public int ReadCount { get; private set; }
    public int IntegerPosition { get; private set; }
    public double FractionalPosition { get; private set; }
    public int SavedCount { get; set; }
    public int BufferSeconds { get; private set; }
    public int BufferMaxSamples { get; private set; }
    public int TuneFrequencyHz { get; set; } = 1750;
    public bool TuneEnabled { get; set; }
    public bool UseLowPass { get; set; }
    public bool VariColorOutput { get; set; }
    public double OutputGain { get; set; }
    public double VariGainRed { get; private set; }
    public double VariGainGreen { get; private set; }
    public double VariGainBlue { get; private set; }
    public int IdleFrequencyHz { get; set; } = DefaultIdleFrequencyHz;

    public void OpenTxBuffer(int seconds)
    {
        var bounded = Math.Max(5, seconds);
        var maxSamples = bounded * SampleRate;
        if (_txBuffer.Length == maxSamples)
        {
            BufferSeconds = bounded;
            BufferMaxSamples = maxSamples;
            return;
        }

        _txBuffer = new int[maxSamples];
        BufferSeconds = bounded;
        BufferMaxSamples = maxSamples;
        InitTxBuffer();
    }

    public void InitTxBuffer()
    {
        WritePoint = 0;
        ReadPoint = 0;
        BufferedCount = 0;
        ReadCount = 0;
        IntegerPosition = 0;
        FractionalPosition = 0.0;
    }

    public void InitGain(int variRed = 1000, int variGreen = 1000, int variBlue = 1000)
    {
        VariGainRed = Math.Clamp(variRed, 0, 1000) * 0.001;
        VariGainGreen = Math.Clamp(variGreen, 0, 1000) * 0.001;
        VariGainBlue = Math.Clamp(variBlue, 0, 1000) * 0.001;
    }

    public void QueueSegments(IReadOnlyList<MmsstvTxToneSegment> segments, MmsstvTxConfiguration tx)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.DurationMs > 0.0)
            {
                Write((short)Math.Round(segment.FrequencyHz), segment.DurationMs, tx, segment.Marker);
            }
        }
    }

    public void Write(short frequencyHz)
    {
        if (_txBuffer.Length == 0)
        {
            OpenTxBuffer(8);
        }

        _txBuffer[WritePoint] = frequencyHz;
        WritePoint++;
        BufferedCount++;
        if (WritePoint >= BufferMaxSamples)
        {
            WritePoint = 0;
        }
    }

    public void Write(short frequencyHz, double durationMs, MmsstvTxConfiguration tx, int marker = 0)
    {
        FractionalPosition += (durationMs * tx.TxSampleFrequency) / 1000.0;
        for (; IntegerPosition < (int)FractionalPosition; IntegerPosition++)
        {
            Write(EncodeFrequency(frequencyHz, marker));
        }
    }

    public void WriteCount(short frequencyHz, double sampleCount, int marker = 0)
    {
        FractionalPosition += sampleCount;
        for (; IntegerPosition < (int)FractionalPosition; IntegerPosition++)
        {
            Write(EncodeFrequency(frequencyHz, marker));
        }
    }

    public void WriteFsk(byte value, double fskIntervalMs = DefaultFskIntervalMs, int markFrequencyHz = 1900, int spaceFrequencyHz = DefaultFskSpaceHz)
    {
        var tx = MmsstvTxConfiguration.Create(MmsstvModeCatalog.Profiles.First(), SampleRate);
        for (var i = 0; i < 6; i++)
        {
            var tone = (value & 0x01) != 0 ? markFrequencyHz : spaceFrequencyHz;
            Write((short)tone, fskIntervalMs, tx);
            value >>= 1;
        }
    }

    public void WriteCwId(char value, int cwIdFrequencyHz = 800, int cwIdSpeedMs = 30)
    {
        var dot = cwIdSpeedMs + 30;
        var encoded = ResolveMorsePattern(value);
        if (encoded < 0)
        {
            WriteSilence(dot * 7);
            return;
        }

        if (encoded == int.MaxValue)
        {
            WriteSilence(250);
            return;
        }

        var count = encoded & 0x00ff;
        var pattern = encoded;
        var tx = MmsstvTxConfiguration.Create(MmsstvModeCatalog.Profiles.First(), SampleRate);
        for (var i = 0; i < count; i++)
        {
            var duration = (pattern & 0x8000) != 0 ? dot : dot * 3;
            Write((short)cwIdFrequencyHz, duration, tx);
            WriteSilence(dot);
            pattern <<= 1;
        }

        WriteSilence(dot * 2);
    }

    public double Do()
    {
        if (TuneEnabled)
        {
            return _vco.Process((TuneFrequencyHz - BlackFrequencyHz) / (WhiteFrequencyHz - BlackFrequencyHz)) * OutputGain;
        }

        if (BufferedCount <= 0)
        {
            var idleControl = Math.Clamp((IdleFrequencyHz - BlackFrequencyHz) / (WhiteFrequencyHz - BlackFrequencyHz), 0.0, 1.0);
            return _vco.Process(idleControl) * OutputGain;
        }

        var encoded = _txBuffer[ReadPoint];
        double value;
        if ((encoded & 0x0fff) > 0)
        {
            var frequency = (encoded & 0x0fff);
            var control = Math.Clamp((frequency - BlackFrequencyHz) / (WhiteFrequencyHz - BlackFrequencyHz), 0.0, 1.0);
            if (UseLowPass)
            {
                control = _avgLpf.Average(control);
            }

            value = _vco.Process(control);
            value *= ResolveGain(encoded);
        }
        else
        {
            value = 0.0;
        }

        BufferedCount--;
        ReadCount++;
        ReadPoint++;
        if (ReadPoint >= BufferMaxSamples)
        {
            ReadPoint = 0;
        }

        return value;
    }

    public float[] RenderQueuedPcm(IReadOnlyList<MmsstvTxToneSegment> segments, MmsstvTxConfiguration tx)
    {
        EnsureCapacityFor(segments, tx);
        InitTxBuffer();
        QueueSegments(segments, tx);
        SavedCount = BufferedCount;
        var output = new float[BufferedCount];
        for (var i = 0; i < output.Length; i++)
        {
            output[i] = (float)Do();
        }

        return output;
    }

    public float[] RenderPcm(IReadOnlyList<MmsstvTxToneSegment> segments, double amplitude = 0.82, bool useLpf = false)
    {
        OutputGain = amplitude;
        UseLowPass = useLpf;
        var tx = MmsstvTxConfiguration.Create(MmsstvModeCatalog.Profiles.First(), SampleRate);
        return RenderQueuedPcm(segments, tx);
    }

    private double ResolveGain(int encoded)
    {
        if (!VariColorOutput)
        {
            return OutputGain;
        }

        return (encoded & 0xf000) switch
        {
            0x1000 => OutputGain * VariGainRed,
            0x2000 => OutputGain * VariGainGreen,
            0x3000 => OutputGain * VariGainBlue,
            _ => OutputGain,
        };
    }

    private static short EncodeFrequency(short frequencyHz, int marker)
    {
        var encodedMarker = marker & unchecked((short)0xf000);
        return (short)(encodedMarker | (frequencyHz & 0x0fff));
    }

    private void EnsureCapacityFor(IReadOnlyList<MmsstvTxToneSegment> segments, MmsstvTxConfiguration tx)
    {
        double totalSamples = 0.0;
        for (var i = 0; i < segments.Count; i++)
        {
            totalSamples += (segments[i].DurationMs * tx.TxSampleFrequency) / 1000.0;
        }

        var requiredSamples = Math.Max(1, (int)Math.Ceiling(totalSamples) + 1);
        if (requiredSamples <= BufferMaxSamples)
        {
            return;
        }

        var requiredSeconds = Math.Max(5, (int)Math.Ceiling(requiredSamples / (double)SampleRate));
        OpenTxBuffer(requiredSeconds);
    }

    private void WriteSilence(double durationMs)
    {
        var tx = MmsstvTxConfiguration.Create(MmsstvModeCatalog.Profiles.First(), SampleRate);
        Write(0, durationMs, tx);
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
}
