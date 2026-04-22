namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested landing zone for MMSSTV's CHILL Hilbert demod path.
/// This preserves the source-shaped control fields and processing flow so we
/// can later compare or substitute it against the current CFQC path.
/// </summary>
internal sealed class MmsstvHilbertDemodulator
{
    private readonly Queue<double> _delayLine = new();
    private readonly double[] _phaseHistory = new double[4];
    private double[] _hilbertTaps = [];
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
        _hilbertTaps = BuildHilbertTaps(TapCount);
        _delayLine.Clear();
        Array.Clear(_phaseHistory);
        _previousOut = 0.0;
    }

    public double Process(double sample)
    {
        _delayLine.Enqueue(sample);
        while (_delayLine.Count > TapCount + 1)
        {
            _delayLine.Dequeue();
        }

        if (_delayLine.Count < TapCount + 1)
        {
            return _previousOut;
        }

        var values = _delayLine.ToArray();
        var quadrature = 0.0;
        for (var i = 0; i <= TapCount; i++)
        {
            quadrature += values[TapCount - i] * _hilbertTaps[i];
        }

        var center = values[HalfTap];
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
        _previousOut = delta * OutputScale;
        return _previousOut;
    }

    private static double[] BuildHilbertTaps(int tapCount)
    {
        var taps = new double[tapCount + 1];
        var center = tapCount / 2.0;
        for (var i = 0; i <= tapCount; i++)
        {
            var n = i - center;
            if (Math.Abs(n) < double.Epsilon || ((int)n & 1) == 0)
            {
                taps[i] = 0.0;
                continue;
            }

            var baseCoeff = 2.0 / (Math.PI * n);
            var window = 0.54 - (0.46 * Math.Cos((2.0 * Math.PI * i) / tapCount));
            taps[i] = baseCoeff * window;
        }

        return taps;
    }
}
