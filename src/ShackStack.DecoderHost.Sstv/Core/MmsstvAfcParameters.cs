namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested from the AFC setup portions of CSSTVSET::SetSampFreq and
/// CSSTVDEM::Start. This preserves the source-derived AFC timing and bounds
/// setup separately from our current decoder heuristics.
/// </summary>
internal sealed class MmsstvAfcParameters
{
    private const double WideCenterHz = 1900.0;
    private const double WideSyncHz = 1200.0;
    private const double WideAfcLowHz = 1000.0;
    private const double WideAfcHighHz = 1325.0;
    private const double WideBandwidthHalfHz = 400.0;
    private const double NarrowLowHz = 2044.0;
    private const double NarrowHighHz = 2300.0;
    private const double NarrowCenterHz = (NarrowHighHz + NarrowLowHz) / 2.0;
    private const double NarrowSyncHz = 1900.0;
    private const double NarrowAfcLowHz = 1800.0;
    private const double NarrowAfcHighHz = 1950.0;
    private const double NarrowBandwidthHalfHz = (NarrowHighHz - NarrowLowHz) / 2.0;

    private MmsstvAfcParameters(
        int afcWidthSamples,
        int afcBeginSamples,
        int afcEndSamples,
        double lowBound,
        double highBound,
        double syncValue,
        double bandwidthScale,
        int averageSamples,
        int lockAverageSamples,
        int guard,
        int interval)
    {
        AfcWidthSamples = afcWidthSamples;
        AfcBeginSamples = afcBeginSamples;
        AfcEndSamples = afcEndSamples;
        LowBound = lowBound;
        HighBound = highBound;
        SyncValue = syncValue;
        BandwidthScale = bandwidthScale;
        AverageSamples = averageSamples;
        LockAverageSamples = lockAverageSamples;
        Guard = guard;
        Interval = interval;
    }

    public int AfcWidthSamples { get; }
    public int AfcBeginSamples { get; }
    public int AfcEndSamples { get; }
    public double LowBound { get; }
    public double HighBound { get; }
    public double SyncValue { get; }
    public double BandwidthScale { get; }
    public int AverageSamples { get; }
    public int LockAverageSamples { get; }
    public int Guard { get; }
    public int Interval { get; }

    public static MmsstvAfcParameters Create(SstvModeProfile profile, int sampleRate)
    {
        var afcWidthSamples = UsesShortAfcWindow(profile)
            ? (int)Math.Round(2.0 * sampleRate / 1000.0)
            : (int)Math.Round(3.0 * sampleRate / 1000.0);
        var afcBeginSamples = UsesShortAfcWindow(profile)
            ? (int)Math.Round(1.0 * sampleRate / 1000.0)
            : (int)Math.Round(1.5 * sampleRate / 1000.0);
        var afcEndSamples = afcBeginSamples + afcWidthSamples;

        var center = profile.Narrow ? NarrowCenterHz : WideCenterHz;
        var sync = profile.Narrow ? NarrowSyncHz : WideSyncHz;
        var afcLow = profile.Narrow ? NarrowAfcLowHz : WideAfcLowHz;
        var afcHigh = profile.Narrow ? NarrowAfcHighHz : WideAfcHighHz;
        var bandwidthHalf = profile.Narrow ? NarrowBandwidthHalfHz : WideBandwidthHalfHz;
        var lowBound = (center - afcLow) * 16384.0 / bandwidthHalf;
        var highBound = (center - afcHigh) * 16384.0 / bandwidthHalf;
        var syncValue = (center - sync) * 16384.0 / bandwidthHalf;
        var bandwidthScale = bandwidthHalf / 16384.0;
        var averageSamples = Math.Max(1, (int)Math.Round(2.5 * sampleRate / 1000.0));

        return new MmsstvAfcParameters(
            afcWidthSamples,
            afcBeginSamples,
            afcEndSamples,
            lowBound,
            highBound,
            syncValue,
            bandwidthScale,
            averageSamples,
            lockAverageSamples: 15,
            guard: 10,
            interval: (int)Math.Round(100.0 * sampleRate / 1000.0));
    }

    private static bool UsesShortAfcWindow(SstvModeProfile profile)
        => profile.Id is SstvModeId.Martin1
            or SstvModeId.Martin2
            or SstvModeId.Sc2_60
            or SstvModeId.Sc2_120
            or SstvModeId.Sc2_180
            or SstvModeId.WraseMc110
            or SstvModeId.WraseMc140
            or SstvModeId.WraseMc180;
}
