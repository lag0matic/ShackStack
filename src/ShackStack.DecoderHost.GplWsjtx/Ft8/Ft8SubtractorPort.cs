using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ShackStack.DecoderHost.GplWsjtx.Ft8;

internal sealed class Ft8SubtractorPort
{
    private const int Nfft = Ft8Constants.InputSamplesPerCycle;
    private const int Nfilt = 4000;
    private readonly Complex[] _filterSpectrum = BuildFilterSpectrum();

    public void SubtractInPlace(float[] samples, int[] tones, double f0Hz, double dtSeconds)
    {
        if (samples.Length != Ft8Constants.InputSamplesPerCycle || tones.Length != Ft8Constants.ChannelSymbols)
        {
            return;
        }

        var cref = Ft8ReferenceSignalPort.GenerateReference(tones, f0Hz);
        if (cref.Length == 0)
        {
            return;
        }

        var nstart = (int)Math.Round(dtSeconds * Ft8Constants.InputSampleRate, MidpointRounding.AwayFromZero);
        var camp = new Complex[Nfft];
        for (var i = 0; i < cref.Length; i++)
        {
            var j = nstart + i;
            if (j < 0 || j >= samples.Length)
            {
                continue;
            }

            camp[i] = samples[j] * Complex.Conjugate(cref[i]);
        }

        Fourier.Forward(camp, FourierOptions.NoScaling);
        for (var i = 0; i < camp.Length; i++)
        {
            camp[i] *= _filterSpectrum[i];
        }

        Fourier.Inverse(camp, FourierOptions.NoScaling);

        for (var i = 0; i < cref.Length; i++)
        {
            var j = nstart + i;
            if (j < 0 || j >= samples.Length)
            {
                continue;
            }

            var z = camp[i] * cref[i];
            samples[j] -= (float)(2.0 * z.Real);
        }
    }

    private static Complex[] BuildFilterSpectrum()
    {
        var cw = new Complex[Nfft];
        var window = new double[Nfilt + 1];
        var sum = 0.0;
        var pi = Math.PI;

        for (var j = -Nfilt / 2; j <= Nfilt / 2; j++)
        {
            var value = Math.Cos(pi * j / Nfilt);
            value *= value;
            window[j + (Nfilt / 2)] = value;
            sum += value;
        }

        for (var i = 0; i < window.Length; i++)
        {
            cw[i] = window[i] / sum;
        }

        CircularShiftInPlace(cw, Nfilt / 2 + 1);
        Fourier.Forward(cw, FourierOptions.NoScaling);

        var scale = 1.0 / Nfft;
        for (var i = 0; i < cw.Length; i++)
        {
            cw[i] *= scale;
        }

        return cw;
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
