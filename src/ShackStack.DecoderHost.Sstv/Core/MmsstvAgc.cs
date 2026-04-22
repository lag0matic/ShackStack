namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested from MMSSTV's CAGC.
/// Maintains a rolling peak envelope and converts it into a modest gain factor.
/// </summary>
internal sealed class MmsstvAgc
{
    private readonly MmsstvSmoother _smoother = new();

    public double CurrentMax { get; private set; } = 1.0;
    public double Max { get; private set; }
    public int Count { get; private set; }
    public int CountMax { get; private set; }

    public MmsstvAgc(double sampleRate)
    {
        CountMax = Math.Max(1, (int)Math.Round((100.0 * sampleRate) / 1000.0));
        _smoother.SetCount(5);
    }

    public double Process(double sample)
    {
        var signed = sample;
        var abs = sample < 0.0 ? -sample : sample;
        if (Max < abs)
        {
            Max = abs;
        }

        if (Count >= CountMax)
        {
            CurrentMax = _smoother.Average(Max);
            if (CurrentMax > 0.0)
            {
                CurrentMax = 16384.0 / CurrentMax;
            }

            Max = 0.0;
            Count = 0;
        }

        Count++;
        return signed * CurrentMax;
    }
}
