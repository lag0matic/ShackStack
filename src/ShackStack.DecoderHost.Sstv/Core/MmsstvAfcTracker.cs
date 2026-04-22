namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvest scaffold for the AFC behavior embedded in CSSTVDEM::SyncFreq.
/// This keeps the original concepts visible while we port the real lock math
/// over from MMSSTV.
/// </summary>
internal sealed class MmsstvAfcTracker
{
    private readonly MmsstvSmoother _average = new();
    private readonly MmsstvSmoother _lockAverage = new();

    public int AfcCount { get; private set; }
    public double AfcData { get; private set; }
    public double AfcLock { get; private set; }
    public double AfcDiff { get; private set; }
    public int AfcFlag { get; private set; }
    public int AfcGuard { get; private set; }
    public int AfcDisable { get; private set; }
    public int AfcInterval { get; set; }

    public double LowBound { get; set; }
    public double HighBound { get; set; }
    public double SyncValue { get; set; }
    public double BandwidthScale { get; set; }
    public double ToneOffsetHz { get; private set; }

    public void Reset()
    {
        AfcCount = 0;
        AfcData = 0.0;
        AfcLock = 0.0;
        AfcDiff = 0.0;
        AfcFlag = 0;
        AfcGuard = 0;
        AfcDisable = 0;
        ToneOffsetHz = 0.0;
    }

    public void Configure(MmsstvAfcParameters parameters)
    {
        LowBound = parameters.LowBound;
        HighBound = parameters.HighBound;
        SyncValue = parameters.SyncValue;
        BandwidthScale = parameters.BandwidthScale;
        AfcGuard = parameters.Guard;
        AfcInterval = parameters.Interval;
        _average.SetCount(2);
        _lockAverage.SetCount(_lockAverage.Capacity);
        AfcData = SyncValue;
        AfcLock = SyncValue;
        AfcFlag = 0;
        AfcDiff = 0.0;
        AfcCount = 0;
        AfcDisable = 0;
        ToneOffsetHz = 0.0;
    }

    public void Update(double demodValue, int afcBegin, int afcEnd)
    {
        var adjusted = demodValue - 128.0;
        if (adjusted <= LowBound && adjusted >= HighBound)
        {
            if (AfcDisable == 0 && AfcCount >= afcBegin && AfcCount <= afcEnd)
            {
                AfcData = _average.Average(adjusted);
                if (AfcCount == afcEnd)
                {
                    if (AfcGuard != 0)
                    {
                        AfcLock = _lockAverage.SetData(AfcData);
                        AfcGuard = 0;
                    }
                    else
                    {
                        AfcLock = _lockAverage.Average(AfcData);
                    }

                    AfcDiff = SyncValue - AfcLock;
                    AfcFlag = 15;
                    AfcDisable = AfcInterval;
                    ToneOffsetHz = AfcDiff * BandwidthScale;
                }
            }

            AfcCount++;
        }
        else
        {
            if (AfcCount >= afcBegin && AfcGuard > 0)
            {
                AfcGuard--;
                if (AfcGuard == 0)
                {
                    _lockAverage.SetData(AfcLock);
                }
            }

            AfcCount = 0;
            if (AfcDisable > 0)
            {
                AfcDisable--;
            }
        }
    }
}
