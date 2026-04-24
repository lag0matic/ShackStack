namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested landing zone for TMmsstv::SyncSSTV.
/// It consumes staged sync rows and computes the wrapped base offset MMSSTV
/// uses when re-synchronizing an already-buffered image.
/// </summary>
internal static class MmsstvSyncBufferAligner
{
    private const int SlantCorrectionMinRows = 16;

    public static bool TryComputeBaseOffset(
        IReadOnlyList<short[]> syncRows,
        SstvModeProfile profile,
        int sampleRate,
        int lineWidthSamples,
        double drawLineWidthSamples,
        double offsetPreviewSamples,
        int stageLineCount,
        bool useRxBuffer,
        bool highAccuracy,
        int hillTapQuarter,
        out int baseOffset)
    {
        baseOffset = 0;
        if (syncRows.Count == 0 || lineWidthSamples <= 0 || drawLineWidthSamples <= 0.0)
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

        var histogram = new int[(int)drawLineWidthSamples + 2];
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
                var wrapped = (int)(cursor - (Math.Floor(cursor / drawLineWidthSamples) * drawLineWidthSamples));
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
            lineWidthSamples: lineWidthSamples,
            stageLineCount: stageLineCount,
            hillTapQuarter: hillTapQuarter);
        return true;
    }

    public static bool TryComputeSlantCorrection(
        IReadOnlyList<short[]> syncRows,
        int sampleRate,
        int lineWidthSamples,
        double drawLineWidthSamples,
        out double correctedDrawLineSamples,
        out string debug)
    {
        correctedDrawLineSamples = drawLineWidthSamples;
        debug = "slant: not-run";
        if (syncRows.Count < SlantCorrectionMinRows || lineWidthSamples <= 0 || drawLineWidthSamples <= 0.0)
        {
            debug = $"slant: skipped rows {syncRows.Count} wd {lineWidthSamples} tw {drawLineWidthSamples:0.000}";
            return false;
        }

        var startDrawLineSamples = drawLineWidthSamples;
        var currentDrawLineSamples = drawLineWidthSamples;
        var currentSampleRate = (double)sampleRate;
        var allowedWander = currentDrawLineSamples * 0.1;
        var iterations = new List<string>(capacity: 5);
        var anyChange = false;
        for (var iteration = 0; iteration < 5; iteration++)
        {
            if (!TryComputeSlantCorrectionPass(
                    syncRows,
                    sampleRate,
                    lineWidthSamples,
                    currentDrawLineSamples,
                    currentSampleRate,
                    startDrawLineSamples,
                    allowedWander,
                    out var nextDrawLineSamples,
                    out var nextSampleRate,
                    out var passDebug))
            {
                debug = iterations.Count == 0
                    ? passDebug
                    : $"{string.Join(" | ", iterations)} | {passDebug}";
                return anyChange;
            }

            iterations.Add($"z{iteration}: {passDebug}");
            var changed = Math.Abs(nextDrawLineSamples - currentDrawLineSamples)
                >= (0.1 / 11025.0 * sampleRate);
            currentDrawLineSamples = nextDrawLineSamples;
            currentSampleRate = nextSampleRate;
            anyChange |= changed;
            if (!changed)
            {
                correctedDrawLineSamples = currentDrawLineSamples;
                debug = $"{string.Join(" | ", iterations)} | converged";
                return anyChange;
            }

            allowedWander *= 0.5;
        }

        correctedDrawLineSamples = currentDrawLineSamples;
        debug = $"{string.Join(" | ", iterations)} | max-iterations";
        return anyChange;
    }

    private static bool TryComputeSlantCorrectionPass(
        IReadOnlyList<short[]> syncRows,
        int sampleRate,
        int lineWidthSamples,
        double drawLineWidthSamples,
        double currentSampleRate,
        double startDrawLineSamples,
        double allowedWander,
        out double correctedDrawLineSamples,
        out double correctedSampleRate,
        out string debug)
    {
        correctedDrawLineSamples = drawLineWidthSamples;
        correctedSampleRate = currentSampleRate;
        var rowsToUse = syncRows.Count;
        var histogramRows = Math.Min(32, syncRows.Count);
        var histogram = new int[(int)drawLineWidthSamples + 2];
        var cursor = 0;
        for (var rowIndex = 0; rowIndex < histogramRows; rowIndex++)
        {
            for (var i = 0; i < lineWidthSamples; i++, cursor++)
            {
                if (!TryGetStagedSample(syncRows, lineWidthSamples, (rowIndex * lineWidthSamples) + i, out var sample))
                {
                    break;
                }

                var wrapped = Wrap(cursor, drawLineWidthSamples);
                histogram[wrapped] += sample;
            }
        }

        var basePeak = MaxIndex(histogram);
        if (!TryFitSyncSlope(
                syncRows,
                rowsToUse,
                originalLineWidthSamples: lineWidthSamples,
                currentLineWidthSamples: lineWidthSamples,
                drawLineWidthSamples,
                basePeak,
                allowedWander,
                out var acceptedLines,
                out var regressionLines,
                out var fitReason,
                out var slopeSamplesPerLine))
        {
            debug = $"slant: wd {lineWidthSamples} tw {drawLineWidthSamples:0.000} base {basePeak:0.000} accepted {acceptedLines} fit {regressionLines} fail {fitReason}";
            return false;
        }

        correctedSampleRate = currentSampleRate + (slopeSamplesPerLine * currentSampleRate / drawLineWidthSamples);
        correctedSampleRate = NormalizeSampleFrequency(correctedSampleRate, 100.0);
        correctedDrawLineSamples = startDrawLineSamples * correctedSampleRate / sampleRate;
        var applied = Math.Abs(correctedDrawLineSamples - drawLineWidthSamples)
            >= (0.1 / 11025.0 * sampleRate);

        debug =
            $"slant: wd {lineWidthSamples} tw {drawLineWidthSamples:0.000} base {basePeak:0.000} accepted {acceptedLines} fit {regressionLines} " +
            $"slope {slopeSamplesPerLine:0.000000} fq {correctedSampleRate:0.00} next {correctedDrawLineSamples:0.000}" +
            (applied ? " | applied" : " | converged-no-change");
        return true;
    }

    private static bool TryFitSyncSlope(
        IReadOnlyList<short[]> syncRows,
        int rowsToUse,
        int originalLineWidthSamples,
        int currentLineWidthSamples,
        double drawLineWidthSamples,
        double basePeak,
        double allowedWander,
        out int acceptedLines,
        out int regressionLines,
        out string reason,
        out double slopeSamplesPerLine)
    {
        slopeSamplesPerLine = 0.0;
        acceptedLines = 0;
        regressionLines = 0;
        reason = "unknown";
        var baseSample = 0;
        var currentLine = 0;
        var max = int.MinValue;
        var min = int.MaxValue;
        var peak = 0.0;
        var acceptedCount = 0;
        var regressionCount = 0;
        double sumLine = 0.0;
        double sumPeak = 0.0;
        double sumLineSquared = 0.0;
        double sumLinePeak = 0.0;

        for (var rowIndex = 0; rowIndex < rowsToUse; rowIndex++)
        {
            for (var i = 0; i < currentLineWidthSamples; i++, baseSample++)
            {
                if (!TryGetStagedSample(syncRows, originalLineWidthSamples, (rowIndex * currentLineWidthSamples) + i, out var value))
                {
                    break;
                }

                var yy = (int)(baseSample / drawLineWidthSamples);
                var xx = baseSample - (Math.Floor(baseSample / drawLineWidthSamples) * drawLineWidthSamples);
                if (yy != currentLine)
                {
                    if (!AccumulateLinePeak(
                            currentLine,
                            max,
                            min,
                            peak,
                            ref basePeak,
                            drawLineWidthSamples,
                            allowedWander,
                            ref acceptedCount,
                            ref regressionCount,
                            ref sumLine,
                            ref sumPeak,
                            ref sumLineSquared,
                            ref sumLinePeak,
                            out reason))
                    {
                        acceptedLines = acceptedCount;
                        regressionLines = regressionCount;
                        return false;
                    }

                    if (acceptedCount >= rowsToUse)
                    {
                        break;
                    }

                    currentLine = yy;
                    max = int.MinValue;
                    min = int.MaxValue;
                    peak = 0.0;
                }

                if (value > max)
                {
                    max = value;
                    peak = xx;
                }
                if (value < min)
                {
                    min = value;
                }
            }

            if (acceptedCount >= rowsToUse)
            {
                break;
            }
        }

        if (regressionCount < 6)
        {
            acceptedLines = acceptedCount;
            regressionLines = regressionCount;
            reason = "too-few-fit-lines";
            return false;
        }

        var denominator = (regressionCount * sumLineSquared) - (sumLine * sumLine);
        if (Math.Abs(denominator) < double.Epsilon)
        {
            acceptedLines = acceptedCount;
            regressionLines = regressionCount;
            reason = "zero-denominator";
            return false;
        }

        slopeSamplesPerLine = ((regressionCount * sumLinePeak) - (sumPeak * sumLine)) / denominator;
        acceptedLines = acceptedCount;
        regressionLines = regressionCount;
        reason = double.IsFinite(slopeSamplesPerLine) ? "ok" : "non-finite-slope";
        return double.IsFinite(slopeSamplesPerLine);
    }

    private static bool AccumulateLinePeak(
        int line,
        int max,
        int min,
        double peak,
        ref double basePeak,
        double drawLineWidthSamples,
        double allowedWander,
        ref int acceptedCount,
        ref int regressionCount,
        ref double sumLine,
        ref double sumPeak,
        ref double sumLineSquared,
        ref double sumLinePeak,
        out string reason)
    {
        reason = "ok";
        if (max == int.MinValue || min == int.MaxValue)
        {
            return true;
        }

        peak = UnwrapPeakNearBase(peak, basePeak, drawLineWidthSamples, out var ambiguous);
        if (ambiguous)
        {
            reason = "ambiguous-wrap";
            return false;
        }

        if (line >= 0 && (max - min) >= 4800 && Math.Abs(peak - basePeak) <= allowedWander)
        {
            basePeak = peak;
            if (acceptedCount >= 2)
            {
                sumLine += line;
                sumPeak += peak;
                sumLineSquared += line * line;
                sumLinePeak += line * peak;
                regressionCount++;
            }

            acceptedCount++;
        }

        return true;
    }

    private static double UnwrapPeakNearBase(double peak, double basePeak, double drawLineWidthSamples, out bool ambiguous)
    {
        ambiguous = false;
        if (basePeak < 0)
        {
            if (peak >= drawLineWidthSamples / 4.0)
            {
                return peak - drawLineWidthSamples;
            }

            if (peak >= drawLineWidthSamples / 8.0)
            {
                ambiguous = true;
            }

            return peak;
        }

        if (basePeak >= drawLineWidthSamples)
        {
            if (peak < drawLineWidthSamples * 3.0 / 4.0)
            {
                return peak + drawLineWidthSamples;
            }

            if (peak < drawLineWidthSamples * 7.0 / 8.0)
            {
                ambiguous = true;
            }

            return peak;
        }

        if (basePeak >= drawLineWidthSamples * 3.0 / 4.0 && peak < drawLineWidthSamples / 4.0)
        {
            return peak + drawLineWidthSamples;
        }

        if (basePeak <= drawLineWidthSamples / 4.0 && peak >= drawLineWidthSamples * 3.0 / 4.0)
        {
            return peak - drawLineWidthSamples;
        }

        return peak;
    }

    private static int MaxIndex(int[] values)
    {
        var peakPos = 0;
        var peakValue = int.MinValue;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] > peakValue)
            {
                peakValue = values[i];
                peakPos = i;
            }
        }

        return peakPos;
    }

    private static int Wrap(int cursor, double width)
        => (int)(cursor - (Math.Floor(cursor / width) * width));

    private static double NormalizeSampleFrequency(double sampleFrequency, double multiplier)
        => (int)((sampleFrequency * multiplier) + 0.5) / multiplier;

    private static bool TryGetStagedSample(
        IReadOnlyList<short[]> rows,
        int originalLineWidthSamples,
        int flatIndex,
        out short sample)
    {
        sample = 0;
        if (originalLineWidthSamples <= 0 || flatIndex < 0)
        {
            return false;
        }

        var rowIndex = flatIndex / originalLineWidthSamples;
        var columnIndex = flatIndex - (rowIndex * originalLineWidthSamples);
        if (rowIndex < 0 || rowIndex >= rows.Count)
        {
            return false;
        }

        var row = rows[rowIndex];
        if (columnIndex < 0 || columnIndex >= row.Length)
        {
            return false;
        }

        sample = row[columnIndex];
        return true;
    }
}
