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
        double offsetPreviewSamples,
        int lineWidthSamples,
        int stageLineCount,
        int hillTapQuarter = 0)
    {
        var adjusted = (int)(rawSyncPosition - offsetPreviewSamples);
        adjusted = -adjusted;

        switch (profile.Id)
        {
            case SstvModeId.Scottie1:
            case SstvModeId.Scottie2:
            case SstvModeId.ScottieDx:
                if (adjusted < 0)
                {
                    adjusted += lineWidthSamples;
                }
                break;
            case SstvModeId.Martin1:
            case SstvModeId.Sc2_180:
            case SstvModeId.Sc2_120:
            case SstvModeId.Pasokon3:
            case SstvModeId.Pasokon5:
            case SstvModeId.Pasokon7:
            case SstvModeId.WraseMc110:
            case SstvModeId.WraseMc140:
            case SstvModeId.WraseMc180:
                adjusted += SamplesFromMilliseconds(0.45, sampleRate);
                break;
            case SstvModeId.Martin2:
            case SstvModeId.Sc2_60:
                adjusted += SamplesFromMilliseconds(stageLineCount < 20 ? 0.30 : 0.40, sampleRate);
                break;
            case SstvModeId.Robot36:
            case SstvModeId.Robot72:
                adjusted += SamplesFromMilliseconds(0.16, sampleRate);
                break;
            case SstvModeId.WraseMp73:
            case SstvModeId.WraseMp115:
            case SstvModeId.WraseMp140:
            case SstvModeId.WraseMp175:
            case SstvModeId.WraseMn73:
            case SstvModeId.WraseMn110:
            case SstvModeId.WraseMn140:
            case SstvModeId.WraseMr73:
            case SstvModeId.WraseMr90:
            case SstvModeId.WraseMr115:
            case SstvModeId.WraseMr140:
            case SstvModeId.WraseMr175:
            case SstvModeId.WraseMl180:
            case SstvModeId.WraseMl240:
            case SstvModeId.WraseMl280:
            case SstvModeId.WraseMl320:
                adjusted += SamplesFromMilliseconds(0.20, sampleRate);
                break;
            case SstvModeId.Robot24:
            case SstvModeId.Bw8:
            case SstvModeId.Bw12:
                adjusted += SamplesFromMilliseconds(0.50, sampleRate);
                break;
        }

        if (hillTapQuarter > 0)
        {
            adjusted -= hillTapQuarter;
        }

        return adjusted;
    }

    private static int SamplesFromMilliseconds(double milliseconds, int sampleRate)
        => (int)(milliseconds / 1000.0 * sampleRate);
}
