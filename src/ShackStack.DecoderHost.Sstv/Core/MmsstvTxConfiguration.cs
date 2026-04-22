namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Source-shaped landing zone for the TX-facing pieces of MMSSTV's CSSTVSET.
/// Mirrors SetTxMode/SetTxSampFreq/GetPictureSize enough to prepare a native
/// modulator path without dragging UI/audio transport into the port yet.
/// </summary>
internal sealed record MmsstvTxConfiguration(
    SstvModeProfile Profile,
    double TxSampleFrequency,
    int BitmapWidth,
    int BitmapHeight,
    int PictureHeight,
    double TotalLineTimingMs,
    int TotalLineTimingSamples)
{
    public static MmsstvTxConfiguration Create(SstvModeProfile profile, double txSampleFrequency)
    {
        var pictureHeight = profile.Family switch
        {
            "robot36" => 240,
            "avt" => 240,
            _ => profile.Height,
        };

        var totalTimingSamples = Math.Max(1, (int)Math.Round(profile.TimingMs * txSampleFrequency / 1000.0));
        return new MmsstvTxConfiguration(
            profile,
            txSampleFrequency,
            profile.Width,
            profile.Height,
            pictureHeight,
            profile.TimingMs,
            totalTimingSamples);
    }
}
