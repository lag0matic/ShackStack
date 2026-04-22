namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested landing zone for MMSSTV's CLVL.
/// Tracks current sample magnitude, recent peak envelope, and AGC gain using
/// the same broad timing model MMSSTV uses in its receiver front-end.
/// </summary>
internal sealed class MmsstvLevelTracker
{
    public double Current { get; private set; }
    public double PeakMax { get; private set; }
    public double PeakAgc { get; private set; }
    public double Peak { get; private set; }
    public double CurrentMax { get; private set; }
    public double Max { get; private set; }
    public double Agc { get; private set; } = 1.0;
    public int PeakCounter { get; private set; }
    public int Counter { get; private set; }
    public int CounterMax { get; private set; }
    public bool FastAgc { get; set; }

    public MmsstvLevelTracker(double sampleRate)
    {
        CounterMax = Math.Max(1, (int)Math.Round(sampleRate * 100.0 / 1000.0));
        Init();
    }

    public void Init()
    {
        PeakMax = 0.0;
        PeakAgc = 0.0;
        Peak = 0.0;
        Current = 0.0;
        CurrentMax = 0.0;
        Max = 0.0;
        Agc = 1.0;
        PeakCounter = 0;
        Counter = 0;
    }

    public void Process(double sample)
    {
        Current = sample;
        var magnitude = sample < 0.0 ? -sample : sample;
        if (Max < magnitude)
        {
            Max = magnitude;
        }

        Counter++;
    }

    public void Fix()
    {
        if (Counter < CounterMax)
        {
            return;
        }

        Counter = 0;
        PeakCounter++;
        if (Peak < Max)
        {
            Peak = Max;
        }

        if (PeakCounter >= 5)
        {
            PeakCounter = 0;
            PeakMax = Max;
            PeakAgc = (PeakAgc + Max) * 0.5;
            Peak = 0.0;
            if (!FastAgc)
            {
                Agc = PeakAgc > 32.0 && PeakMax > 0.0
                    ? 16384.0 / PeakMax
                    : 16384.0 / 32.0;
            }
        }
        else if (PeakMax < Max)
        {
            PeakMax = Max;
        }

        CurrentMax = Max;
        if (FastAgc)
        {
            Agc = CurrentMax > 32.0
                ? 16384.0 / CurrentMax
                : 16384.0 / 32.0;
        }

        Max = 0.0;
    }

    public double ApplyAgc(double sample) => sample * Agc;
}
