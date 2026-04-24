namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Direct port landing zone for MMSSTV's MakeFilter/CFIR2 receive FIR.
/// CSSTVDEM uses this as m_BPF before CLVL and image demodulation.
/// </summary>
internal sealed class MmsstvFirFilter
{
    public enum FilterType
    {
        LowPass = 0,
        HighPass = 1,
        BandPass = 2,
        BandEliminate = 3
    }

    private double[] _taps = [];
    private double[] _delay = [];
    private int _writeIndex;

    public int TapCount { get; private set; }

    public void Create(int tapCount, FilterType type, double sampleRate, double lowCutHz, double highCutHz, double attenuation, double gain)
    {
        TapCount = Math.Max(0, tapCount);
        _taps = MakeFilter(TapCount, type, sampleRate, lowCutHz, highCutHz, attenuation, gain);
        _delay = new double[(TapCount + 1) * 2];
        _writeIndex = 0;
    }

    public void Create(int tapCount)
    {
        TapCount = Math.Max(0, tapCount);
        _delay = new double[(TapCount + 1) * 2];
        _writeIndex = 0;
    }

    public void Clear()
    {
        Array.Clear(_delay);
        _writeIndex = 0;
    }

    public double Process(double sample)
    {
        if (TapCount <= 0 || _taps.Length == 0)
        {
            return sample;
        }

        var mirrorIndex = _writeIndex + TapCount + 1;
        _delay[mirrorIndex] = sample;
        _delay[_writeIndex] = sample;

        var output = 0.0;
        for (var i = 0; i <= TapCount; i++)
        {
            output += _delay[mirrorIndex - i] * _taps[i];
        }

        _writeIndex++;
        if (_writeIndex > TapCount)
        {
            _writeIndex = 0;
        }

        return output;
    }

    private static double[] MakeFilter(int tapCount, FilterType type, double sampleRate, double lowCutHz, double highCutHz, double attenuation, double gain)
    {
        var half = tapCount / 2;
        var hp = new double[half + 1];
        var fc = type switch
        {
            FilterType.HighPass => (0.5 * sampleRate) - lowCutHz,
            FilterType.LowPass => lowCutHz,
            _ => (highCutHz - lowCutHz) / 2.0
        };

        var alpha = attenuation >= 50.0
            ? 0.1102 * (attenuation - 8.7)
            : attenuation >= 21.0
                ? (0.5842 * Math.Pow(attenuation - 21.0, 0.4)) + (0.07886 * (attenuation - 21.0))
                : 0.0;

        var sumArg = Math.PI * 2.0 * fc / sampleRate;
        for (var j = 0; j <= half; j++)
        {
            if (j == 0)
            {
                hp[j] = fc * 2.0 / sampleRate;
                continue;
            }

            var impulse = (1.0 / (Math.PI * j)) * Math.Sin(j * sumArg);
            if (attenuation >= 21.0)
            {
                var fm = (2.0 * j) / tapCount;
                impulse *= BesselI0(alpha * Math.Sqrt(1.0 - (fm * fm))) / BesselI0(alpha);
            }

            hp[j] = impulse;
        }

        var norm = hp[0];
        for (var j = 1; j <= half; j++)
        {
            norm += 2.0 * hp[j];
        }

        if (norm > 0.0)
        {
            for (var j = 0; j <= half; j++)
            {
                hp[j] /= norm;
            }
        }

        if (type == FilterType.HighPass)
        {
            for (var j = 0; j <= half; j++)
            {
                hp[j] *= Math.Cos(j * Math.PI);
            }
        }
        else if (type != FilterType.LowPass)
        {
            var w0 = Math.PI * (lowCutHz + highCutHz) / sampleRate;
            if (type == FilterType.BandPass)
            {
                for (var j = 0; j <= half; j++)
                {
                    hp[j] *= 2.0 * Math.Cos(j * w0);
                }
            }
            else
            {
                hp[0] = 1.0 - (2.0 * hp[0]);
                for (var j = 1; j <= half; j++)
                {
                    hp[j] *= -2.0 * Math.Cos(j * w0);
                }
            }
        }

        var taps = new double[tapCount + 1];
        var index = 0;
        for (var j = half; j >= 0; j--)
        {
            taps[index++] = hp[j] * gain;
        }

        for (var j = 1; j <= half; j++)
        {
            taps[index++] = hp[j] * gain;
        }

        return taps;
    }

    private static double BesselI0(double value)
    {
        var sum = 1.0;
        var xj = 1.0;
        var j = 1;
        while (true)
        {
            xj *= (0.5 * value) / j;
            var next = xj * xj;
            sum += next;
            j++;
            if (((0.00000001 * sum) - next) > 0.0)
            {
                return sum;
            }
        }
    }
}
