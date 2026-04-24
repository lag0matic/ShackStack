namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested landing zone for MMSSTV's CHILL Hilbert demod path.
/// This preserves the source-shaped control fields and processing flow so we
/// can later compare or substitute it against the current CFQC path.
/// </summary>
internal sealed class MmsstvHilbertDemodulator
{
    private readonly double[] _phaseHistory = new double[4];
    private readonly MmsstvIirFilter _outputFilter = new();
    private double[] _hilbertTaps = [];
    private double[] _firDelay = [];
    private double _previousOut;

    public double Offset { get; private set; }
    public double OutputScale { get; private set; }
    public int HalfTap { get; private set; }
    public int DelayFactor { get; private set; }
    public int TapCount { get; private set; }

    public MmsstvHilbertDemodulator(double sampleRate, bool narrow = false)
    {
        SetWidth(sampleRate, narrow);
    }

    public void SetWidth(double sampleRate, bool narrow)
    {
        if (narrow)
        {
            Offset = (2.0 * Math.PI * 2172.0) / sampleRate;
            OutputScale = 32768.0 * sampleRate / (2.0 * Math.PI * 256.0);
        }
        else
        {
            Offset = (2.0 * Math.PI * 1900.0) / sampleRate;
            OutputScale = 32768.0 * sampleRate / (2.0 * Math.PI * 800.0);
        }

        if (sampleRate >= 40000.0)
        {
            Offset *= 4.0;
            OutputScale *= 0.25;
            TapCount = 48;
            DelayFactor = 2;
        }
        else if (sampleRate >= 16000.0)
        {
            Offset *= 2.0;
            OutputScale *= 0.5;
            TapCount = 24;
            DelayFactor = 1;
        }
        else
        {
            TapCount = 12;
            DelayFactor = 0;
        }

        HalfTap = TapCount / 2;
        _hilbertTaps = BuildHilbertTaps(TapCount, sampleRate);
        _firDelay = new double[TapCount + 1];
        _outputFilter.MakeIir(1800.0, sampleRate, 3, 0, 0.0);
        _outputFilter.Clear();
        Array.Clear(_firDelay);
        Array.Clear(_phaseHistory);
        _previousOut = 0.0;
    }

    public double Process(double sample)
    {
        var quadrature = DoSourceFir(sample);
        var center = _firDelay[HalfTap];
        var angle = center != 0.0 ? Math.Atan2(quadrature, center) : 0.0;
        var delta = angle - _phaseHistory[0];

        switch (DelayFactor)
        {
            case 1:
                _phaseHistory[0] = _phaseHistory[1];
                _phaseHistory[1] = angle;
                break;
            case 2:
                _phaseHistory[0] = _phaseHistory[1];
                _phaseHistory[1] = _phaseHistory[2];
                _phaseHistory[2] = _phaseHistory[3];
                _phaseHistory[3] = angle;
                break;
            default:
                _phaseHistory[0] = angle;
                break;
        }

        if (delta >= Math.PI)
        {
            delta -= Math.PI * 2.0;
        }
        else if (delta <= -Math.PI)
        {
            delta += Math.PI * 2.0;
        }

        delta += Offset;
        _previousOut = _outputFilter.Process(delta * OutputScale);
        return _previousOut;
    }

    private double DoSourceFir(double sample)
    {
        // MMSSTV builds CHILL with HILLDOUBLEBUF FALSE, so CHILL::Do uses the
        // simple DoFIR(H, Z, d, m_tap) shift register and reads Z[m_htap].
        Array.Copy(_firDelay, 1, _firDelay, 0, TapCount);
        _firDelay[TapCount] = sample;

        var output = 0.0;
        for (var i = 0; i <= TapCount; i++)
        {
            output += _firDelay[i] * _hilbertTaps[i];
        }

        return output;
    }

    private static double[] BuildHilbertTaps(int tapCount, double sampleRate)
    {
        var taps = new double[tapCount + 1];
        var center = tapCount / 2;
        const double lowCutHz = 100.0;
        var highCutHz = (sampleRate / 2.0) - 100.0;
        var samplePeriod = 1.0 / sampleRate;
        var w1 = 2.0 * Math.PI * lowCutHz;
        var w2 = 2.0 * Math.PI * highCutHz;

        for (var n = 0; n <= tapCount; n++)
        {
            double x1;
            double x2;
            if (n == center)
            {
                x1 = 0.0;
                x2 = 0.0;
            }
            else
            {
                var offset = n - center;
                x1 = (offset * w1 * samplePeriod);
                x1 = Math.Cos(x1) / x1;
                x2 = (offset * w2 * samplePeriod);
                x2 = Math.Cos(x2) / x2;
            }

            var window = 0.54 - (0.46 * Math.Cos(2.0 * Math.PI * n / tapCount));
            taps[n] = -((2.0 * highCutHz * samplePeriod * x2) - (2.0 * lowCutHz * samplePeriod * x1)) * window;
        }

        return taps;
    }
}
