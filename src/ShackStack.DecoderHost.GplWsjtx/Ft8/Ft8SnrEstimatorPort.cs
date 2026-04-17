using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ShackStack.DecoderHost.GplWsjtx.Ft8;

internal static class Ft8SnrEstimatorPort
{
    public static int EstimateDb(Complex[] lane, int startOffset, int[] tones)
    {
        if (tones.Length < Ft8Constants.ChannelSymbols)
        {
            return -24;
        }

        var samplesPerSymbol = Ft8Constants.SamplesPerSymbol / Ft8Constants.DownsampleFactor;
        var symbol = new Complex[samplesPerSymbol];
        double xsig = 0.0;
        double xnoi = 0.0;

        for (var i = 0; i < Ft8Constants.ChannelSymbols; i++)
        {
            Array.Clear(symbol);
            var source = startOffset + (i * samplesPerSymbol);
            if (source < 0 || source + samplesPerSymbol > lane.Length)
            {
                continue;
            }

            Array.Copy(lane, source, symbol, 0, samplesPerSymbol);
            Fourier.Forward(symbol, FourierOptions.NoScaling);

            var tone = Math.Clamp(tones[i], 0, 7);
            var noiseTone = (tone + 4) % 8;
            var signal = symbol[tone].Magnitude;
            var noise = symbol[noiseTone].Magnitude;
            xsig += signal * signal;
            xnoi += noise * noise;
        }

        var arg = xnoi > 0.0 ? (xsig / xnoi) - 1.0 : 0.001;
        if (arg <= 0.1)
        {
            arg = 0.001;
        }

        var xsnr = (10.0 * Math.Log10(arg)) - 27.0;
        if (xsnr < -24.0)
        {
            xsnr = -24.0;
        }

        return (int)Math.Round(xsnr, MidpointRounding.AwayFromZero);
    }
}
