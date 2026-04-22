namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested from MMSSTV's TMmsstv::AdjustSyncPos.
/// This helper converts a raw sync-peak position inside one wrapped line into
/// the mode-shaped base-offset correction MMSSTV applies before redrawing.
/// </summary>
internal static class MmsstvSyncPositionAdjuster
{
    public static int Adjust(
        int rawSyncPosition,
        SstvModeProfile profile,
        int sampleRate,
        int offsetPreviewSamples,
        int stageLineCount,
        int hillTapQuarter = 0)
    {
        var adjusted = rawSyncPosition - offsetPreviewSamples;
        adjusted = -adjusted;

        switch (profile.Name)
        {
            case "Scottie 1":
            case "Scottie 2":
            case "Scottie DX":
                if (adjusted < 0)
                {
                    adjusted += MmsstvTimingEngine.CalculateLineSamples(profile, sampleRate);
                }
                break;

            case "Martin 1":
                adjusted += SamplesFromMilliseconds(0.45, sampleRate);
                break;

            case "Martin 2":
                adjusted += SamplesFromMilliseconds(stageLineCount < 20 ? 0.30 : 0.40, sampleRate);
                break;

            case "Robot 36":
            case "Robot 72":
                adjusted += SamplesFromMilliseconds(0.16, sampleRate);
                break;
        }

        if (hillTapQuarter > 0)
        {
            adjusted -= hillTapQuarter;
        }

        return adjusted;
    }

    private static int SamplesFromMilliseconds(double milliseconds, int sampleRate)
        => (int)Math.Round((milliseconds / 1000.0) * sampleRate);
}
