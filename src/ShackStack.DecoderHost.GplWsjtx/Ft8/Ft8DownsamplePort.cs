using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ShackStack.DecoderHost.GplWsjtx.Ft8;

internal sealed class Ft8DownsamplePort
{
    private readonly double[] _taper = BuildTaper();
    private Complex[]? _spectrum;

    public void Prepare(float[] cycleSamples)
    {
        if (cycleSamples.Length != Ft8Constants.InputSamplesPerCycle)
        {
            throw new ArgumentException($"Expected {Ft8Constants.InputSamplesPerCycle} samples.", nameof(cycleSamples));
        }

        var fft = new Complex[Ft8Constants.LongFftLength];
        for (var i = 0; i < cycleSamples.Length; i++)
        {
            fft[i] = new Complex(cycleSamples[i], 0.0);
        }

        Fourier.Forward(fft, FourierOptions.NoScaling);
        _spectrum = fft;
    }

    public Complex[] ExtractLane(double f0)
    {
        if (_spectrum is null)
        {
            throw new InvalidOperationException("Prepare must be called before ExtractLane.");
        }

        var df = (double)Ft8Constants.InputSampleRate / Ft8Constants.LongFftLength;
        var i0 = (int)Math.Round(f0 / df, MidpointRounding.AwayFromZero);
        var ft = f0 + 8.5 * Ft8Constants.Baud;
        var it = Math.Min((int)Math.Round(ft / df, MidpointRounding.AwayFromZero), Ft8Constants.LongFftLength / 2);
        var fb = f0 - 1.5 * Ft8Constants.Baud;
        var ib = Math.Max(1, (int)Math.Round(fb / df, MidpointRounding.AwayFromZero));

        var lane = new Complex[Ft8Constants.DownsampledFftLength];
        var k = 0;
        for (var i = ib; i <= it && k < lane.Length; i++, k++)
        {
            lane[k] = _spectrum[i];
        }

        var edgeLength = Math.Min(101, Math.Max(0, k));
        for (var i = 0; i < edgeLength; i++)
        {
            lane[i] *= _taper[100 - i];
        }

        if (k >= 101)
        {
            for (var i = 0; i < 101; i++)
            {
                lane[k - 101 + i] *= _taper[i];
            }
        }

        CircularShiftInPlace(lane, i0 - ib);
        Fourier.Inverse(lane, FourierOptions.NoScaling);

        var scale = 1.0 / Math.Sqrt((double)Ft8Constants.LongFftLength * Ft8Constants.DownsampledFftLength);
        for (var i = 0; i < lane.Length; i++)
        {
            lane[i] *= scale;
        }

        return lane;
    }

    private static double[] BuildTaper()
    {
        var taper = new double[101];
        var pi = Math.PI;
        for (var i = 0; i <= 100; i++)
        {
            taper[i] = 0.5 * (1.0 + Math.Cos(i * pi / 100.0));
        }

        return taper;
    }

    private static void CircularShiftInPlace(Complex[] values, int shift)
    {
        if (values.Length == 0)
        {
            return;
        }

        shift %= values.Length;
        if (shift < 0)
        {
            shift += values.Length;
        }

        if (shift == 0)
        {
            return;
        }

        var copy = new Complex[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            copy[i] = values[(i + shift) % values.Length];
        }

        Array.Copy(copy, values, values.Length);
    }
}
