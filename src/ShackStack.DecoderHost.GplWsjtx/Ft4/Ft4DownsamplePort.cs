using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ShackStack.DecoderHost.GplWsjtx.Ft4;

internal sealed class Ft4DownsamplePort
{
    private readonly double[] _window = BuildWindow();
    private Complex[]? _spectrum;

    public void Prepare(float[] frameSamples)
    {
        if (frameSamples.Length != Ft4Constants.InputFrameSamples)
        {
            throw new ArgumentException($"Expected {Ft4Constants.InputFrameSamples} samples.", nameof(frameSamples));
        }

        var fft = new Complex[Ft4Constants.InputFrameSamples];
        for (var i = 0; i < frameSamples.Length; i++)
        {
            fft[i] = new Complex(frameSamples[i], 0.0);
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

        var df = (double)Ft4Constants.InputSampleRate / Ft4Constants.InputFrameSamples;
        var i0 = (int)Math.Round(f0 / df, MidpointRounding.AwayFromZero);
        var lane = new Complex[Ft4Constants.DownsampledLength];
        var half = lane.Length / 2;

        if (i0 >= 0 && i0 <= Ft4Constants.InputFrameSamples / 2)
        {
            lane[0] = _spectrum[i0];
        }

        for (var i = 1; i <= half; i++)
        {
            if (i0 + i >= 0 && i0 + i <= Ft4Constants.InputFrameSamples / 2)
            {
                lane[i] = _spectrum[i0 + i];
            }

            if (i0 - i >= 0 && i0 - i <= Ft4Constants.InputFrameSamples / 2)
            {
                lane[lane.Length - i] = _spectrum[i0 - i];
            }
        }

        for (var i = 0; i < lane.Length; i++)
        {
            lane[i] *= _window[i] / lane.Length;
        }

        Fourier.Inverse(lane, FourierOptions.NoScaling);
        return lane;
    }

    private static double[] BuildWindow()
    {
        var df = (double)Ft4Constants.InputSampleRate / Ft4Constants.InputFrameSamples;
        var bwTransition = 0.5 * Ft4Constants.Baud;
        var bwFlat = 4.0 * Ft4Constants.Baud;
        var iwt = Math.Max(1, (int)Math.Round(bwTransition / df, MidpointRounding.AwayFromZero));
        var iwf = Math.Max(1, (int)Math.Round(bwFlat / df, MidpointRounding.AwayFromZero));
        var window = new double[Ft4Constants.DownsampledLength];
        var pi = Math.PI;

        for (var i = 0; i < iwt && i < window.Length; i++)
        {
            window[i] = 0.5 * (1.0 + Math.Cos(pi * (iwt - 1 - i) / iwt));
        }

        for (var i = iwt; i < iwt + iwf && i < window.Length; i++)
        {
            window[i] = 1.0;
        }

        for (var i = 0; i < iwt; i++)
        {
            var index = iwt + iwf + i;
            if (index >= window.Length)
            {
                break;
            }

            window[index] = 0.5 * (1.0 + Math.Cos(pi * i / iwt));
        }

        var shift = Math.Max(1, (int)Math.Round(Ft4Constants.Baud / df, MidpointRounding.AwayFromZero));
        CircularShift(window, shift);
        return window;
    }

    private static void CircularShift(double[] values, int shift)
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

        var copy = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            copy[i] = values[(i + shift) % values.Length];
        }

        Array.Copy(copy, values, values.Length);
    }
}
