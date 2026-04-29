namespace ShackStack.DecoderHost.Sstv.Core;

internal static class VisDetector
{
    private const int WorkingSampleRate = SstvWorkingConfig.WorkingSampleRate;
    private const double FreqVisBit1 = 1100.0;
    private const double FreqSync = 1200.0;
    private const double FreqVisBit0 = 1300.0;
    private const double FreqVisStart = 1900.0;
    private const double DefaultToneShareThreshold = 0.45;
    private const double DefaultToneLeadThreshold = 1.20;
    private const double StrictToneShareThreshold = 0.55;
    private const double StrictToneLeadThreshold = 1.35;
    private const double DefaultBitShareThreshold = 0.45;
    private const double DefaultBitLeadThreshold = 1.15;
    private const double StrictBitShareThreshold = 0.55;
    private const double StrictBitLeadThreshold = 1.25;
    private static readonly int[] LegacyFrameMs = [10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10];
    private static readonly int[] MmsstvFrameMs = [300, 10, 300, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30];
    private static readonly double?[] LegacyFrameTones = [FreqVisStart, FreqSync, null, null, null, null, null, null, null, null, FreqSync, null];
    private static readonly double?[] MmsstvFrameTones = [FreqVisStart, FreqSync, FreqVisStart, FreqSync, null, null, null, null, null, null, null, null, FreqSync];
    private static readonly int MaxPatternFrames10Ms = Math.Max(
        DurationToFrames10Ms(LegacyFrameMs),
        DurationToFrames10Ms(MmsstvFrameMs));

    public static bool TryDetect(
        List<float> samples,
        int startFrameIndex,
        bool allowLegacyPattern,
        out int nextFrameIndex,
        out SstvModeProfile? profile,
        bool resolveAllPlannedModes = false)
    {
        profile = null;
        nextFrameIndex = startFrameIndex;
        var startSampleIndex = startFrameIndex * WorkingSampleRate * 10 / 1000;
        if (TryDetectPattern(
                samples,
                startSampleIndex,
                MmsstvFrameMs,
                MmsstvFrameTones,
                dataStartIndex: 4,
                stopIndex: 12,
                stripParity: true,
                strict: !allowLegacyPattern,
                resolveAllPlannedModes,
                out nextFrameIndex,
                out profile))
        {
            return true;
        }

        if (allowLegacyPattern &&
            TryDetectPattern(
                samples,
                startSampleIndex,
                LegacyFrameMs,
                LegacyFrameTones,
                dataStartIndex: 2,
                stopIndex: 10,
                stripParity: false,
                strict: false,
                resolveAllPlannedModes,
                out nextFrameIndex,
                out profile))
        {
            return true;
        }

        var totalFrames10Ms = samples.Count / (WorkingSampleRate * 10 / 1000);
        nextFrameIndex = Math.Max(startFrameIndex, Math.Max(0, totalFrames10Ms - MaxPatternFrames10Ms));
        return false;
    }

    private static bool TryDetectPattern(
        List<float> samples,
        int startSampleIndex,
        IReadOnlyList<int> frameDurationsMs,
        IReadOnlyList<double?> frameTones,
        int dataStartIndex,
        int stopIndex,
        bool stripParity,
        bool strict,
        bool resolveAllPlannedModes,
        out int nextFrameIndex,
        out SstvModeProfile? profile)
    {
        profile = null;
        nextFrameIndex = 0;
        var bestScore = double.NegativeInfinity;
        var bestNextFrameIndex = 0;
        SstvModeProfile? bestProfile = null;

        var strideSamples = WorkingSampleRate * 10 / 1000;
        var maxStart = samples.Count - DurationToSamples(frameDurationsMs);
        if (maxStart <= 0 || startSampleIndex > maxStart)
        {
            return false;
        }

        for (var sampleIndex = startSampleIndex; sampleIndex <= maxStart; sampleIndex += strideSamples)
        {
            if (!IsTone(samples, sampleIndex, frameDurationsMs[0], FreqVisStart, strict))
            {
                continue;
            }

            var cursor = sampleIndex + DurationToSamples(frameDurationsMs[0]);
            // MMSSTV's real RX follows this as a continuous state machine. The
            // 10 ms VIS break is easy to smear by a frame over RF, so do not let
            // that one edge reject an otherwise parity-valid VIS frame.
            if (frameDurationsMs[1] > 10 && !IsTone(samples, cursor, frameDurationsMs[1], FreqSync, strict))
            {
                continue;
            }

            var bits = new int[8];
            cursor += DurationToSamples(frameDurationsMs[1]);
            var valid = true;
            for (var i = 2; i < dataStartIndex; i++)
            {
                if (frameTones[i] is { } expectedTone
                    && expectedTone != FreqSync
                    && !IsTone(samples, cursor, frameDurationsMs[i], expectedTone, strict))
                {
                    valid = false;
                    break;
                }

                cursor += DurationToSamples(frameDurationsMs[i]);
            }

            if (!valid)
            {
                continue;
            }

            var bitStart = cursor;
            for (var bitOffset = -strideSamples * 3; bitOffset <= strideSamples * 3 && valid; bitOffset += strideSamples)
            {
                cursor = bitStart + bitOffset;
                if (cursor < 0)
                {
                    continue;
                }

                var score = 0.0;
                for (var bitIndex = 0; bitIndex < bits.Length; bitIndex++)
                {
                    if (TryClassifyBit(samples, cursor, frameDurationsMs[dataStartIndex + bitIndex], strict, out var bit, out var bitScore))
                    {
                        bits[bitIndex] = bit;
                        score += bitScore;
                        cursor += DurationToSamples(frameDurationsMs[dataStartIndex + bitIndex]);
                    }
                    else
                    {
                        valid = false;
                        break;
                    }
                }

                if (!valid)
                {
                    valid = true;
                    continue;
                }

                var visCode = 0;
                for (var bitIndex = 0; bitIndex < bits.Length; bitIndex++)
                {
                    visCode |= bits[bitIndex] << bitIndex;
                }

                if (!HasValidEvenParity(visCode))
                {
                    continue;
                }

                if (stripParity)
                {
                    visCode &= 0x7f;
                }

                nextFrameIndex = Math.Max(0, (cursor + DurationToSamples(frameDurationsMs[stopIndex])) / strideSamples);
                SstvModeProfile detected;
                var resolved = resolveAllPlannedModes
                    ? MmsstvModeCatalog.TryResolvePlannedVis(visCode, out detected)
                    : MmsstvModeCatalog.TryResolveVis(visCode, out detected);
                if (resolved)
                {
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestProfile = detected;
                        bestNextFrameIndex = nextFrameIndex;
                    }
                }
            }

            if (!valid)
            {
                continue;
            }
        }

        if (bestProfile is not null)
        {
            profile = bestProfile;
            nextFrameIndex = bestNextFrameIndex;
            return true;
        }

        return false;
    }

    private static bool IsTone(List<float> samples, int startSampleIndex, int durationMs, double expectedFreq, bool strict)
    {
        var block = SliceWindow(samples, startSampleIndex, durationMs);
        if (block.Length == 0)
        {
            return false;
        }

        var tonePower = SstvAudioMath.TonePower(block, WorkingSampleRate, expectedFreq);
        var p1100 = expectedFreq == FreqVisBit1 ? tonePower : SstvAudioMath.TonePower(block, WorkingSampleRate, FreqVisBit1);
        var p1200 = expectedFreq == FreqSync ? tonePower : SstvAudioMath.TonePower(block, WorkingSampleRate, FreqSync);
        var p1300 = expectedFreq == FreqVisBit0 ? tonePower : SstvAudioMath.TonePower(block, WorkingSampleRate, FreqVisBit0);
        var p1900 = expectedFreq == FreqVisStart ? tonePower : SstvAudioMath.TonePower(block, WorkingSampleRate, FreqVisStart);
        var total = p1100 + p1200 + p1300 + p1900;
        var strongestOther = expectedFreq switch
        {
            var f when f == FreqVisBit1 => Math.Max(p1200, Math.Max(p1300, p1900)),
            var f when f == FreqSync => Math.Max(p1100, Math.Max(p1300, p1900)),
            var f when f == FreqVisBit0 => Math.Max(p1100, Math.Max(p1200, p1900)),
            _ => Math.Max(p1100, Math.Max(p1200, p1300)),
        };

        var shareThreshold = strict ? StrictToneShareThreshold : DefaultToneShareThreshold;
        var leadThreshold = strict ? StrictToneLeadThreshold : DefaultToneLeadThreshold;
        return total > 0.0
            && (tonePower / total) >= shareThreshold
            && tonePower >= (strongestOther * leadThreshold);
    }

    private static bool TryClassifyBit(List<float> samples, int startSampleIndex, int durationMs, bool strict, out int bit, out double score)
    {
        bit = 0;
        score = 0.0;
        var block = SliceWindow(samples, startSampleIndex, durationMs);
        if (block.Length == 0)
        {
            return false;
        }

        var power1 = SstvAudioMath.TonePower(block, WorkingSampleRate, FreqVisBit1);
        var power0 = SstvAudioMath.TonePower(block, WorkingSampleRate, FreqVisBit0);
        var total = power1 + power0
            + SstvAudioMath.TonePower(block, WorkingSampleRate, FreqSync)
            + SstvAudioMath.TonePower(block, WorkingSampleRate, FreqVisStart);

        var strongest = Math.Max(power1, power0);
        var weakest = Math.Min(power1, power0);
        var shareThreshold = strict ? StrictBitShareThreshold : DefaultBitShareThreshold;
        var leadThreshold = strict ? StrictBitLeadThreshold : DefaultBitLeadThreshold;
        var share = total > 0.0 ? strongest / total : 0.0;
        if (total <= 0.0 || share < shareThreshold || strongest < (weakest * leadThreshold))
        {
            return false;
        }

        bit = power1 >= power0 ? 1 : 0;
        score = Math.Log((strongest + 1.0) / (weakest + 1.0)) + share;
        return true;
    }

    private static int DurationToSamples(IReadOnlyList<int> durationsMs)
        => durationsMs.Sum(static ms => DurationToSamples(ms));

    private static int DurationToSamples(int durationMs)
        => Math.Max(1, WorkingSampleRate * durationMs / 1000);

    private static int DurationToFrames10Ms(IReadOnlyList<int> durationsMs)
        => Math.Max(1, durationsMs.Sum() / 10);

    private static bool HasValidEvenParity(int visCode)
    {
        var payload = visCode & 0x7f;
        var parityBit = (visCode >> 7) & 0x01;
        var oneCount = System.Numerics.BitOperations.PopCount((uint)payload) + parityBit;
        return (oneCount % 2) == 0;
    }

    private static ReadOnlySpan<float> SliceWindow(List<float> samples, int startSampleIndex, int durationMs)
    {
        if (startSampleIndex >= samples.Count)
        {
            return ReadOnlySpan<float>.Empty;
        }

        var count = Math.Min(DurationToSamples(durationMs), samples.Count - startSampleIndex);
        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(samples).Slice(startSampleIndex, count);
    }
}
