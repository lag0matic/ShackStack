using System.Numerics;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ShackStack.DecoderHost.GplWsjtx.Ft4;

internal sealed class Ft4CandidateSearchPort
{
    private readonly double[] _window = BuildNuttallWindow();

    public IReadOnlyList<Ft4PreCandidate> Search(
        float[] frameSamples,
        double startHz,
        double endHz,
        double syncMinimum,
        int maxCandidates,
        double nearFrequencyHz = 0.0)
    {
        if (frameSamples.Length != Ft4Constants.InputFrameSamples)
        {
            throw new ArgumentException($"Expected {Ft4Constants.InputFrameSamples} samples.", nameof(frameSamples));
        }

        var spectra = new double[Ft4Constants.SymbolFftBins + 1, Ft4Constants.HalfSymbolSteps];
        var averageSpectrum = new double[Ft4Constants.SymbolFftBins + 1];
        var fft = new Complex[Ft4Constants.SymbolFftLength];
        const double scale = 1.0 / 300.0;

        for (var step = 0; step < Ft4Constants.HalfSymbolSteps; step++)
        {
            Array.Clear(fft);
            var start = step * Ft4Constants.StepSamples;
            for (var i = 0; i < Ft4Constants.SymbolFftLength; i++)
            {
                fft[i] = new Complex(scale * frameSamples[start + i] * _window[i], 0.0);
            }

            Fourier.Forward(fft, FourierOptions.NoScaling);
            for (var bin = 1; bin <= Ft4Constants.SymbolFftBins; bin++)
            {
                var power = fft[bin].Magnitude * fft[bin].Magnitude;
                spectra[bin, step] = power;
                averageSpectrum[bin] += power;
            }
        }

        for (var i = 1; i <= Ft4Constants.SymbolFftBins; i++)
        {
            averageSpectrum[i] /= Math.Max(1, Ft4Constants.HalfSymbolSteps);
        }

        var df = (double)Ft4Constants.InputSampleRate / Ft4Constants.SymbolFftLength;
        var nfa = Math.Max((int)Math.Round(200.0 / df, MidpointRounding.AwayFromZero), (int)Math.Round(startHz / df, MidpointRounding.AwayFromZero));
        var nfb = Math.Min((int)Math.Round(4910.0 / df, MidpointRounding.AwayFromZero), (int)Math.Round(endHz / df, MidpointRounding.AwayFromZero));
        if (nfb <= nfa)
        {
            return [];
        }

        var smooth = new double[Ft4Constants.SymbolFftBins + 1];
        for (var i = 8; i <= Ft4Constants.SymbolFftBins - 7; i++)
        {
            double sum = 0.0;
            for (var j = i - 7; j <= i + 7; j++)
            {
                sum += averageSpectrum[j];
            }

            smooth[i] = sum / 15.0;
        }

        var baseline = BuildBaseline((double[])averageSpectrum.Clone(), nfa, nfb);
        for (var i = nfa; i <= nfb; i++)
        {
            if (baseline[i] > 0.0)
            {
                smooth[i] /= baseline[i];
            }
        }

        var frequencyOffset = -1.5 * Ft4Constants.Baud;
        var raw = new List<Ft4PreCandidate>();
        for (var i = nfa + 1; i < nfb; i++)
        {
            if (smooth[i] < syncMinimum || smooth[i] < smooth[i - 1] || smooth[i] < smooth[i + 1])
            {
                continue;
            }

            var den = smooth[i - 1] - (2.0 * smooth[i]) + smooth[i + 1];
            var del = den != 0.0 ? 0.5 * (smooth[i - 1] - smooth[i + 1]) / den : 0.0;
            var peakHz = ((i + del) * df) + frequencyOffset;
            if (peakHz is < 200.0 or > 4910.0)
            {
                continue;
            }

            var score = smooth[i] - (0.25 * (smooth[i - 1] - smooth[i + 1]) * del);
            raw.Add(new Ft4PreCandidate(peakHz, score));
        }

        var prioritized = raw
            .OrderByDescending(c => Math.Abs(c.FrequencyHz - nearFrequencyHz) <= 20.0)
            .ThenByDescending(c => c.SyncScore)
            .Take(maxCandidates)
            .ToArray();

        _ = spectra;
        return prioritized;
    }

    private static double[] BuildBaseline(double[] spectrum, int startBin, int endBin)
    {
        for (var i = startBin; i <= endBin; i++)
        {
            spectrum[i] = 10.0 * Math.Log10(Math.Max(spectrum[i], 1e-12));
        }

        const int segments = 10;
        const int percentile = 10;
        var length = Math.Max(1, (endBin - startBin + 1) / segments);
        var midpoint = (endBin - startBin + 1) / 2.0;
        var xs = new List<double>(1000);
        var ys = new List<double>(1000);

        for (var segment = 0; segment < segments; segment++)
        {
            var ja = startBin + (segment * length);
            var jb = Math.Min(endBin, ja + length - 1);
            if (jb < ja)
            {
                continue;
            }

            var slice = new double[jb - ja + 1];
            Array.Copy(spectrum, ja, slice, 0, slice.Length);
            Array.Sort(slice);
            var pIndex = Math.Clamp((int)Math.Round((percentile / 100.0) * slice.Length, MidpointRounding.AwayFromZero) - 1, 0, slice.Length - 1);
            var baseValue = slice[pIndex];

            for (var i = ja; i <= jb; i++)
            {
                if (spectrum[i] <= baseValue && xs.Count < 1000)
                {
                    xs.Add(i - midpoint);
                    ys.Add(spectrum[i]);
                }
            }
        }

        var coeffs = xs.Count >= 5
            ? Fit.Polynomial(xs.ToArray(), ys.ToArray(), 4)
            : [0.0, 0.0, 0.0, 0.0, 0.0];
        var baseline = new double[spectrum.Length];
        for (var i = startBin; i <= endBin; i++)
        {
            var t = i - midpoint;
            var db = Evaluate(coeffs, t) + 0.65;
            baseline[i] = Math.Pow(10.0, db / 10.0);
        }

        return baseline;
    }

    private static double Evaluate(double[] coeffs, double x)
    {
        var value = 0.0;
        for (var i = coeffs.Length - 1; i >= 0; i--)
        {
            value = (value * x) + coeffs[i];
        }

        return value;
    }

    private static double[] BuildNuttallWindow()
    {
        var window = new double[Ft4Constants.SymbolFftLength];
        const double a0 = 0.355768;
        const double a1 = 0.487396;
        const double a2 = 0.144232;
        const double a3 = 0.012604;
        var nMinusOne = Ft4Constants.SymbolFftLength - 1.0;

        for (var i = 0; i < window.Length; i++)
        {
            var phase = (2.0 * Math.PI * i) / nMinusOne;
            window[i] = a0
                        - (a1 * Math.Cos(phase))
                        + (a2 * Math.Cos(2.0 * phase))
                        - (a3 * Math.Cos(3.0 * phase));
        }

        return window;
    }
}

internal sealed record Ft4PreCandidate(double FrequencyHz, double SyncScore);
