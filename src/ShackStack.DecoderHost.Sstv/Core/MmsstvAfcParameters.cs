namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested from the AFC setup portions of CSSTVSET::SetSampFreq and
/// CSSTVDEM::Start. This preserves the source-derived AFC timing and bounds
/// setup separately from our current decoder heuristics.
/// </summary>
internal sealed class MmsstvAfcParameters
{
    private MmsstvAfcParameters(
        int afcWidthSamples,
        int afcBeginSamples,
        int afcEndSamples,
        double lowBound,
        double highBound,
        double syncValue,
        double bandwidthScale,
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

        const double center = 1900.0;
        const double sync = 1200.0;
        const double bandwidth = 400.0;
        var lowBound = (center - 1000.0) * 16384.0 / bandwidth;
        var highBound = (center - 1325.0) * 16384.0 / bandwidth;
        var syncValue = (center - sync) * 16384.0 / bandwidth;
        var bandwidthScale = bandwidth / 16384.0;

        return new MmsstvAfcParameters(
            afcWidthSamples,
            afcBeginSamples,
            afcEndSamples,
            lowBound,
            highBound,
            syncValue,
            bandwidthScale,
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
