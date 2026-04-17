namespace ShackStack.DecoderHost.GplWsjtx.Ft8;

internal static class Ft8ApPort
{
    private const int MaxApPasses = 2;
    private const int Ntokens = 2063592;
    private const int Max22 = 4194304;
    private const int MaxGrid4 = 32400;

    private const string C1 = " 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string C2 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string C3 = "0123456789";
    private const string C4 = " ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private static readonly int[] Mcq =
        ScaleMask([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0]);

    public static IReadOnlyList<Ft8ApDecodePass> BuildPasses(Ft8MetricResult metrics, string stationCallsign, IReadOnlyList<string>? recentHisCalls)
    {
        // AP is temporarily disabled while we re-align it with WSJT-X.
        // Recent experiments showed it can materially suppress decode rate,
        // so the safest path is to leave the base decoder untouched.
        return [];
    }

    private static bool TryBuildApsym(string stationCallsign, string? hisCallsign, out int[] apsym)
    {
        apsym = [];
        if (!TryPackStandardCall(stationCallsign, out var myCall28))
        {
            return false;
        }

        var effectiveHisCall = string.IsNullOrWhiteSpace(hisCallsign) ? "KA1ABC" : hisCallsign;
        if (!TryPackStandardCall(effectiveHisCall, out var hisCall28))
        {
            return false;
        }

        var bits77 = new int[77];
        WriteBits(bits77, 0, 28, myCall28);
        WriteBits(bits77, 28, 1, 0);
        WriteBits(bits77, 29, 28, hisCall28);
        WriteBits(bits77, 57, 1, 0);
        WriteBits(bits77, 58, 1, 0);
        WriteBits(bits77, 59, 15, MaxGrid4 + 2);
        WriteBits(bits77, 71, 3, 0);
        WriteBits(bits77, 74, 3, 1);

        apsym = bits77.Take(58).Select(static bit => bit == 0 ? -1 : 1).ToArray();
        return apsym.Length == 58;
    }

    private static Ft8ApDecodePass BuildCqPass(double[] sourceLlr, double apmag)
    {
        var llr = (double[])sourceLlr.Clone();
        var mask = new int[174];
        ApplyMask(llr, mask, Mcq, 0, apmag);
        ApplyRawBit(llr, mask, 74, -1, apmag);
        ApplyRawBit(llr, mask, 75, -1, apmag);
        ApplyRawBit(llr, mask, 76, +1, apmag);
        return new Ft8ApDecodePass("ap-cq", llr, mask);
    }

    private static Ft8ApDecodePass BuildMyCallPass(double[] sourceLlr, int[] apsym, double apmag)
    {
        var llr = (double[])sourceLlr.Clone();
        var mask = new int[174];
        ApplyMask(llr, mask, apsym.Take(29).ToArray(), 0, apmag);
        ApplyRawBit(llr, mask, 74, -1, apmag);
        ApplyRawBit(llr, mask, 75, -1, apmag);
        ApplyRawBit(llr, mask, 76, +1, apmag);
        return new Ft8ApDecodePass("ap-mycall", llr, mask);
    }

    private static Ft8ApDecodePass BuildPairPass(string label, double[] sourceLlr, int[] apsym, double apmag, int[]? tailPattern = null)
    {
        var llr = (double[])sourceLlr.Clone();
        var mask = new int[174];
        ApplyMask(llr, mask, apsym, 0, apmag);
        if (tailPattern is null)
        {
            ApplyRawBit(llr, mask, 74, -1, apmag);
            ApplyRawBit(llr, mask, 75, -1, apmag);
            ApplyRawBit(llr, mask, 76, +1, apmag);
        }
        else
        {
            ApplyMask(llr, mask, tailPattern, 58, apmag);
        }

        return new Ft8ApDecodePass(label, llr, mask);
    }

    private static bool TryPackStandardCall(string callsign, out int packed)
    {
        packed = 0;
        var normalized = NormalizeStandardCall(callsign);
        if (normalized is null)
        {
            return false;
        }

        var c1Index = C1.IndexOf(normalized[0]);
        var c2Index = C2.IndexOf(normalized[1]);
        var c3Index = C3.IndexOf(normalized[2]);
        var c4Index = C4.IndexOf(normalized[3]);
        var c5Index = C4.IndexOf(normalized[4]);
        var c6Index = C4.IndexOf(normalized[5]);
        if (c1Index < 0 || c2Index < 0 || c3Index < 0 || c4Index < 0 || c5Index < 0 || c6Index < 0)
        {
            return false;
        }

        var nstd =
            (c1Index * 36 * 10 * 27 * 27 * 27) +
            (c2Index * 10 * 27 * 27 * 27) +
            (c3Index * 27 * 27 * 27) +
            (c4Index * 27 * 27) +
            (c5Index * 27) +
            c6Index;

        packed = Ntokens + Max22 + nstd;
        return true;
    }

    private static string? NormalizeStandardCall(string callsign)
    {
        var value = callsign.Trim().ToUpperInvariant();
        var match = System.Text.RegularExpressions.Regex.Match(value, "^([0-9A-Z]{1,2})([0-9])([A-Z]{1,3})$");
        if (!match.Success)
        {
            return null;
        }

        var prefix = match.Groups[1].Value;
        var digit = match.Groups[2].Value;
        var suffix = match.Groups[3].Value;
        var leading = prefix.Length == 1 ? " " : string.Empty;
        if (prefix.Length > 2 || suffix.Length > 3)
        {
            return null;
        }

        return $"{leading}{prefix}{digit}{suffix.PadRight(3)}";
    }

    private static void WriteBits(int[] bits, int start, int width, int value)
    {
        for (var i = 0; i < width; i++)
        {
            var shift = width - 1 - i;
            bits[start + i] = (value >> shift) & 1;
        }
    }

    private static void ApplyMask(double[] llr, int[] apmask, int[] pattern, int startBit, double apmag)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            ApplyRawBit(llr, apmask, startBit + i, pattern[i], apmag);
        }
    }

    private static void ApplyRawBit(double[] llr, int[] apmask, int bitIndex, int bitSign, double apmag)
    {
        if (bitIndex < 0 || bitIndex >= llr.Length || bitIndex >= apmask.Length || bitSign == 99)
        {
            return;
        }

        apmask[bitIndex] = 1;
        llr[bitIndex] = apmag * bitSign;
    }

    private static int[] ScaleMask(int[] mask) => mask.Select(static bit => (bit * 2) - 1).ToArray();
}

internal sealed record Ft8ApDecodePass(string Label, double[] Llr, int[] ApMask);
