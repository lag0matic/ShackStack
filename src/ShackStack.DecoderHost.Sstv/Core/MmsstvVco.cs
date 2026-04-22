namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Source-shaped landing zone for MMSSTV's CVCO.
/// The behavior is intentionally close to the original table-driven oscillator
/// so later ports can move over PLL/Hilbert demod paths without inventing a
/// different primitive first.
/// </summary>
internal sealed class MmsstvVco
{
    private double _gainTableFactor;
    private double _tableStep;
    private double _phase;
    private int _tableSize;
    private double[] _sinTable = [];

    public double FreeFrequency { get; private set; }
    public double SampleFrequency { get; private set; }

    public MmsstvVco(double sampleFrequency)
    {
        SetSampleFrequency(sampleFrequency);
        SetFreeFrequency(1900.0);
        SetGain(1.0);
        InitializePhase();
    }

    public void InitializePhase()
    {
        _phase = 0.0;
    }

    public void SetGain(double gain)
    {
        _gainTableFactor = _tableSize * gain / SampleFrequency;
    }

    public void SetSampleFrequency(double sampleFrequency)
    {
        SampleFrequency = sampleFrequency;
        var newSize = Math.Max(8, (int)Math.Round(sampleFrequency * 2.0));
        if (newSize != _tableSize)
        {
            _tableSize = newSize;
            _sinTable = new double[_tableSize];
            var step = 2.0 * Math.PI / _tableSize;
            for (var i = 0; i < _tableSize; i++)
            {
                _sinTable[i] = Math.Sin(i * step);
            }
        }

        SetFreeFrequency(FreeFrequency <= 0.0 ? 1900.0 : FreeFrequency);
    }

    public void SetFreeFrequency(double frequency)
    {
        FreeFrequency = frequency;
        _tableStep = _tableSize * FreeFrequency / SampleFrequency;
    }

    public double Process(double control)
    {
        _phase += (control * _gainTableFactor) + _tableStep;
        while (_phase >= _tableSize)
        {
            _phase -= _tableSize;
        }

        while (_phase < 0.0)
        {
            _phase += _tableSize;
        }

        return _sinTable[(int)_phase];
    }
}
