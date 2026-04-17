using System.Numerics;

namespace ShackStack.DecoderHost.GplWsjtx.Ft8;

internal sealed class Ft8SyncPort
{
    private static readonly int[] Icos7 = [3, 1, 4, 0, 6, 5, 2];
    private readonly Complex[,] _csync = BuildSyncTable();

    public double Compute(Complex[] cd0, int i0, Complex[]? tweak)
    {
        double sync = 0;
        for (var i = 0; i < 7; i++)
        {
            var i1 = i0 + i * 32;
            var i2 = i1 + 36 * 32;
            var i3 = i1 + 72 * 32;

            sync += SumWindow(cd0, i1, i, tweak);
            sync += SumWindow(cd0, i2, i, tweak);
            sync += SumWindow(cd0, i3, i, tweak);
        }

        return sync;
    }

    public Complex[] BuildFrequencyTweak(double delfHz)
    {
        var tweak = new Complex[32];
        var dphi = 2.0 * Math.PI * delfHz / Ft8Constants.DownsampledSampleRate;
        var phi = 0.0;
        for (var i = 0; i < tweak.Length; i++)
        {
            tweak[i] = new Complex(Math.Cos(phi), Math.Sin(phi));
            phi = (phi + dphi) % (2.0 * Math.PI);
        }

        return tweak;
    }

    private double SumWindow(Complex[] cd0, int start, int syncIndex, Complex[]? tweak)
    {
        if (start < 0 || start + 31 > Ft8Constants.UsefulDownsampledLength - 1)
        {
            return 0;
        }

        Complex sum = Complex.Zero;
        for (var j = 0; j < 32; j++)
        {
            var sync = _csync[syncIndex, j];
            if (tweak is not null)
            {
                sync *= tweak[j];
            }

            sum += cd0[start + j] * Complex.Conjugate(sync);
        }

        return sum.Real * sum.Real + sum.Imaginary * sum.Imaginary;
    }

    private static Complex[,] BuildSyncTable()
    {
        var table = new Complex[7, 32];
        for (var i = 0; i < 7; i++)
        {
            var phi = 0.0;
            var dphi = 2.0 * Math.PI * Icos7[i] / 32.0;
            for (var j = 0; j < 32; j++)
            {
                table[i, j] = new Complex(Math.Cos(phi), Math.Sin(phi));
                phi = (phi + dphi) % (2.0 * Math.PI);
            }
        }

        return table;
    }
}
