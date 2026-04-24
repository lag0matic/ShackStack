namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested landing zone for CSSTVDEM's demod selection point.
/// MMSSTV keeps PLL, zero-crossing, and Hilbert demodulators alive together and
/// chooses between them by m_Type. This bank mirrors that structure so we can
/// thread the source's receive order in one place.
/// </summary>
internal sealed class MmsstvDemodulatorBank
{
    private readonly MmsstvPllDemodulator _pll;
    private readonly MmsstvFrequencyCounter _frequencyCounter;
    private readonly MmsstvHilbertDemodulator _hilbert;
    private readonly int _sampleRate;

    public MmsstvDemodulatorBank(int sampleRate, bool narrow)
    {
        _sampleRate = sampleRate;
        _pll = new MmsstvPllDemodulator(sampleRate);
        _frequencyCounter = new MmsstvFrequencyCounter(sampleRate);
        _hilbert = new MmsstvHilbertDemodulator(sampleRate, narrow);
        SetWidth(narrow);
    }

    public void SetWidth(bool narrow)
    {
        _pll.SetWidth(narrow);
        _frequencyCounter.SetWidth(narrow);
        _hilbert.SetWidth(_sampleRate, narrow);
    }

    public double ProcessRaw(double sample, MmsstvDemodulatorType type)
    {
        return type switch
        {
            MmsstvDemodulatorType.Pll => _pll.Process(sample),
            MmsstvDemodulatorType.ZeroCrossing => _frequencyCounter.Process(sample),
            _ => _hilbert.Process(sample),
        };
    }

    public double NormalizeRaw(double rawValue, MmsstvDemodulatorType type)
    {
        return type switch
        {
            MmsstvDemodulatorType.Pll => rawValue / 32768.0,
            MmsstvDemodulatorType.ZeroCrossing => -rawValue / 16384.0,
            _ => rawValue / 32768.0,
        };
    }

    public double Process(double sample, MmsstvDemodulatorType type)
        => NormalizeRaw(ProcessRaw(sample, type), type);
}
