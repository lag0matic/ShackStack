using System.Numerics;

namespace ShackStack.DecoderHost.GplWsjtx.Ft4;

internal sealed class Ft4SyncPort
{
    private static readonly int[] Icos4A = [0, 1, 3, 2];
    private static readonly int[] Icos4B = [1, 0, 2, 3];
    private static readonly int[] Icos4C = [2, 3, 1, 0];
    private static readonly int[] Icos4D = [3, 2, 0, 1];
    private readonly Complex[] _syncA = BuildSync(Icos4A);
    private readonly Complex[] _syncB = BuildSync(Icos4B);
    private readonly Complex[] _syncC = BuildSync(Icos4C);
    private readonly Complex[] _syncD = BuildSync(Icos4D);

    public double Compute(Complex[] cd0, int i0, Complex[]? tweak)
    {
        var nss = Ft4Constants.DownsampledSamplesPerSymbol;
        var i1 = i0;
        var i2 = i0 + 33 * nss;
        var i3 = i0 + 66 * nss;
        var i4 = i0 + 99 * nss;

        var z1 = SumWindow(cd0, i1, _syncA, tweak);
        var z2 = SumWindow(cd0, i2, _syncB, tweak);
        var z3 = SumWindow(cd0, i3, _syncC, tweak);
        var z4 = SumWindow(cd0, i4, _syncD, tweak);
        var fac = 1.0 / (2.0 * nss);

        return Magnitude(z1 * fac) + Magnitude(z2 * fac) + Magnitude(z3 * fac) + Magnitude(z4 * fac);
    }

    public Complex[] BuildFrequencyTweak(double delfHz)
    {
        var length = 2 * Ft4Constants.DownsampledSamplesPerSymbol;
        var tweak = new Complex[length];
        var effectiveSampleRate = Ft4Constants.DownsampledSampleRate / 2.0;
        var dphi = 2.0 * Math.PI * delfHz / effectiveSampleRate;
        var phi = 0.0;
        for (var i = 0; i < length; i++)
        {
            tweak[i] = new Complex(Math.Cos(phi), Math.Sin(phi));
            phi = (phi + dphi) % (2.0 * Math.PI);
        }

        return tweak;
    }

    private static Complex[] BuildSync(int[] costas)
    {
        var nss = Ft4Constants.DownsampledSamplesPerSymbol;
        var sync = new Complex[2 * nss];
        var phi = 0.0;
        var k = 0;
        for (var i = 0; i < costas.Length; i++)
        {
            var dphi = 2.0 * Math.PI * costas[i] / nss;
            for (var j = 0; j < nss / 2 && k < sync.Length; j++)
            {
                sync[k++] = new Complex(Math.Cos(phi), Math.Sin(phi));
                phi = (phi + dphi) % (2.0 * Math.PI);
            }
        }

        return sync;
    }

    private static Complex SumWindow(Complex[] cd0, int start, Complex[] sync, Complex[]? tweak)
    {
        Complex sum = Complex.Zero;
        var maxSource = cd0.Length - 1;

        for (var i = 0; i < sync.Length; i++)
        {
            var sourceIndex = start + (2 * i);
            if (sourceIndex < 0 || sourceIndex > maxSource)
            {
                continue;
            }

            var syncValue = tweak is null ? sync[i] : tweak[i] * sync[i];
            sum += cd0[sourceIndex] * Complex.Conjugate(syncValue);
        }

        return sum;
    }

    private static double Magnitude(Complex value) => Math.Sqrt((value.Real * value.Real) + (value.Imaginary * value.Imaginary));
}
