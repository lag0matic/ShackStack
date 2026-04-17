using System.Numerics;

namespace ShackStack.DecoderHost.GplWsjtx.Ft8;

internal static class Ft8ReferenceSignalPort
{
    public static Complex[] GenerateReference(int[] tones, double f0Hz)
    {
        if (tones.Length == 0)
        {
            return [];
        }

        var nsym = tones.Length;
        var nsps = Ft8Constants.SamplesPerSymbol;
        var dt = 1.0 / Ft8Constants.InputSampleRate;
        var twopi = 2.0 * Math.PI;
        var length = nsym * nsps;
        var cref = new Complex[length];
        var phi = 0.0;
        var k = 0;

        for (var i = 0; i < nsym; i++)
        {
            var dphi = twopi * ((f0Hz * dt) + (tones[i] / (double)nsps));
            for (var s = 0; s < nsps; s++)
            {
                cref[k++] = new Complex(Math.Cos(phi), Math.Sin(phi));
                phi = (phi + dphi) % twopi;
            }
        }

        return cref;
    }
}
