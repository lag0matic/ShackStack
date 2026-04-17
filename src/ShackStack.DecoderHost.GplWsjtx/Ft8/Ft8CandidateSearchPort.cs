using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ShackStack.DecoderHost.GplWsjtx.Ft8;

internal sealed class Ft8CandidateSearchPort
{
    private const int MaxLag = 62;
    private static readonly int[] Costas = [3, 1, 4, 0, 6, 5, 2];

    public IReadOnlyList<Ft8PreCandidate> Search(
        float[] cycleSamples,
        double startHz,
        double endHz,
        double syncMinimum,
        int maxCandidates)
    {
        if (cycleSamples.Length != Ft8Constants.InputSamplesPerCycle)
        {
            throw new ArgumentException($"Expected {Ft8Constants.InputSamplesPerCycle} samples.", nameof(cycleSamples));
        }

        var spectra = BuildSymbolSpectra(cycleSamples, out var spectrumAverage);
        var df = (double)Ft8Constants.InputSampleRate / Ft8Constants.SymbolFftLength;
        var ia = Math.Max(1, (int)Math.Round(startHz / df, MidpointRounding.AwayFromZero));
        var ib = Math.Min(Ft8Constants.SymbolFftBins - 7, (int)Math.Round(endHz / df, MidpointRounding.AwayFromZero));
        if (ib <= ia)
        {
            return [];
        }

        var stepsPerSymbol = Ft8Constants.SamplesPerSymbol / Ft8Constants.StepSamples;
        var oversampleBins = Ft8Constants.SymbolFftLength / Ft8Constants.SamplesPerSymbol;
        var startStep = (int)(0.5 / (Ft8Constants.StepSamples / (double)Ft8Constants.InputSampleRate));
        var sync2d = new double[ib - ia + 1, 2 * MaxLag + 1];

        for (var i = ia; i <= ib; i++)
        {
            var fi = i - ia;
            for (var j = -MaxLag; j <= MaxLag; j++)
            {
                var ta = 0.0;
                var tb = 0.0;
                var tc = 0.0;
                var t0a = 0.0;
                var t0b = 0.0;
                var t0c = 0.0;

                for (var n = 0; n < 7; n++)
                {
                    var m = j + startStep + stepsPerSymbol * n;
                    if (m >= 1 && m <= Ft8Constants.HalfSymbolSteps)
                    {
                        ta += spectra[i + oversampleBins * Costas[n], m - 1];
                        t0a += SumCostasBins(spectra, i, oversampleBins, m - 1);
                    }

                    var mb = m + stepsPerSymbol * 36;
                    if (mb >= 1 && mb <= Ft8Constants.HalfSymbolSteps)
                    {
                        tb += spectra[i + oversampleBins * Costas[n], mb - 1];
                        t0b += SumCostasBins(spectra, i, oversampleBins, mb - 1);
                    }

                    var mc = m + stepsPerSymbol * 72;
                    if (mc >= 1 && mc <= Ft8Constants.HalfSymbolSteps)
                    {
                        tc += spectra[i + oversampleBins * Costas[n], mc - 1];
                        t0c += SumCostasBins(spectra, i, oversampleBins, mc - 1);
                    }
                }

                var t = ta + tb + tc;
                var t0 = (t0a + t0b + t0c - t) / 6.0;
                var syncAbc = t0 > 0.0 ? t / t0 : 0.0;

                var tBc = tb + tc;
                var t0Bc = (t0b + t0c - tBc) / 6.0;
                var syncBc = t0Bc > 0.0 ? tBc / t0Bc : 0.0;

                sync2d[fi, j + MaxLag] = Math.Max(syncAbc, syncBc);
            }
        }

        var red = new double[ib + 1];
        var red2 = new double[ib + 1];
        var jpeak = new int[ib + 1];
        var jpeak2 = new int[ib + 1];

        for (var i = ia; i <= ib; i++)
        {
            var fi = i - ia;
            (jpeak[i], red[i]) = FindLagPeak(sync2d, fi, 10);
            (jpeak2[i], red2[i]) = FindLagPeak(sync2d, fi, MaxLag);
        }

        NormalizeByPercentile(red, ia, ib);
        NormalizeByPercentile(red2, ia, ib);

        var candidates = new List<Ft8PreCandidate>();
        var binsByStrength = Enumerable.Range(ia, ib - ia + 1)
            .OrderByDescending(i => red[i])
            .ToArray();

        foreach (var n in binsByStrength)
        {
            if (candidates.Count >= maxCandidates * 4)
            {
                break;
            }

            TryAddCandidate(candidates, n * df, (jpeak[n] - 0.5) * (Ft8Constants.StepSamples / (double)Ft8Constants.InputSampleRate), red[n], syncMinimum);
            if (Math.Abs(jpeak2[n] - jpeak[n]) > 0)
            {
                TryAddCandidate(candidates, n * df, (jpeak2[n] - 0.5) * (Ft8Constants.StepSamples / (double)Ft8Constants.InputSampleRate), red2[n], syncMinimum);
            }
        }

        var deduped = DeduplicateCandidates(candidates)
            .OrderByDescending(c => c.SyncScore)
            .Take(maxCandidates)
            .ToArray();

        _ = spectrumAverage;
        return deduped;
    }

    private static double[,] BuildSymbolSpectra(float[] cycleSamples, out double[] spectrumAverage)
    {
        var spectra = new double[Ft8Constants.SymbolFftBins + 1, Ft8Constants.HalfSymbolSteps];
        spectrumAverage = new double[Ft8Constants.SymbolFftBins + 1];
        var fft = new Complex[Ft8Constants.SymbolFftLength];
        const double fac = 1.0 / 300.0;

        for (var j = 0; j < Ft8Constants.HalfSymbolSteps; j++)
        {
            Array.Clear(fft);
            var start = j * Ft8Constants.StepSamples;
            for (var i = 0; i < Ft8Constants.SamplesPerSymbol; i++)
            {
                fft[i] = new Complex(fac * cycleSamples[start + i], 0.0);
            }

            Fourier.Forward(fft, FourierOptions.NoScaling);
            for (var i = 1; i <= Ft8Constants.SymbolFftBins; i++)
            {
                var p = fft[i].Real * fft[i].Real + fft[i].Imaginary * fft[i].Imaginary;
                spectra[i, j] = p;
                spectrumAverage[i] += p;
            }
        }

        return spectra;
    }

    private static double SumCostasBins(double[,] spectra, int baseBin, int oversampleBins, int stepIndex)
    {
        var total = 0.0;
        for (var k = 0; k <= 6; k++)
        {
            total += spectra[baseBin + oversampleBins * k, stepIndex];
        }

        return total;
    }

    private static (int Lag, double Value) FindLagPeak(double[,] sync2d, int freqIndex, int maxLag)
    {
        var bestLag = 0;
        var bestValue = double.MinValue;
        for (var lag = -maxLag; lag <= maxLag; lag++)
        {
            var value = sync2d[freqIndex, lag + MaxLag];
            if (value > bestValue)
            {
                bestValue = value;
                bestLag = lag;
            }
        }

        return (bestLag, bestValue);
    }

    private static void NormalizeByPercentile(double[] values, int ia, int ib)
    {
        var slice = values.Skip(ia).Take(ib - ia + 1).OrderBy(v => v).ToArray();
        var percentileIndex = (int)Math.Round(0.40 * slice.Length, MidpointRounding.AwayFromZero);
        percentileIndex = Math.Clamp(percentileIndex, 1, slice.Length) - 1;
        var baseline = slice[percentileIndex];
        if (!(baseline > 0.0))
        {
            baseline = 1.0;
        }

        for (var i = ia; i <= ib; i++)
        {
            values[i] /= baseline;
        }
    }

    private static void TryAddCandidate(List<Ft8PreCandidate> candidates, double frequencyHz, double xdtSeconds, double syncScore, double syncMinimum)
    {
        if (double.IsNaN(syncScore) || syncScore < syncMinimum)
        {
            return;
        }

        candidates.Add(new Ft8PreCandidate(Math.Abs(frequencyHz), xdtSeconds, syncScore));
    }

    private static IEnumerable<Ft8PreCandidate> DeduplicateCandidates(List<Ft8PreCandidate> candidates)
    {
        var ordered = candidates.OrderByDescending(c => c.SyncScore).ToList();
        var kept = new List<Ft8PreCandidate>();
        foreach (var candidate in ordered)
        {
            var duplicate = kept.Any(existing =>
                Math.Abs(existing.FrequencyHz - candidate.FrequencyHz) < 4.0 &&
                Math.Abs(existing.XdtSeconds - candidate.XdtSeconds) < 0.04);
            if (!duplicate)
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }
}

internal sealed record Ft8PreCandidate(double FrequencyHz, double XdtSeconds, double SyncScore);
