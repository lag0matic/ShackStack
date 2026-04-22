namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvest landing zone for MMSSTV's CPLL.
/// This preserves the original oscillator/loop/output shape closely enough that
/// we can replace the current shortcut demod path with a source-faithful one
/// in later passes.
/// </summary>
internal sealed class MmsstvPllDemodulator
{
    private readonly MmsstvVco _vco;
    private readonly MmsstvIirFilter _loopLpf = new();
    private readonly MmsstvIirFilter _outLpf = new();
    private double _error;
    private double _output;
    private double _vcoOutput;
    private double _sampleFrequency;
    private double _freeFrequency;
    private double _shift;
    private double _max = 1.0;
    private double _min = -1.0;
    private double _previousInput;
    private double _agc = 1.0;
    private double _agcAverage;

    public int LoopOrder { get; set; } = 1;
    public double LoopCutoffHz { get; set; } = 1500.0;
    public int OutputOrder { get; set; } = 3;
    public double OutputCutoffHz { get; set; } = 900.0;
    public double VcoGain { get; private set; } = 1.0;
    public double OutputGain { get; private set; } = 32768.0;

    public MmsstvPllDemodulator(double sampleFrequency)
    {
        _sampleFrequency = sampleFrequency;
        _vco = new MmsstvVco(sampleFrequency);
        SetWidth(false);
        SetSampleFrequency(sampleFrequency);
    }

    public void SetWidth(bool narrow)
    {
        if (narrow)
        {
            _shift = 256.0;
            _freeFrequency = 2172.0;
            SetFreeFrequency(2044.0, 2300.0);
        }
        else
        {
            _shift = 800.0;
            _freeFrequency = (1500.0 + 2300.0) * 0.5;
            SetFreeFrequency(1500.0, 2300.0);
        }

        SetVcoGain(VcoGain);
    }

    public void SetVcoGain(double gain)
    {
        VcoGain = gain;
        _vco.SetGain(-_shift * gain);
        OutputGain = 32768.0 * gain;
    }

    public void SetFreeFrequency(double low, double high)
    {
        _freeFrequency = (low + high) * 0.5;
        _shift = high - low;
        _vco.SetFreeFrequency(_freeFrequency);
        _vco.SetGain(-_shift * VcoGain);
    }

    public void MakeLoopLpf()
    {
        _loopLpf.MakeIir(LoopCutoffHz, _sampleFrequency, LoopOrder, 0, 0);
        _loopLpf.Clear();
    }

    public void MakeOutLpf()
    {
        _outLpf.MakeIir(OutputCutoffHz, _sampleFrequency, OutputOrder, 0, 0);
        _outLpf.Clear();
    }

    public void SetSampleFrequency(double sampleFrequency)
    {
        _sampleFrequency = sampleFrequency;
        _vco.SetSampleFrequency(sampleFrequency);
        _vco.SetFreeFrequency(_freeFrequency);
        SetVcoGain(1.0);
        MakeLoopLpf();
        MakeOutLpf();
    }

    public double Process(double sample)
    {
        if (_max < sample)
        {
            _max = sample;
        }

        if (_min > sample)
        {
            _min = sample;
        }

        if (sample >= 0.0 && _previousInput < 0.0)
        {
            var span = _max - _min;
            if (Math.Abs(span) > 1e-9)
            {
                _previousInput = 5.0 / span;
                _agc = (_agcAverage + _previousInput) * 0.5;
                _agcAverage = _previousInput;
            }

            _max = 1.0;
            _min = -1.0;
        }

        _previousInput = sample;
        var adjusted = sample * _agc;
        _output = _loopLpf.Process(_error);
        if (_output > 1.5)
        {
            _output = 1.5;
        }
        else if (_output < -1.5)
        {
            _output = -1.5;
        }

        _vcoOutput = _vco.Process(_output);
        _error = _vcoOutput * adjusted;
        return _outLpf.Process(_output) * OutputGain;
    }
}
