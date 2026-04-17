using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ShackStack.DecoderHost.GplWsjtx.Ft4;

internal static class Ft4SnrEstimatorPort
{
    public static int EstimateDb(Complex[] cd, int[] tones)
    {
        if (tones.Length < Ft4Constants.ChannelSymbols || cd.Length < Ft4Constants.ChannelSymbols * Ft4Constants.DownsampledSamplesPerSymbol)
        {
            return -21;
        }

        var symbol = new Complex[Ft4Constants.DownsampledSamplesPerSymbol];
        double xsig = 0.0;
        double xnoi = 0.0;

        for (var i = 0; i < Ft4Constants.ChannelSymbols; i++)
        {
            Array.Clear(symbol);
            var source = i * Ft4Constants.DownsampledSamplesPerSymbol;
            Array.Copy(cd, source, symbol, 0, Ft4Constants.DownsampledSamplesPerSymbol);
            Fourier.Forward(symbol, FourierOptions.NoScaling);

            var tone = Math.Clamp(tones[i], 0, 3);
            var signal = symbol[tone].Magnitude;
            var noise = 0.0;
            for (var bin = 0; bin < 4; bin++)
            {
                if (bin == tone)
                {
                    continue;
                }

                noise += symbol[bin].Magnitude * symbol[bin].Magnitude;
            }

            xsig += signal * signal;
            xnoi += noise / 3.0;
        }

        double xsnr;
        if (xnoi > 0.0)
        {
            var arg = (xsig / xnoi) - 1.0;
            xsnr = arg > 0.0 ? (10.0 * Math.Log10(arg)) - 14.8 : -21.0;
        }
        else
        {
            xsnr = -21.0;
        }

        if (xsnr < -21.0)
        {
            xsnr = -21.0;
        }

        return (int)Math.Round(xsnr, MidpointRounding.AwayFromZero);
    }
}
