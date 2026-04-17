namespace ShackStack.DecoderHost.GplWsjtx.Ft8;

internal sealed class Ft8BpDecoderPort
{
    private const int N = 174;
    private const int K = 91;
    private const int M = N - K;

    public Ft8BpDecodeResult Decode(double[] llr, int maxIterations = 30, int[]? apmask = null, int maxOsdSnapshots = 0)
    {
        if (llr.Length != N)
        {
            return new Ft8BpDecodeResult(false, false, -1, 0, -1, null, []);
        }

        var toc = new double[7, M];
        var tov = new double[Ft8LdpcParity.Ncw, N];
        var tanhToc = new double[7, M];
        var zn = new double[N];
        var zsum = new double[N];
        var cw = new int[N];
        var synd = new int[M];
        var osdSnapshots = new List<double[]>();

        for (var j = 0; j < M; j++)
        {
            for (var i = 0; i < Ft8LdpcParity.Nrw[j]; i++)
            {
                var bitIndex = Ft8LdpcParity.GetNm(i, j) - 1;
                if (!IsValidBitIndex(bitIndex))
                {
                    return new Ft8BpDecodeResult(false, false, -1, 0, -1, null, []);
                }

                toc[i, j] = llr[bitIndex];
            }
        }

        var ncnt = 0;
        var nclast = 0;

        for (var iter = 0; iter <= maxIterations; iter++)
        {
            for (var i = 0; i < N; i++)
            {
                var isApMasked = apmask is not null && i < apmask.Length && apmask[i] == 1;
                if (isApMasked)
                {
                    zn[i] = llr[i];
                    continue;
                }

                zn[i] = llr[i];
                for (var kk = 0; kk < Ft8LdpcParity.Ncw; kk++)
                {
                    if (Ft8LdpcParity.Mn[kk, i] > 0)
                    {
                        zn[i] += tov[kk, i];
                    }
                }
            }

            for (var i = 0; i < N; i++)
            {
                zsum[i] += zn[i];
            }

            if (iter > 0 && iter <= maxOsdSnapshots)
            {
                osdSnapshots.Add((double[])zsum.Clone());
            }

            Array.Clear(cw);
            for (var i = 0; i < N; i++)
            {
                if (zn[i] > 0.0)
                {
                    cw[i] = 1;
                }
            }

            var ncheck = 0;
            for (var i = 0; i < M; i++)
            {
                var sum = 0;
                for (var j = 0; j < Ft8LdpcParity.Nrw[i]; j++)
                {
                    var bitIndex = Ft8LdpcParity.GetNm(j, i) - 1;
                    if (!IsValidBitIndex(bitIndex))
                    {
                        return new Ft8BpDecodeResult(false, false, -1, iter, -1, null, osdSnapshots);
                    }

                    sum += cw[bitIndex];
                }

                synd[i] = sum;
                if ((sum & 1) != 0)
                {
                    ncheck++;
                }
            }

            if (ncheck == 0)
                {
                    var decoded91 = new int[K];
                    Array.Copy(cw, decoded91, K);
                    var crcOk = Ft8CrcPort.CheckCrc14(decoded91);
                var hardErrors = 0;
                for (var i = 0; i < N; i++)
                {
                    if (((2 * cw[i] - 1) * llr[i]) < 0.0)
                    {
                        hardErrors++;
                        }
                    }

                return new Ft8BpDecodeResult(true, crcOk, ncheck, iter, hardErrors, decoded91, osdSnapshots);
            }

            if (iter > 0)
            {
                var nd = ncheck - nclast;
                if (nd < 0)
                {
                    ncnt = 0;
                }
                else
                {
                    ncnt++;
                }

                if (ncnt >= 5 && iter >= 10 && ncheck > 15)
                {
                    return new Ft8BpDecodeResult(false, false, ncheck, iter, -1, null, osdSnapshots);
                }
            }

            nclast = ncheck;

            for (var j = 0; j < M; j++)
            {
                for (var i = 0; i < Ft8LdpcParity.Nrw[j]; i++)
                {
                    var bitIndex = Ft8LdpcParity.Nm[i, j];
                    if (bitIndex <= 0)
                    {
                        toc[i, j] = 0.0;
                        continue;
                    }

                    var ibj = bitIndex - 1;
                    var value = zn[ibj];
                    for (var kk = 0; kk < Ft8LdpcParity.Ncw; kk++)
                    {
                        var checkIndex = Ft8LdpcParity.GetMn(kk, ibj);
                        if (checkIndex == j + 1)
                        {
                            value -= tov[kk, ibj];
                        }
                }

                    toc[i, j] = value;
                }
            }

            for (var i = 0; i < M; i++)
            {
                for (var j = 0; j < Ft8LdpcParity.Nrw[i]; j++)
                {
                    tanhToc[j, i] = Math.Tanh(-toc[j, i] / 2.0);
                }
            }

            for (var j = 0; j < N; j++)
            {
                for (var i = 0; i < Ft8LdpcParity.Ncw; i++)
                {
                    var checkIndex = Ft8LdpcParity.GetMn(i, j);
                    if (checkIndex <= 0)
                    {
                        tov[i, j] = 0.0;
                        continue;
                    }

                    var ichk = checkIndex - 1;
                    if (!IsValidCheckIndex(ichk))
                    {
                        tov[i, j] = 0.0;
                        continue;
                    }

                    var product = 1.0;
                    for (var k = 0; k < Ft8LdpcParity.Nrw[ichk]; k++)
                    {
                        var neighborBit = Ft8LdpcParity.GetNm(k, ichk);
                        if (neighborBit <= 0)
                        {
                            continue;
                        }

                        if (neighborBit == j + 1)
                        {
                            continue;
                        }

                        product *= tanhToc[k, ichk];
                    }

                    tov[i, j] = 2.0 * PlateauAtanh(-product);
                }
            }
        }

        return new Ft8BpDecodeResult(false, false, -1, maxIterations, -1, null, osdSnapshots);
    }

    private static bool IsValidBitIndex(int bitIndex) => bitIndex >= 0 && bitIndex < N;

    private static bool IsValidCheckIndex(int checkIndex) => checkIndex >= 0 && checkIndex < M;

    private static double PlateauAtanh(double x)
    {
        var sign = x < 0 ? -1.0 : 1.0;
        var z = Math.Abs(x);
        if (z <= 0.664) return x / 0.83;
        if (z <= 0.9217) return sign * ((z - 0.4064) / 0.322);
        if (z <= 0.9951) return sign * ((z - 0.8378) / 0.0524);
        if (z <= 0.9998) return sign * ((z - 0.9914) / 0.0012);
        return sign * 7.0;
    }
}

internal sealed record Ft8BpDecodeResult(
    bool HasCodeword,
    bool CrcOk,
    int UnsatisfiedParityChecks,
    int Iterations,
    int HardErrors,
    int[]? Decoded91,
    IReadOnlyList<double[]> OsdSnapshots);
