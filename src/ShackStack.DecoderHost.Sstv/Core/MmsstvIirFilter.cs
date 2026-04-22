namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Source-shaped port of MMSSTV's CIIR helper with the shared MakeIIR
/// coefficient generator.
/// </summary>
internal sealed class MmsstvIirFilter
{
    private const int IirMax = 16;
    private readonly double[] _a = new double[IirMax * 3];
    private readonly double[] _b = new double[IirMax * 2];
    private readonly double[] _z = new double[IirMax * 2];

    public int Order { get; private set; }
    public int ButterworthOrChebyshev { get; private set; }
    public double Ripple { get; private set; }

    public void MakeIir(double cutoffHz, double sampleRate, int order, int bc, double ripple)
    {
        Order = order;
        ButterworthOrChebyshev = bc;
        Ripple = ripple;
        MakeIir(_a, _b, cutoffHz, sampleRate, order, bc, ripple);
    }

    public double Process(double sample)
    {
        var d = sample;
        var aIndex = 0;
        var bIndex = 0;
        var zIndex = 0;

        for (var i = 0; i < Order / 2; i++, aIndex += 3, bIndex += 2, zIndex += 2)
        {
            d += (_z[zIndex] * _a[aIndex + 1]) + (_z[zIndex + 1] * _a[aIndex + 2]);
            var output = (d * _b[bIndex]) + (_z[zIndex] * _b[bIndex + 1]) + (_z[zIndex + 1] * _b[bIndex]);
            _z[zIndex + 1] = _z[zIndex];
            if (Math.Abs(d) < 1e-37)
            {
                d = 0.0;
            }

            _z[zIndex] = d;
            d = output;
        }

        if ((Order & 1) != 0)
        {
            d += _z[zIndex] * _a[aIndex + 1];
            var output = (d * _b[bIndex]) + (_z[zIndex] * _b[bIndex]);
            if (Math.Abs(d) < 1e-37)
            {
                d = 0.0;
            }

            _z[zIndex] = d;
            d = output;
        }

        return d;
    }

    public void Clear()
    {
        Array.Clear(_z);
    }

    private static void MakeIir(double[] a, double[] b, double cutoffHz, double sampleRate, int order, int bc, double ripple)
    {
        double u = 0.0;
        if (bc != 0)
        {
            u = Math.Asinh(1.0 / Math.Sqrt(Math.Pow(10.0, 0.1 * ripple) - 1.0)) / order;
        }

        var wa = Math.Tan(Math.PI * cutoffHz / sampleRate);
        var w0 = 1.0;
        var n = (order & 1) + 1;
        var aIndex = 0;
        var bIndex = 0;

        for (var j = 1; j <= order / 2; j++, aIndex += 3, bIndex += 2)
        {
            double zt;
            if (bc != 0)
            {
                var d1 = Math.Sinh(u) * Math.Cos(n * Math.PI / (2.0 * order));
                var d2 = Math.Cosh(u) * Math.Sin(n * Math.PI / (2.0 * order));
                w0 = Math.Sqrt((d1 * d1) + (d2 * d2));
                zt = Math.Sinh(u) * Math.Cos(n * Math.PI / (2.0 * order)) / w0;
            }
            else
            {
                w0 = 1.0;
                zt = Math.Cos(n * Math.PI / (2.0 * order));
            }

            a[aIndex + 0] = 1 + (wa * w0 * 2 * zt) + (wa * w0 * wa * w0);
            a[aIndex + 1] = -2 * (((wa * w0 * wa * w0) - 1) / a[aIndex + 0]);
            a[aIndex + 2] = -((1.0 - (wa * w0 * 2 * zt) + (wa * w0 * wa * w0)) / a[aIndex + 0]);
            b[bIndex + 0] = (wa * w0 * wa * w0) / a[aIndex + 0];
            b[bIndex + 1] = 2 * b[bIndex + 0];
            n += 2;
        }

        if (bc != 0 && (order & 1) == 0)
        {
            var x = Math.Pow(1.0 / Math.Pow(10.0, ripple / 20.0), 1.0 / (order / 2.0));
            for (var j = 1; j <= order / 2; j++)
            {
                var index = (j - 1) * 2;
                b[index + 0] *= x;
                b[index + 1] *= x;
            }
        }

        if ((order & 1) != 0)
        {
            if (bc != 0)
            {
                w0 = Math.Sinh(u);
            }

            var j = order / 2;
            aIndex = j * 3;
            bIndex = j * 2;
            a[aIndex + 0] = 1 + (wa * w0);
            a[aIndex + 1] = -((wa * w0 - 1) / a[aIndex + 0]);
            b[bIndex + 0] = (wa * w0) / a[aIndex + 0];
            b[bIndex + 1] = b[bIndex + 0];
        }
    }
}
