namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested from CSSTVSET::InitIntervalPara.
/// Keeps the original mode interval table and threshold values in one place.
/// </summary>
internal sealed class MmsstvIntervalParameters
{
    private readonly Dictionary<SstvModeId, uint> _modeSamples;

    private MmsstvIntervalParameters(
        Dictionary<SstvModeId, uint> modeSamples,
        int sampleRate,
        uint syncLowestLine,
        uint syncLowest,
        uint syncHighest)
    {
        _modeSamples = modeSamples;
        SampleRate = sampleRate;
        SyncLowestLine = syncLowestLine;
        SyncLowest = syncLowest;
        SyncHighest = syncHighest;
    }

    public int SampleRate { get; }
    public uint SyncLowestLine { get; }
    public uint SyncLowest { get; }
    public uint SyncHighest { get; }

    public uint GetModeSamples(SstvModeId modeId)
        => _modeSamples.TryGetValue(modeId, out var value) ? value : 0u;

    public static MmsstvIntervalParameters Create(int sampleRate)
    {
        var modeSamples = new Dictionary<SstvModeId, uint>();
        foreach (var profile in MmsstvModeCatalog.Profiles)
        {
            modeSamples[profile.Id] = (uint)MmsstvPictureGeometry.CalculateLineSamples(profile, sampleRate);
        }

        // MMSSTV special-case: AVT does not participate in the normal sync interval match table.
        modeSamples[SstvModeId.Avt90] = 0;

        return new MmsstvIntervalParameters(
            modeSamples,
            sampleRate,
            syncLowestLine: (uint)Math.Round(50.0 * sampleRate / 1000.0),
            syncLowest: (uint)Math.Round(63.0 * sampleRate / 1000.0),
            syncHighest: (uint)Math.Round(1390.0 * 3.0 * sampleRate / 1000.0));
    }
}
