using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ShackStack.DecoderHost.GplWsjtx.Ft4;

internal sealed class Ft4BitMetricsPort
{
    private static readonly int[] Icos4A = [0, 1, 3, 2];
    private static readonly int[] Icos4B = [1, 0, 2, 3];
    private static readonly int[] Icos4C = [2, 3, 1, 0];
    private static readonly int[] Icos4D = [3, 2, 0, 1];
    private static readonly int[] SyncBits1 = [0, 0, 0, 1, 1, 0, 1, 1];
    private static readonly int[] SyncBits2 = [0, 1, 0, 0, 1, 1, 1, 0];
    private static readonly int[] SyncBits3 = [1, 1, 1, 0, 0, 1, 0, 0];
    private static readonly int[] SyncBits4 = [1, 0, 1, 1, 0, 0, 0, 1];
    private static readonly int[] GrayMap = [0, 1, 3, 2];
    private static readonly bool[,] One = BuildBitMaskTable();

    public Ft4MetricResult? Extract(Complex[] cd)
    {
        var required = Ft4Constants.ChannelSymbols * Ft4Constants.DownsampledSamplesPerSymbol;
        if (cd.Length < required)
        {
            return null;
        }

        var cs = new Complex[4, Ft4Constants.ChannelSymbols];
        var s4 = new double[4, Ft4Constants.ChannelSymbols];
        var symbol = new Complex[Ft4Constants.DownsampledSamplesPerSymbol];

        for (var k = 0; k < Ft4Constants.ChannelSymbols; k++)
        {
            Array.Clear(symbol);
            var source = k * Ft4Constants.DownsampledSamplesPerSymbol;
            Array.Copy(cd, source, symbol, 0, Ft4Constants.DownsampledSamplesPerSymbol);
            Fourier.Forward(symbol, FourierOptions.NoScaling);
            for (var tone = 0; tone < 4; tone++)
            {
                var value = symbol[tone];
                cs[tone, k] = value;
                s4[tone, k] = value.Magnitude;
            }
        }

        var nsync = ComputeHardSyncCount(s4);
        if (nsync < 8)
        {
            return new Ft4MetricResult(nsync, 0, [], [], []);
        }

        var bitMetrics = new double[2 * Ft4Constants.ChannelSymbols, 3];

        for (var nseq = 1; nseq <= 3; nseq++)
        {
            var nsym = nseq switch
            {
                1 => 1,
                2 => 2,
                _ => 4,
            };

            var nt = 1 << (2 * nsym);
            var s2 = new double[nt];

            for (var ks = 0; ks <= Ft4Constants.ChannelSymbols - nsym; ks += nsym)
            {
                for (var i = 0; i < nt; i++)
                {
                    var i1 = i / 64;
                    var i2 = (i & 63) / 16;
                    var i3 = (i & 15) / 4;
                    var i4 = i & 3;
                    s2[i] = nsym switch
                    {
                        1 => cs[GrayMap[i4], ks].Magnitude,
                        2 => (cs[GrayMap[i3], ks] + cs[GrayMap[i4], ks + 1]).Magnitude,
                        4 => (cs[GrayMap[i1], ks] + cs[GrayMap[i2], ks + 1] + cs[GrayMap[i3], ks + 2] + cs[GrayMap[i4], ks + 3]).Magnitude,
                        _ => 0.0,
                    };
                }

                var ipt = 1 + (ks * 2);
                var ibmax = nsym switch
                {
                    1 => 1,
                    2 => 3,
                    _ => 7,
                };

                for (var ib = 0; ib <= ibmax; ib++)
                {
                    var on = MaxValue(s2, nt, ibmax - ib, true);
                    var off = MaxValue(s2, nt, ibmax - ib, false);
                    var bm = on - off;
                    if (ipt + ib > 2 * Ft4Constants.ChannelSymbols)
                    {
                        continue;
                    }

                    bitMetrics[ipt + ib - 1, nseq - 1] = bm;
                }
            }
        }

        bitMetrics[204, 1] = bitMetrics[204, 0];
        bitMetrics[205, 1] = bitMetrics[205, 0];
        bitMetrics[200, 2] = bitMetrics[200, 1];
        bitMetrics[201, 2] = bitMetrics[201, 1];
        bitMetrics[202, 2] = bitMetrics[202, 1];
        bitMetrics[203, 2] = bitMetrics[203, 1];
        bitMetrics[204, 2] = bitMetrics[204, 0];
        bitMetrics[205, 2] = bitMetrics[205, 0];

        var column1 = GetColumn(bitMetrics, 0);
        var column2 = GetColumn(bitMetrics, 1);
        var column3 = GetColumn(bitMetrics, 2);

        var hardBits = new int[column1.Length];
        for (var i = 0; i < column1.Length; i++)
        {
            hardBits[i] = column1[i] >= 0.0 ? 1 : 0;
        }

        var syncQualityCount =
            CountMatches(hardBits, 0, SyncBits1) +
            CountMatches(hardBits, 66, SyncBits2) +
            CountMatches(hardBits, 132, SyncBits3) +
            CountMatches(hardBits, 198, SyncBits4);

        if (syncQualityCount < 20)
        {
            return new Ft4MetricResult(nsync, syncQualityCount, [], [], []);
        }

        Normalize(column1);
        Normalize(column2);
        Normalize(column3);

        const double scaleFactor = 2.83;
        var llra = new double[174];
        var llrb = new double[174];
        var llrc = new double[174];

        Array.Copy(column1, 8, llra, 0, 58);
        Array.Copy(column1, 74, llra, 58, 58);
        Array.Copy(column1, 140, llra, 116, 58);

        Array.Copy(column2, 8, llrb, 0, 58);
        Array.Copy(column2, 74, llrb, 58, 58);
        Array.Copy(column2, 140, llrb, 116, 58);

        Array.Copy(column3, 8, llrc, 0, 58);
        Array.Copy(column3, 74, llrc, 58, 58);
        Array.Copy(column3, 140, llrc, 116, 58);

        ScaleInPlace(llra, scaleFactor);
        ScaleInPlace(llrb, scaleFactor);
        ScaleInPlace(llrc, scaleFactor);

        return new Ft4MetricResult(nsync, syncQualityCount, llra, llrb, llrc);
    }

    private static int ComputeHardSyncCount(double[,] s4)
    {
        var is1 = 0;
        var is2 = 0;
        var is3 = 0;
        var is4 = 0;
        for (var k = 0; k < 4; k++)
        {
            if (Icos4A[k] == MaxTone(s4, k)) is1++;
            if (Icos4B[k] == MaxTone(s4, k + 33)) is2++;
            if (Icos4C[k] == MaxTone(s4, k + 66)) is3++;
            if (Icos4D[k] == MaxTone(s4, k + 99)) is4++;
        }

        return is1 + is2 + is3 + is4;
    }

    private static int MaxTone(double[,] values, int symbolIndex)
    {
        var bestTone = 0;
        var bestValue = double.MinValue;
        for (var tone = 0; tone < 4; tone++)
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

    private static double[] GetColumn(double[,] values, int column)
    {
        var result = new double[values.GetLength(0)];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = values[i, column];
        }

        return result;
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

    private static void ScaleInPlace(double[] values, double factor)
    {
        for (var i = 0; i < values.Length; i++)
        {
            values[i] *= factor;
        }
    }

    private static bool[,] BuildBitMaskTable()
    {
        var table = new bool[256, 8];
        for (var i = 0; i < 256; i++)
        {
            for (var bit = 0; bit < 8; bit++)
            {
                table[i, bit] = (i & (1 << bit)) != 0;
            }
        }

        return table;
    }

    private static int CountMatches(int[] values, int offset, int[] pattern)
    {
        var matches = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (offset + i >= values.Length)
            {
                break;
            }

            if (values[offset + i] == pattern[i])
            {
                matches++;
            }
        }

        return matches;
    }
}

internal sealed record Ft4MetricResult(
    int HardSyncCount,
    int SyncQualityCount,
    double[] Llra,
    double[] Llrb,
    double[] Llrc);
