namespace ShackStack.DecoderHost.GplWsjtx.Ft8;

internal sealed class Ft8MessageUnpackerPort
{
    private const int Ntokens = 2063592;
    private const int Max22 = 4194304;
    private const int MaxGrid4 = 32400;
    private const int MaxHash22 = 1000;

    private const string C1 = " 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string C2 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string C3 = "0123456789";
    private const string C4 = " ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string C38 = " 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private static readonly string[] Calls10 = new string[1024];
    private static readonly string[] Calls12 = new string[4096];
    private static readonly List<(int Hash22, string Call)> Calls22 = [];

    public bool TryUnpack(int[] message77, out string message)
    {
        message = string.Empty;
        if (message77.Length < 77)
        {
            return false;
        }

        var bits = string.Concat(message77.Take(77).Select(b => b != 0 ? '1' : '0'));
        var n3 = ReadInt(bits, 71, 3);
        var i3 = ReadInt(bits, 74, 3);

        if (i3 == 1 || i3 == 2)
        {
            return TryUnpackStandard(bits, i3, out message);
        }

        if (i3 == 4)
        {
            return TryUnpackType4(bits, out message);
        }

        if (i3 == 0 && n3 == 0)
        {
            message = "[ft8 free text]";
            return true;
        }

        message = $"[ft8 type {i3}.{n3}]";
        return true;
    }

    private static bool TryUnpackStandard(string bits, int i3, out string message)
    {
        message = string.Empty;
        var n28a = ReadInt(bits, 0, 28);
        var ipa = ReadInt(bits, 28, 1);
        var n28b = ReadInt(bits, 29, 28);
        var ipb = ReadInt(bits, 57, 1);
        var ir = ReadInt(bits, 58, 1);
        var igrid4 = ReadInt(bits, 59, 15);

        if (!TryUnpack28(n28a, out var call1) || !TryUnpack28(n28b, out var call2))
        {
            return false;
        }

        if (call1.StartsWith("CQ_"))
        {
            call1 = "CQ " + call1[3..].Trim();
        }

        if (!call1.StartsWith("<", StringComparison.Ordinal))
        {
            call1 = ApplySuffix(call1, ipa, i3);
            SaveHashCall(call1);
        }

        if (!call2.StartsWith("<", StringComparison.Ordinal))
        {
            call2 = ApplySuffix(call2, ipb, i3);
            SaveHashCall(call2);
        }

        if (igrid4 <= MaxGrid4)
        {
            if (!TryToGrid4(igrid4, out var grid4))
            {
                return false;
            }

            message = ir == 0
                ? $"{call1} {call2} {grid4}"
                : $"{call1} {call2} R {grid4}";
            return !message.StartsWith("CQ ") || ir == 0;
        }

        var irpt = igrid4 - MaxGrid4;
        if (irpt == 1)
        {
            message = $"{call1} {call2}";
            return true;
        }

        if (irpt == 2)
        {
            message = $"{call1} {call2} RRR";
            return !message.StartsWith("CQ ", StringComparison.Ordinal);
        }

        if (irpt == 3)
        {
            message = $"{call1} {call2} RR73";
            return !message.StartsWith("CQ ", StringComparison.Ordinal);
        }

        if (irpt == 4)
        {
            message = $"{call1} {call2} 73";
            return !message.StartsWith("CQ ", StringComparison.Ordinal);
        }

        var isnr = irpt - 35;
        if (isnr > 50)
        {
            isnr -= 101;
        }

        var report = isnr >= 0 ? $"+{isnr:00}" : $"{isnr:00}";
        message = ir == 0
            ? $"{call1} {call2} {report}"
            : $"{call1} {call2} R{report}";
        return !message.StartsWith("CQ ", StringComparison.Ordinal) || irpt < 2;
    }

    private static bool TryUnpackType4(string bits, out string message)
    {
        message = string.Empty;
        var n12 = ReadInt(bits, 0, 12);
        var n58 = ReadLong(bits, 12, 58);
        var iflip = ReadInt(bits, 70, 1);
        var nrpt = ReadInt(bits, 71, 2);
        var icq = ReadInt(bits, 73, 1);

        var c11 = DecodeBase38(n58, 11);
        var hashedCall = ResolveHash12(n12);
        string call1;
        string call2;
        if (iflip == 0)
        {
            call1 = hashedCall;
            call2 = c11;
            SaveHashCall(call2);
        }
        else
        {
            call1 = c11;
            call2 = hashedCall;
            SaveHashCall(call1);
        }

        if (icq == 1)
        {
            message = $"CQ {call2}";
            return !message.StartsWith("CQ <", StringComparison.Ordinal);
        }

        message = nrpt switch
        {
            0 => $"{call1} {call2}",
            1 => $"{call1} {call2} RRR",
            2 => $"{call1} {call2} RR73",
            3 => $"{call1} {call2} 73",
            _ => $"{call1} {call2}",
        };
        return true;
    }

    private static string ApplySuffix(string call, int suffixFlag, int i3)
    {
        if (suffixFlag != 1)
        {
            return call;
        }

        return i3 switch
        {
            1 => $"{call}/R",
            2 => $"{call}/P",
            _ => call,
        };
    }

    private static bool TryUnpack28(int n28, out string call)
    {
        call = string.Empty;
        if (n28 < Ntokens)
        {
            if (n28 == 0)
            {
                call = "DE";
                return true;
            }

            if (n28 == 1)
            {
                call = "QRZ";
                return true;
            }

            if (n28 == 2)
            {
                call = "CQ";
                return true;
            }

            if (n28 <= 1002)
            {
                call = $"CQ_{n28 - 3:000}";
                return true;
            }

            if (n28 <= 532443)
            {
                var n = n28 - 1003;
                var i1 = n / (27 * 27 * 27);
                n -= 27 * 27 * 27 * i1;
                var i2 = n / (27 * 27);
                n -= 27 * 27 * i2;
                var i3 = n / 27;
                var i4 = n - 27 * i3;
                call = $"CQ_{new string(new[] { C4[i1], C4[i2], C4[i3], C4[i4] }).Trim()}";
                return true;
            }
        }

        n28 -= Ntokens;
        if (n28 < Max22)
        {
            call = "<...>";
            return true;
        }

        var nstd = n28 - Max22;
        var c1 = nstd / (36 * 10 * 27 * 27 * 27);
        nstd -= 36 * 10 * 27 * 27 * 27 * c1;
        var c2 = nstd / (10 * 27 * 27 * 27);
        nstd -= 10 * 27 * 27 * 27 * c2;
        var c3 = nstd / (27 * 27 * 27);
        nstd -= 27 * 27 * 27 * c3;
        var c4 = nstd / (27 * 27);
        nstd -= 27 * 27 * c4;
        var c5 = nstd / 27;
        var c6 = nstd - 27 * c5;

        if (c1 < 0 || c1 >= C1.Length || c2 < 0 || c2 >= C2.Length || c3 < 0 || c3 >= C3.Length ||
            c4 < 0 || c4 >= C4.Length || c5 < 0 || c5 >= C4.Length || c6 < 0 || c6 >= C4.Length)
        {
            return false;
        }

        call = $"{C1[c1]}{C2[c2]}{C3[c3]}{C4[c4]}{C4[c5]}{C4[c6]}".Trim();
        return !string.IsNullOrWhiteSpace(call) && !call.Contains(' ');
    }

    private static bool TryToGrid4(int n, out string grid4)
    {
        grid4 = string.Empty;
        var j1 = n / (18 * 10 * 10);
        if (j1 < 0 || j1 > 17) return false;
        n -= j1 * 18 * 10 * 10;
        var j2 = n / (10 * 10);
        if (j2 < 0 || j2 > 17) return false;
        n -= j2 * 10 * 10;
        var j3 = n / 10;
        if (j3 < 0 || j3 > 9) return false;
        var j4 = n - j3 * 10;
        if (j4 < 0 || j4 > 9) return false;
        grid4 = $"{(char)('A' + j1)}{(char)('A' + j2)}{(char)('0' + j3)}{(char)('0' + j4)}";
        return true;
    }

    private static int ReadInt(string bits, int start, int length)
    {
        var value = 0;
        for (var i = 0; i < length; i++)
        {
            value = (value << 1) | (bits[start + i] == '1' ? 1 : 0);
        }

        return value;
    }

    private static long ReadLong(string bits, int start, int length)
    {
        long value = 0;
        for (var i = 0; i < length; i++)
        {
            value = (value << 1) | (bits[start + i] == '1' ? 1L : 0L);
        }

        return value;
    }

    private static string DecodeBase38(long value, int width)
    {
        var chars = new char[width];
        for (var i = width - 1; i >= 0; i--)
        {
            var index = (int)(value % 38);
            chars[i] = C38[index];
            value /= 38;
        }

        return new string(chars).Trim();
    }

    private static string ResolveHash12(int n12)
    {
        if (n12 < 0 || n12 >= Calls12.Length)
        {
            return "<...>";
        }

        var value = Calls12[n12];
        return string.IsNullOrWhiteSpace(value) ? "<...>" : $"<{value}>";
    }

    private static void SaveHashCall(string? call)
    {
        if (string.IsNullOrWhiteSpace(call))
        {
            return;
        }

        var normalized = call.Trim().ToUpperInvariant();
        if (normalized.StartsWith("<", StringComparison.Ordinal) || normalized.Length < 3)
        {
            return;
        }

        var n10 = HashCall(normalized, 10);
        if (n10 >= 0 && n10 < Calls10.Length)
        {
            Calls10[n10] = normalized;
        }

        var n12 = HashCall(normalized, 12);
        if (n12 >= 0 && n12 < Calls12.Length)
        {
            Calls12[n12] = normalized;
        }

        var n22 = HashCall(normalized, 22);
        var existingIndex = Calls22.FindIndex(entry => entry.Hash22 == n22);
        if (existingIndex >= 0)
        {
            Calls22.RemoveAt(existingIndex);
        }

        Calls22.Insert(0, (n22, normalized));
        if (Calls22.Count > MaxHash22)
        {
            Calls22.RemoveRange(MaxHash22, Calls22.Count - MaxHash22);
        }
    }

    private static int HashCall(string call, int bits)
    {
        long n8 = 0;
        var padded = call.PadRight(11);
        for (var i = 0; i < 11; i++)
        {
            var j = C38.IndexOf(padded[i]);
            if (j < 0)
            {
                return -1;
            }

            n8 = (38 * n8) + j;
        }

        return (int)(47055833459L * n8 >> (64 - bits));
    }
}
