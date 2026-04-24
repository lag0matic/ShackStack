namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Source-shaped landing zone for the retuned sync tone tank filters
/// (`m_iir11`, `m_iir12`, `m_iir13`, `m_iir19`, `m_iirfsk`).
/// This currently feeds the receiver control path before the full MMSSTV
/// low-pass and state-flow around these filters is ported.
/// </summary>
internal sealed class MmsstvSyncFilterBank
{
    private readonly MmsstvIirTank _tone1080;
    private readonly MmsstvIirTank _tone1200;
    private readonly MmsstvIirTank _tone1320;
    private readonly MmsstvIirTank _tone1900;
    private readonly MmsstvIirTank _toneFsk;
    private readonly MmsstvIirFilter _lpf1080;
    private readonly MmsstvIirFilter _lpf1200;
    private readonly MmsstvIirFilter _lpf1320;
    private readonly MmsstvIirFilter _lpf1900;
    private readonly MmsstvIirFilter _lpfFsk;
    private readonly double _sampleRate;
    private int _lastOffsetHz = int.MinValue;
    private double _lastToneOffsetHz = double.NaN;

    public MmsstvSyncFilterBank(double sampleRate)
    {
        _sampleRate = sampleRate;
        _tone1080 = new(sampleRate);
        _tone1200 = new(sampleRate);
        _tone1320 = new(sampleRate);
        _tone1900 = new(sampleRate);
        _toneFsk = new(sampleRate);
        _lpf1080 = CreateToneLpf(sampleRate);
        _lpf1200 = CreateToneLpf(sampleRate);
        _lpf1320 = CreateToneLpf(sampleRate);
        _lpf1900 = CreateToneLpf(sampleRate);
        _lpfFsk = CreateToneLpf(sampleRate);
    }

    public void Retune(MmsstvSyncToneBank toneBank)
    {
        if (_lastOffsetHz == toneBank.AfcFrequencyOffsetHz && _lastToneOffsetHz.Equals(toneBank.ToneOffsetHz))
        {
            return;
        }

        _tone1080.SetFrequency(toneBank.Tone1080Hz, _sampleRate, 80.0);
        _tone1200.SetFrequency(toneBank.Tone1200Hz, _sampleRate, 100.0);
        _tone1320.SetFrequency(toneBank.Tone1320Hz, _sampleRate, 80.0);
        _tone1900.SetFrequency(toneBank.Tone1900Hz, _sampleRate, 100.0);
        _toneFsk.SetFrequency(toneBank.ToneFskHz, _sampleRate, 100.0);
        _lastOffsetHz = toneBank.AfcFrequencyOffsetHz;
        _lastToneOffsetHz = toneBank.ToneOffsetHz;
    }

    public SyncFilterSnapshot Measure(ReadOnlySpan<float> samples)
    {
        var p1080 = 0.0;
        var p1200 = 0.0;
        var p1320 = 0.0;
        var p1900 = 0.0;
        var pfsk = 0.0;

        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i] * 16384.0;
            p1080 += Square(_lpf1080.Process(_tone1080.Process(sample)));
            p1200 += Square(_lpf1200.Process(_tone1200.Process(sample)));
            p1320 += Square(_lpf1320.Process(_tone1320.Process(sample)));
            p1900 += Square(_lpf1900.Process(_tone1900.Process(sample)));
            pfsk += Square(_lpfFsk.Process(_toneFsk.Process(sample)));
        }

        var scale = samples.Length > 0 ? 1.0 / samples.Length : 0.0;
        return new SyncFilterSnapshot(
            p1080 * scale,
            p1200 * scale,
            p1320 * scale,
            p1900 * scale,
            pfsk * scale);
    }

    public SyncFilterSnapshot ProcessSample(float sample)
    {
        var scaled = sample * 16384.0;
        return new SyncFilterSnapshot(
            _lpf1080.Process(Math.Abs(_tone1080.Process(scaled))),
            _lpf1200.Process(Math.Abs(_tone1200.Process(scaled))),
            _lpf1320.Process(Math.Abs(_tone1320.Process(scaled))),
            _lpf1900.Process(Math.Abs(_tone1900.Process(scaled))),
            _lpfFsk.Process(Math.Abs(_toneFsk.Process(scaled))));
    }

    public SyncFilterSnapshot ProcessScaledEnvelope(double scaledSample)
    {
        var tone1080 = _lpf1080.Process(Math.Abs(_tone1080.Process(scaledSample)));
        var tone1200 = _lpf1200.Process(Math.Abs(_tone1200.Process(scaledSample)));
        var tone1320 = _lpf1320.Process(Math.Abs(_tone1320.Process(scaledSample)));
        var tone1900 = _lpf1900.Process(Math.Abs(_tone1900.Process(scaledSample)));
        var toneFsk = _lpfFsk.Process(Math.Abs(_toneFsk.Process(scaledSample)));
        return new SyncFilterSnapshot(tone1080, tone1200, tone1320, tone1900, toneFsk);
    }

    public void Clear()
    {
        _tone1080.Clear();
        _tone1200.Clear();
        _tone1320.Clear();
        _tone1900.Clear();
        _toneFsk.Clear();
        _lpf1080.Clear();
        _lpf1200.Clear();
        _lpf1320.Clear();
        _lpf1900.Clear();
        _lpfFsk.Clear();
        _lastOffsetHz = int.MinValue;
        _lastToneOffsetHz = double.NaN;
    }

    private static double Square(double value) => value * value;

    private static MmsstvIirFilter CreateToneLpf(double sampleRate)
    {
        var filter = new MmsstvIirFilter();
        filter.MakeIir(50.0, sampleRate, 2, 0, 0.0);
        return filter;
    }

    internal readonly record struct SyncFilterSnapshot(
        double Tone1080,
        double Tone1200,
        double Tone1320,
        double Tone1900,
        double ToneFsk);
}
