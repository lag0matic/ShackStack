namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Direct port of the AFC lock behavior in CSSTVDEM::InitAFC/SyncFreq.
/// </summary>
internal sealed class MmsstvAfcTracker
{
    private readonly MmsstvSmoother _average = new();
    private readonly MmsstvSmoother _lockAverage = new();
    private int _afcBegin;
    private int _afcEnd;

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
    public int ToneOffsetHzInt => (int)ToneOffsetHz;

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
        _afcBegin = parameters.AfcBeginSamples;
        _afcEnd = parameters.AfcEndSamples;
        AfcGuard = parameters.Guard;
        AfcInterval = parameters.Interval;
        _average.SetCount(parameters.AverageSamples);
        _lockAverage.SetCount(parameters.LockAverageSamples);
        AfcData = SyncValue;
        AfcLock = SyncValue;
        AfcFlag = 0;
        AfcDiff = 0.0;
        AfcCount = 0;
        AfcDisable = 0;
        ToneOffsetHz = 0.0;
    }

    public void Update(double demodValue)
    {
        var adjusted = demodValue - 128.0;
        if (adjusted <= LowBound && adjusted >= HighBound)
        {
            if (AfcDisable == 0 && AfcCount >= _afcBegin && AfcCount <= _afcEnd)
            {
                AfcData = _average.Average(adjusted);
                if (AfcCount == _afcEnd)
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
            if (AfcCount >= _afcBegin && AfcGuard > 0)
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
