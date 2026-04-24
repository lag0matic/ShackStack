namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvest landing zone for MMSSTV's CFQC.
/// This is the zero-crossing/frequency-counter style demod core used by the
/// receive path before AFC and image reconstruction.
/// </summary>
internal sealed class MmsstvFrequencyCounter
{
    private readonly MmsstvIirFilter _outputFilter = new();
    public int Mode { get; set; }
    public int Count { get; private set; }
    public double CrossingCount { get; private set; }
    public double PreviousSample { get; private set; }
    public double NormalizedFrequency { get; private set; } = -1900.0 / 400.0;
    public double Output { get; private set; }
    public double SampleFrequency { get; private set; }
    public double CenterFrequency { get; private set; }
    public double HighFrequency { get; private set; }
    public double LowFrequency { get; private set; }
    public double HighValue { get; private set; }
    public double LowValue { get; private set; }
    public double HalfBandwidth { get; private set; }
    public int Type { get; set; }
    public int Limit { get; set; } = 1;
    public double SmoothFrequency { get; set; } = 2200.0;
    public int OutputOrder { get; set; } = 3;
    public double OutputCutoffHz { get; set; } = 900.0;
    public int Timer { get; private set; }
    public int SampleTimer { get; private set; }

    public MmsstvFrequencyCounter(double sampleFrequency)
    {
        SetWidth(false);
        SetSampleFrequency(sampleFrequency);
        Clear();
    }

    public void SetWidth(bool narrow)
    {
        if (narrow)
        {
            HalfBandwidth = 128.0;
            CenterFrequency = 2172.0;
            HighFrequency = 2400.0;
            LowFrequency = 1800.0;
        }
        else
        {
            HalfBandwidth = 400.0;
            CenterFrequency = 1900.0;
            HighFrequency = 2400.0;
            LowFrequency = 1000.0;
        }

        HighValue = (HighFrequency - CenterFrequency) / HalfBandwidth;
        LowValue = (LowFrequency - CenterFrequency) / HalfBandwidth;
    }

    public void Clear()
    {
        PreviousSample = 0.0;
        Count = 0;
        CrossingCount = 0.0;
        NormalizedFrequency = -CenterFrequency / HalfBandwidth;
        Output = 0.0;
        Timer = SampleTimer;
        _outputFilter.Clear();
    }

    public void SetSampleFrequency(double sampleFrequency)
    {
        SampleFrequency = sampleFrequency;
        SampleTimer = Math.Max(1, (int)Math.Round(sampleFrequency));
        CalcOutputFilter();
    }

    public void CalcOutputFilter()
        => _outputFilter.MakeIir(OutputCutoffHz, SampleFrequency, OutputOrder, 0, 0.0);

    public double Process(double sample)
    {
        void HandleCrossing(double offset)
        {
            var count = Count - CrossingCount;
            count -= offset;
            CrossingCount = Count - offset;
            if (count < 1.0)
            {
                return;
            }

            var hz = SampleFrequency * 0.5 / count;
            if (Limit != 0)
            {
                if (hz > HighFrequency)
                {
                    NormalizedFrequency = HighValue;
                }
                else if (hz < LowFrequency)
                {
                    NormalizedFrequency = LowValue;
                }
                else
                {
                    NormalizedFrequency = (hz - CenterFrequency) / HalfBandwidth;
                }
            }
            else
            {
                NormalizedFrequency = (hz - CenterFrequency) / HalfBandwidth;
                Timer = SampleTimer;
            }
        }

        if (sample >= 0.0 && PreviousSample < 0.0)
        {
            var offset = sample / (sample - PreviousSample);
            HandleCrossing(offset);
        }
        else if (sample < 0.0 && PreviousSample >= 0.0)
        {
            var offset = sample / (sample - PreviousSample);
            HandleCrossing(offset);
        }

        if (Limit == 0 && Timer > 0)
        {
            Timer--;
            if (Timer == 0)
            {
                NormalizedFrequency = -CenterFrequency / HalfBandwidth;
            }
        }

        Output = Type switch
        {
            0 => _outputFilter.Process(NormalizedFrequency),
            _ => NormalizedFrequency,
        };
        PreviousSample = sample;
        Count++;
        return -(Output * 16384.0);
    }
}
