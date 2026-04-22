namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested landing zone for TMmsstv::SyncSSTV.
/// It consumes staged sync rows and computes the wrapped base offset MMSSTV
/// uses when re-synchronizing an already-buffered image.
/// </summary>
internal static class MmsstvSyncBufferAligner
{
    public static bool TryComputeBaseOffset(
        IReadOnlyList<short[]> syncRows,
        SstvModeProfile profile,
        int sampleRate,
        int lineWidthSamples,
        int offsetPreviewSamples,
        int stageLineCount,
        bool useRxBuffer,
        bool highAccuracy,
        out int baseOffset)
    {
        baseOffset = 0;
        if (syncRows.Count == 0 || lineWidthSamples <= 0)
        {
            return false;
        }

        var requiredRows = 4;
        if (highAccuracy && useRxBuffer && lineWidthSamples >= sampleRate)
        {
            requiredRows = 3;
        }

        if (syncRows.Count < requiredRows)
        {
            return false;
        }

        var histogram = new int[lineWidthSamples + 2];
        var cursor = 0;

        for (var page = 0; page < requiredRows; page++)
        {
            var row = syncRows[page];
            if (row is null || row.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < Math.Min(lineWidthSamples, row.Length); i++, cursor++)
            {
                var wrapped = PositiveModulo(cursor, lineWidthSamples);
                histogram[wrapped] += row[i];
            }
        }

        var peakPos = 0;
        var peakValue = int.MinValue;
        for (var i = 0; i < histogram.Length; i++)
        {
            if (histogram[i] > peakValue)
            {
                peakValue = histogram[i];
                peakPos = i;
            }
        }

        baseOffset = MmsstvSyncPositionAdjuster.Adjust(
            rawSyncPosition: peakPos,
            profile: profile,
            sampleRate: sampleRate,
            offsetPreviewSamples: offsetPreviewSamples,
            stageLineCount: stageLineCount);
        return true;
    }

    private static int PositiveModulo(int value, int modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}
