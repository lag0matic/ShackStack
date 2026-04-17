namespace ShackStack.DecoderHost.GplWsjtx.Ft8;

internal sealed class Ft8OsdDecoderPort
{
    private const int N = 174;
    private const int K = 91;
    private static readonly int[,] Generator = Encode174_91Port.GetSystematicGenerator();

    public Ft8OsdDecodeResult Decode(double[] llr, int[]? apmask = null, int order = 1)
    {
        if (llr.Length != N)
        {
            return new Ft8OsdDecodeResult(false, 0, double.PositiveInfinity, null);
        }

        var work = PrepareMrb(llr, apmask);
        if (!work.IsValid)
        {
            return new Ft8OsdDecodeResult(false, 0, double.PositiveInfinity, null);
        }

        Ft8OsdDecodeResult best = EvaluatePattern(work, work.M0, 0);
        if (best.CrcOk)
        {
            return best;
        }

        if (order >= 1)
        {
            for (var i = K - 1; i >= 0; i--)
            {
                if (work.ApMaskPermuted[i] == 1)
                {
                    continue;
                }

                var trial = (int[])work.M0.Clone();
                trial[i] ^= 1;
                var candidate = EvaluatePattern(work, trial, 1);
                if (candidate.CrcOk && candidate.Distance < best.Distance)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    private static Ft8OsdDecodeResult EvaluatePattern(Ft8OsdWork work, int[] message, int order)
    {
        var encodedPermuted = EncodeMessage(message, work.GeneratorPermuted);
        var distance = 0.0;
        for (var i = 0; i < N; i++)
        {
            if (encodedPermuted[i] != work.HardDecisionPermuted[i])
            {
                distance += work.ReliabilityPermuted[i];
            }
        }

        var codeword = new int[N];
        for (var i = 0; i < N; i++)
        {
            codeword[work.Indices[i]] = encodedPermuted[i];
        }

        var decoded91 = new int[K];
        Array.Copy(codeword, decoded91, K);
        var crcOk = Ft8CrcPort.CheckCrc14(decoded91);
        return new Ft8OsdDecodeResult(crcOk, order, distance, crcOk ? decoded91 : null);
    }

    private static int[] EncodeMessage(int[] message, int[,] generatorPermuted)
    {
        var codeword = new int[N];
        for (var row = 0; row < K; row++)
        {
            if (message[row] == 0)
            {
                continue;
            }

            for (var col = 0; col < N; col++)
            {
                codeword[col] ^= generatorPermuted[row, col];
            }
        }

        return codeword;
    }

    private static Ft8OsdWork PrepareMrb(double[] llr, int[]? apmask)
    {
        var indices = Enumerable.Range(0, N)
            .OrderByDescending(i => Math.Abs(llr[i]))
            .ToArray();

        var generatorPermuted = new int[K, N];
        for (var row = 0; row < K; row++)
        {
            for (var col = 0; col < N; col++)
            {
                generatorPermuted[row, col] = Generator[row, indices[col]];
            }
        }

        var hardDecisionPermuted = indices.Select(i => llr[i] >= 0.0 ? 1 : 0).ToArray();
        var reliabilityPermuted = indices.Select(i => Math.Abs(llr[i])).ToArray();
        var apMaskPermuted = indices.Select(i => apmask is not null && i < apmask.Length ? apmask[i] : 0).ToArray();

        if (!GaussianEliminate(generatorPermuted, indices, hardDecisionPermuted, reliabilityPermuted, apMaskPermuted))
        {
            return Ft8OsdWork.Invalid;
        }

        var m0 = new int[K];
        Array.Copy(hardDecisionPermuted, m0, K);
        return new Ft8OsdWork(true, generatorPermuted, indices, hardDecisionPermuted, reliabilityPermuted, apMaskPermuted, m0);
    }

    private static bool GaussianEliminate(int[,] matrix, int[] indices, int[] hardDecisionPermuted, double[] reliabilityPermuted, int[] apMaskPermuted)
    {
        for (var pivot = 0; pivot < K; pivot++)
        {
            var pivotColumn = -1;
            for (var col = pivot; col < N; col++)
            {
                if (matrix[pivot, col] == 1)
                {
                    pivotColumn = col;
                    break;
                }
            }

            if (pivotColumn < 0)
            {
                return false;
            }

            if (pivotColumn != pivot)
            {
                SwapColumns(matrix, pivot, pivotColumn);
                (indices[pivot], indices[pivotColumn]) = (indices[pivotColumn], indices[pivot]);
                (hardDecisionPermuted[pivot], hardDecisionPermuted[pivotColumn]) = (hardDecisionPermuted[pivotColumn], hardDecisionPermuted[pivot]);
                (reliabilityPermuted[pivot], reliabilityPermuted[pivotColumn]) = (reliabilityPermuted[pivotColumn], reliabilityPermuted[pivot]);
                (apMaskPermuted[pivot], apMaskPermuted[pivotColumn]) = (apMaskPermuted[pivotColumn], apMaskPermuted[pivot]);
            }

            for (var row = 0; row < K; row++)
            {
                if (row == pivot || matrix[row, pivot] == 0)
                {
                    continue;
                }

                for (var col = pivot; col < N; col++)
                {
                    matrix[row, col] ^= matrix[pivot, col];
                }
            }
        }

        return true;
    }

    private static void SwapColumns(int[,] matrix, int left, int right)
    {
        for (var row = 0; row < K; row++)
        {
            (matrix[row, left], matrix[row, right]) = (matrix[row, right], matrix[row, left]);
        }
    }

    private sealed record Ft8OsdWork(
        bool IsValid,
        int[,] GeneratorPermuted,
        int[] Indices,
        int[] HardDecisionPermuted,
        double[] ReliabilityPermuted,
        int[] ApMaskPermuted,
        int[] M0)
    {
        public static Ft8OsdWork Invalid { get; } = new(false, new int[0, 0], [], [], [], [], []);
    }
}

internal sealed record Ft8OsdDecodeResult(
    bool CrcOk,
    int Order,
    double Distance,
    int[]? Decoded91);
