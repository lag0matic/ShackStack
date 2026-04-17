using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ShackStack.DecoderHost.GplWsjtx.Ft8;

internal sealed class Ft8SymbolMetricsPort
{
    private static readonly int[] GrayMap = [0, 1, 3, 2, 5, 6, 4, 7];
    private static readonly int[] Icos7 = [3, 1, 4, 0, 6, 5, 2];
    private static readonly bool[,] One = BuildBitMaskTable();

    public Ft8MetricResult? Extract(Complex[] cd0, int startOffset)
    {
        if (cd0.Length < Ft8Constants.UsefulDownsampledLength)
        {
            return new Ft8MetricResult(0, [], [], [], []);
        }

        var minStart = -(Ft8Constants.SymbolFftLength - 1);
        var maxStart = Ft8Constants.UsefulDownsampledLength - Ft8Constants.SymbolFftLength;
        if (startOffset < minStart || startOffset > maxStart)
        {
            return new Ft8MetricResult(0, [], [], [], []);
        }

        var cs = new Complex[8, Ft8Constants.ChannelSymbols];
        var s8 = new double[8, Ft8Constants.ChannelSymbols];

        for (var k = 0; k < Ft8Constants.ChannelSymbols; k++)
        {
            var i1 = startOffset + k * 32;
            var symbol = new Complex[32];
            if (i1 >= 0 && i1 + 31 <= Ft8Constants.UsefulDownsampledLength - 1)
            {
                Array.Copy(cd0, i1, symbol, 0, 32);
            }

            Fourier.Forward(symbol, FourierOptions.NoScaling);
            for (var tone = 0; tone < 8; tone++)
            {
                var value = symbol[tone] / 1_000.0;
                cs[tone, k] = value;
                s8[tone, k] = value.Magnitude;
            }
        }

        var nsync = ComputeHardSyncCount(s8);
        if (nsync <= 6)
        {
            return new Ft8MetricResult(nsync, [], [], [], []);
        }

        var bmeta = new double[174];
        var bmetb = new double[174];
        var bmetc = new double[174];
        var bmetd = new double[174];

        for (var nsym = 1; nsym <= 3; nsym++)
        {
            var nt = 1 << (3 * nsym);
            var s2 = new double[nt];
            for (var ihalf = 1; ihalf <= 2; ihalf++)
            {
                for (var k = 1; k <= 29; k += nsym)
                {
                    var ks = ihalf == 1 ? k + 7 : k + 43;
                    for (var i = 0; i < nt; i++)
                    {
                        var i1 = i / 64;
                        var i2 = (i & 63) / 8;
                        var i3 = i & 7;
                        s2[i] = nsym switch
                        {
                            1 => cs[GrayMap[i3], ks - 1].Magnitude,
                            2 => (cs[GrayMap[i2], ks - 1] + cs[GrayMap[i3], ks]).Magnitude,
                            3 => (cs[GrayMap[i1], ks - 1] + cs[GrayMap[i2], ks] + cs[GrayMap[i3], ks + 1]).Magnitude,
                            _ => 0.0,
                        };
                    }

                    var i32 = 1 + (k - 1) * 3 + (ihalf - 1) * 87;
                    var ibmax = nsym switch
                    {
                        1 => 2,
                        2 => 5,
                        3 => 8,
                        _ => 0,
                    };

                    for (var ib = 0; ib <= ibmax; ib++)
                    {
                        var maskIndex = ibmax - ib;
                        var on = MaxValue(s2, nt, maskIndex, true);
                        var off = MaxValue(s2, nt, maskIndex, false);
                        var bm = on - off;

                        var target = i32 + ib;
                        if (target <= 0 || target > 174)
                        {
                            continue;
                        }

                        var index = target - 1;
                        if (nsym == 1)
                        {
                            bmeta[index] = bm;
                            var den = Math.Max(on, off);
                            bmetd[index] = den > 0.0 ? bm / den : 0.0;
                        }
                        else if (nsym == 2)
                        {
                            bmetb[index] = bm;
                        }
                        else if (nsym == 3)
                        {
                            bmetc[index] = bm;
                        }
                    }
                }
            }
        }

        Normalize(bmeta);
        Normalize(bmetb);
        Normalize(bmetc);
        Normalize(bmetd);

        const double scaleFactor = 2.83;
        var llra = Scale(bmeta, scaleFactor);
        var llrb = Scale(bmetb, scaleFactor);
        var llrc = Scale(bmetc, scaleFactor);
        var llrd = Scale(bmetd, scaleFactor);

        return new Ft8MetricResult(nsync, llra, llrb, llrc, llrd);
    }

    private static int ComputeHardSyncCount(double[,] s8)
    {
        var is1 = 0;
        var is2 = 0;
        var is3 = 0;
        for (var k = 0; k < 7; k++)
        {
            if (Icos7[k] == MaxTone(s8, k)) is1++;
            if (Icos7[k] == MaxTone(s8, k + 36)) is2++;
            if (Icos7[k] == MaxTone(s8, k + 72)) is3++;
        }

        return is1 + is2 + is3;
    }

    private static int MaxTone(double[,] values, int symbolIndex)
    {
        var bestTone = 0;
        var bestValue = double.MinValue;
        for (var tone = 0; tone < 8; tone++)
        {
            var value = values[tone, symbolIndex];
            if (value > bestValue)
            {
                bestValue = value;
                bestTone = tone;
            }
        }

        return bestTone;
    }

    private static double MaxValue(double[] values, int length, int maskIndex, bool bitSet)
    {
        var best = double.MinValue;
        for (var i = 0; i < length; i++)
        {
            if (One[i, maskIndex] != bitSet)
            {
                continue;
            }

            if (values[i] > best)
            {
                best = values[i];
            }
        }

        return best == double.MinValue ? 0.0 : best;
    }

    private static void Normalize(double[] values)
    {
        if (values.Length == 0)
        {
            return;
        }

        var average = values.Average();
        var average2 = values.Select(v => v * v).Average();
        var variance = average2 - (average * average);
        var sigma = variance > 0.0 ? Math.Sqrt(variance) : Math.Sqrt(Math.Max(average2, 0.0));
        if (sigma <= 0.0)
        {
            return;
        }

        for (var i = 0; i < values.Length; i++)
        {
            values[i] /= sigma;
        }
    }

    private static double[] Scale(double[] values, double factor)
    {
        var scaled = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            scaled[i] = values[i] * factor;
        }

        return scaled;
    }

    private static bool[,] BuildBitMaskTable()
    {
        var table = new bool[512, 9];
        for (var i = 0; i < 512; i++)
        {
            for (var bit = 0; bit <= 8; bit++)
            {
                table[i, bit] = (i & (1 << bit)) != 0;
            }
        }

        return table;
    }
}

internal sealed record Ft8MetricResult(
    int HardSyncCount,
    double[] Llra,
    double[] Llrb,
    double[] Llrc,
    double[] Llrd);
