using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace ShackStack.DecoderHost.GplWsjtx;

internal static class Js8Varicode
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ+-./?";
    private const string Alphabet72 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-+/?." ;
    private const string Alphanumeric = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ /@";

    private const int FrameHeartbeat = 0;
    private const int FrameCompound = 1;
    private const int FrameCompoundDirected = 2;
    private const int FrameDirected = 3;
    private const int FrameData = 4;
    private const int Js8CallData = 4;
    private const uint NBaseCall = 37 * 36 * 10 * 27 * 27 * 27;
    private const ushort NBaseGrid = 180 * 180;
    private const ushort NUserGrid = NBaseGrid + 10;
    private const ushort NMaxGrid = (1 << 15) - 1;

    private static readonly Dictionary<uint, string> BaseCalls = new()
    {
        [NBaseCall + 1] = "<....>",
        [NBaseCall + 2] = "@ALLCALL",
        [NBaseCall + 3] = "@JS8NET",
        [NBaseCall + 4] = "@DX/NA",
        [NBaseCall + 5] = "@DX/SA",
        [NBaseCall + 6] = "@DX/EU",
        [NBaseCall + 7] = "@DX/AS",
        [NBaseCall + 8] = "@DX/AF",
        [NBaseCall + 9] = "@DX/OC",
        [NBaseCall + 10] = "@DX/AN",
        [NBaseCall + 11] = "@REGION/1",
        [NBaseCall + 12] = "@REGION/2",
        [NBaseCall + 13] = "@REGION/3",
        [NBaseCall + 14] = "@GROUP/0",
        [NBaseCall + 15] = "@GROUP/1",
        [NBaseCall + 16] = "@GROUP/2",
        [NBaseCall + 17] = "@GROUP/3",
        [NBaseCall + 18] = "@GROUP/4",
        [NBaseCall + 19] = "@GROUP/5",
        [NBaseCall + 20] = "@GROUP/6",
        [NBaseCall + 21] = "@GROUP/7",
        [NBaseCall + 22] = "@GROUP/8",
        [NBaseCall + 23] = "@GROUP/9",
        [NBaseCall + 24] = "@COMMAND",
        [NBaseCall + 25] = "@CONTROL",
        [NBaseCall + 26] = "@NET",
        [NBaseCall + 27] = "@NTS",
        [NBaseCall + 28] = "@RESERVE/0",
        [NBaseCall + 29] = "@RESERVE/1",
        [NBaseCall + 30] = "@RESERVE/2",
        [NBaseCall + 31] = "@RESERVE/3",
        [NBaseCall + 32] = "@RESERVE/4",
        [NBaseCall + 33] = "@APRSIS",
        [NBaseCall + 34] = "@RAGCHEW",
        [NBaseCall + 35] = "@JS8",
        [NBaseCall + 36] = "@EMCOMM",
        [NBaseCall + 37] = "@ARES",
        [NBaseCall + 38] = "@MARS",
        [NBaseCall + 39] = "@AMRRON",
        [NBaseCall + 40] = "@RACES",
        [NBaseCall + 41] = "@RAYNET",
        [NBaseCall + 42] = "@RADAR",
        [NBaseCall + 43] = "@SKYWARN",
        [NBaseCall + 44] = "@CQ",
        [NBaseCall + 45] = "@HB",
        [NBaseCall + 46] = "@QSO",
        [NBaseCall + 47] = "@QSOPARTY",
        [NBaseCall + 48] = "@CONTEST",
        [NBaseCall + 49] = "@FIELDDAY",
        [NBaseCall + 50] = "@SOTA",
        [NBaseCall + 51] = "@IOTA",
        [NBaseCall + 52] = "@POTA",
        [NBaseCall + 53] = "@QRP",
        [NBaseCall + 54] = "@QRO",
    };

    private static readonly Dictionary<int, string> CqStrings = new()
    {
        [0] = "CQ CQ CQ",
        [1] = "CQ DX",
        [2] = "CQ QRP",
        [3] = "CQ CONTEST",
        [4] = "CQ FIELD",
        [5] = "CQ FD",
        [6] = "CQ CQ",
        [7] = "CQ",
    };

    private static readonly Dictionary<int, string> HbStrings = new()
    {
        [0] = "HB",
        [1] = "HB",
        [2] = "HB",
        [3] = "HB",
        [4] = "HB",
        [5] = "HB",
        [6] = "HB",
        [7] = "HB",
    };

    private static readonly Dictionary<int, string> DirectedCommands = new()
    {
        [-1] = " HB",
        [0] = " SNR?",
        [1] = " DIT DIT",
        [2] = " NACK",
        [3] = " HEARING?",
        [4] = " GRID?",
        [5] = ">",
        [6] = " STATUS?",
        [7] = " STATUS",
        [8] = " HEARING",
        [9] = " MSG",
        [10] = " MSG TO:",
        [11] = " QUERY",
        [12] = " QUERY MSGS",
        [13] = " QUERY CALL",
        [14] = " ACK",
        [15] = " GRID",
        [16] = " INFO?",
        [17] = " INFO",
        [18] = " FB",
        [19] = " HW CPY?",
        [20] = " SK",
        [21] = " RR",
        [22] = " QSL?",
        [23] = " QSL",
        [24] = " CMD",
        [25] = " SNR",
        [26] = " NO",
        [27] = " YES",
        [28] = " 73",
        [29] = " HEARTBEAT SNR",
        [30] = " AGN?",
        [31] = " ",
    };

    private static readonly HashSet<int> SnrCommands = [25, 29];

    private static readonly Dictionary<string, string> HuffTable = new(StringComparer.Ordinal)
    {
        [" "] = "01",
        ["E"] = "100",
        ["T"] = "1101",
        ["A"] = "0011",
        ["O"] = "11111",
        ["I"] = "11100",
        ["N"] = "10111",
        ["S"] = "10100",
        ["H"] = "00011",
        ["R"] = "00000",
        ["D"] = "111011",
        ["L"] = "110011",
        ["C"] = "110001",
        ["U"] = "101101",
        ["M"] = "101011",
        ["W"] = "001011",
        ["F"] = "001001",
        ["G"] = "000101",
        ["Y"] = "000011",
        ["P"] = "1111011",
        ["B"] = "1111001",
        ["."] = "1110100",
        ["V"] = "1100101",
        ["K"] = "1100100",
        ["-"] = "1100001",
        ["+"] = "1100000",
        ["?"] = "1011001",
        ["!"] = "1011000",
        ["\""] = "1010101",
        ["X"] = "1010100",
        ["0"] = "0010101",
        ["J"] = "0010100",
        ["1"] = "0010001",
        ["Q"] = "0010000",
        ["2"] = "0001001",
        ["Z"] = "0001000",
        ["3"] = "0000101",
        ["5"] = "0000100",
        ["4"] = "11110101",
        ["9"] = "11110100",
        ["8"] = "11110001",
        ["6"] = "11110000",
        ["7"] = "11101011",
        ["/"] = "11101010",
    };

    private static readonly object JscMapLock = new();
    private static readonly Dictionary<string, string[]> JscMapCache = new(StringComparer.OrdinalIgnoreCase);

    public static string DecodeFrame(string rawMessage, string? dictionaryDirectory = null)
    {
        var (frame, bits) = SplitRawFrame(rawMessage);
        if (frame.Length < 12 || frame.Contains(' ', StringComparison.Ordinal))
        {
            return frame.Trim();
        }

        if ((bits & Js8CallData) == Js8CallData && TryUnpackFastData(frame, dictionaryDirectory, out var fastData))
        {
            return fastData;
        }

        if ((bits & Js8CallData) != Js8CallData && TryUnpackData(frame, dictionaryDirectory, out var data))
        {
            return data;
        }

        if ((bits & Js8CallData) != Js8CallData && TryUnpackHeartbeat(frame, out var heartbeat))
        {
            return heartbeat;
        }

        if ((bits & Js8CallData) != Js8CallData && TryUnpackCompound(frame, out var compound))
        {
            return compound;
        }

        if ((bits & Js8CallData) != Js8CallData && TryUnpackDirected(frame, out var directed))
        {
            return directed;
        }

        return frame.Trim();
    }

    private static (string Frame, int Bits) SplitRawFrame(string rawMessage)
    {
        var messageField = rawMessage.Length >= 22 ? rawMessage[..22] : rawMessage;
        var bits = 0;
        if (messageField.Length == 22 && char.IsDigit(messageField[21]))
        {
            bits = messageField[21] - '0';
            messageField = messageField[..21];
        }

        return (messageField.Length >= 12 ? messageField[..12] : messageField.Trim(), bits);
    }

    private static bool TryUnpackFastData(string text, string? dictionaryDirectory, out string message)
    {
        message = string.Empty;
        if (!TryUnpack72Bits(text, out var value, out var rem))
        {
            return false;
        }

        var bits = IntToBits(value, 64).Concat(IntToBits(rem, 8)).ToList();
        var n = LastIndexOf(bits, false);
        if (n <= 0)
        {
            return false;
        }

        var payload = bits.Take(n).ToList();
        return TryJscDecompress(payload, dictionaryDirectory, out message);
    }

    private static bool TryUnpackData(string text, string? dictionaryDirectory, out string message)
    {
        message = string.Empty;
        if (!TryUnpack72Bits(text, out var value, out var rem))
        {
            return false;
        }

        var bits = IntToBits(value, 64).Concat(IntToBits(rem, 8)).ToList();
        if (bits.Count < 3 || !bits[0])
        {
            return false;
        }

        bits = bits.Skip(1).ToList();
        var compressed = bits[0];
        var n = LastIndexOf(bits, false);
        if (n <= 1)
        {
            return false;
        }

        var payload = bits.Skip(1).Take(n - 1).ToList();
        if (compressed)
        {
            return TryJscDecompress(payload, dictionaryDirectory, out message);
        }

        message = HuffDecode(payload).TrimEnd();
        return !string.IsNullOrWhiteSpace(message);
    }

    private static bool TryUnpackHeartbeat(string text, out string message)
    {
        message = string.Empty;
        if (!TryUnpackCompoundFrame(text, out var type, out var num, out var bits3, out var callsign)
            || type != FrameHeartbeat)
        {
            return false;
        }

        var extra = UnpackGrid((ushort)(num & ((1 << 15) - 1)));
        var isAlt = (num & (1 << 15)) != 0;
        var label = isAlt
            ? CqStrings.GetValueOrDefault(bits3, string.Empty)
            : HbStrings.GetValueOrDefault(bits3, "HB") == "HB"
                ? "HEARTBEAT"
                : HbStrings.GetValueOrDefault(bits3, string.Empty);

        message = $"{callsign}: {(isAlt ? "@ALLCALL " : "@HB ")}{label} {extra} ".TrimEnd();
        return true;
    }

    private static bool TryUnpackCompound(string text, out string message)
    {
        message = string.Empty;
        if (!TryUnpackCompoundFrame(text, out var type, out var extra, out _, out var callsign)
            || (type != FrameCompound && type != FrameCompoundDirected))
        {
            return false;
        }

        if (type == FrameCompound)
        {
            var suffix = extra <= NBaseGrid ? $" {UnpackGrid(extra)}" : string.Empty;
            message = $"{callsign}: {suffix}".TrimEnd();
            return true;
        }

        if (NUserGrid <= extra && extra < NMaxGrid)
        {
            var cmd = UnpackCmd((byte)(extra - NUserGrid), out var num);
            var cmdText = DirectedCommands.GetValueOrDefault(cmd, string.Empty);
            if (SnrCommands.Contains(cmd))
            {
                cmdText += $" {FormatSnr(num - 31)}";
            }

            message = $"{callsign}{cmdText} ";
            return true;
        }

        message = $"{callsign}: ";
        return true;
    }

    private static bool TryUnpackDirected(string text, out string message)
    {
        message = string.Empty;
        if (!TryUnpack72Bits(text, out var value, out var extra))
        {
            return false;
        }

        var bits = IntToBits(value, 64);
        var packedFlag = BitsToInt(bits, 0, 3);
        if (packedFlag != FrameDirected)
        {
            return false;
        }

        var packedFrom = (uint)BitsToInt(bits, 3, 28);
        var packedTo = (uint)BitsToInt(bits, 31, 28);
        var packedCmd = (byte)BitsToInt(bits, 59, 5);
        var portableFrom = ((extra >> 7) & 1) == 1;
        var portableTo = ((extra >> 6) & 1) == 1;
        var num = extra % 64;

        var from = UnpackCallsign(packedFrom, portableFrom);
        var to = UnpackCallsign(packedTo, portableTo);
        var cmd = DirectedCommands.GetValueOrDefault(packedCmd % 32, string.Empty);
        var suffix = string.Empty;
        if (num != 0)
        {
            suffix = SnrCommands.Contains(packedCmd % 32)
                ? $" {FormatSnr(num - 31)}"
                : $" {num - 31}";
        }

        message = $"{from}: {to}{cmd}{suffix} ";
        return true;
    }

    private static bool TryUnpackCompoundFrame(string text, out int type, out ushort num, out byte bits3, out string callsign)
    {
        type = 0;
        num = 0;
        bits3 = 0;
        callsign = string.Empty;
        if (!TryUnpack72Bits(text, out var value, out var packed8))
        {
            return false;
        }

        var bits = IntToBits(value, 64);
        type = (int)BitsToInt(bits, 0, 3);
        if (type == FrameData || type == FrameDirected)
        {
            return false;
        }

        var packed5 = packed8 >> 3;
        bits3 = (byte)(packed8 & ((1 << 3) - 1));
        var packedCallsign = BitsToInt(bits, 3, 50);
        var packed11 = (ushort)BitsToInt(bits, 53, 11);
        callsign = UnpackAlphaNumeric50(packedCallsign);
        num = (ushort)((packed11 << 5) | packed5);
        return !string.IsNullOrWhiteSpace(callsign);
    }

    private static bool TryUnpack72Bits(string text, out ulong value, out byte rem)
    {
        value = 0;
        rem = 0;
        if (text.Length < 12)
        {
            return false;
        }

        for (var i = 0; i < 10; i++)
        {
            var index = Alphabet72.IndexOf(text[i], StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            value |= (ulong)index << (58 - (6 * i));
        }

        var remHigh = Alphabet72.IndexOf(text[10], StringComparison.Ordinal);
        var remLow = Alphabet72.IndexOf(text[11], StringComparison.Ordinal);
        if (remHigh < 0 || remLow < 0)
        {
            return false;
        }

        value |= (ulong)((uint)remHigh >> 2);
        rem = (byte)((uint)((remHigh & 0x03) << 6) | (uint)(remLow & 0x3F));
        return true;
    }

    private static string UnpackAlphaNumeric50(ulong packed)
    {
        Span<char> word = stackalloc char[11];

        word[10] = Alphanumeric[SafeIndex((int)(packed % 38), Alphanumeric.Length)];
        packed /= 38;
        word[9] = Alphanumeric[SafeIndex((int)(packed % 38), Alphanumeric.Length)];
        packed /= 38;
        word[8] = Alphanumeric[SafeIndex((int)(packed % 38), Alphanumeric.Length)];
        packed /= 38;
        word[7] = packed % 2 == 1 ? '/' : ' ';
        packed /= 2;
        word[6] = Alphanumeric[SafeIndex((int)(packed % 38), Alphanumeric.Length)];
        packed /= 38;
        word[5] = Alphanumeric[SafeIndex((int)(packed % 38), Alphanumeric.Length)];
        packed /= 38;
        word[4] = Alphanumeric[SafeIndex((int)(packed % 38), Alphanumeric.Length)];
        packed /= 38;
        word[3] = packed % 2 == 1 ? '/' : ' ';
        packed /= 2;
        word[2] = Alphanumeric[SafeIndex((int)(packed % 38), Alphanumeric.Length)];
        packed /= 38;
        word[1] = Alphanumeric[SafeIndex((int)(packed % 38), Alphanumeric.Length)];
        packed /= 38;
        word[0] = Alphanumeric[SafeIndex((int)(packed % 39), Alphanumeric.Length)];

        return new string(word).Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string UnpackCallsign(uint value, bool portable)
    {
        if (BaseCalls.TryGetValue(value, out var baseCall))
        {
            return baseCall;
        }

        Span<char> word = stackalloc char[6];
        word[5] = Alphanumeric[SafeIndex((int)(value % 27 + 10), Alphanumeric.Length)];
        value /= 27;
        word[4] = Alphanumeric[SafeIndex((int)(value % 27 + 10), Alphanumeric.Length)];
        value /= 27;
        word[3] = Alphanumeric[SafeIndex((int)(value % 27 + 10), Alphanumeric.Length)];
        value /= 27;
        word[2] = Alphanumeric[SafeIndex((int)(value % 10), Alphanumeric.Length)];
        value /= 10;
        word[1] = Alphanumeric[SafeIndex((int)(value % 36), Alphanumeric.Length)];
        value /= 36;
        word[0] = Alphanumeric[SafeIndex((int)value, Alphanumeric.Length)];

        var callsign = new string(word).Trim();
        if (callsign.StartsWith("3D0", StringComparison.Ordinal))
        {
            callsign = $"3DA0{callsign[3..]}";
        }

        if (callsign.Length > 1 && callsign[0] == 'Q' && callsign[1] is >= 'A' and <= 'Z')
        {
            callsign = $"3X{callsign[1..]}";
        }

        return portable ? $"{callsign}/P" : callsign;
    }

    private static string UnpackGrid(ushort value)
    {
        if (value > NBaseGrid)
        {
            return string.Empty;
        }

        var dlat = (value % 180) - 90;
        var dlong = (value / 180 * 2) - 180 + 2;
        return DegToGrid(dlong, dlat)[..4];
    }

    private static string DegToGrid(double longitude, double latitude)
    {
        if (longitude < -180)
        {
            longitude += 360;
        }

        if (longitude > 180)
        {
            longitude -= 360;
        }

        var nlong = (int)(60.0 * (180.0 - longitude) / 5);
        var lon1 = nlong / 240;
        var lon2 = (nlong - (240 * lon1)) / 24;
        var lon3 = nlong - (240 * lon1) - (24 * lon2);

        var nlat = (int)(60.0 * (latitude + 90) / 2.5);
        var lat1 = nlat / 240;
        var lat2 = (nlat - (240 * lat1)) / 24;
        var lat3 = nlat - (240 * lat1) - (24 * lat2);

        return string.Create(6, (lon1, lat1, lon2, lat2, lon3, lat3), static (span, state) =>
        {
            span[0] = (char)('A' + state.lon1);
            span[1] = (char)('A' + state.lat1);
            span[2] = (char)('0' + state.lon2);
            span[3] = (char)('0' + state.lat2);
            span[4] = (char)('a' + state.lon3);
            span[5] = (char)('a' + state.lat3);
        });
    }

    private static int UnpackCmd(byte value, out int num)
    {
        if ((value & (1 << 7)) != 0)
        {
            num = value & ((1 << 6) - 1);
            return (value & (1 << 6)) != 0 ? 29 : 25;
        }

        num = 0;
        return value & ((1 << 7) - 1);
    }

    private static string FormatSnr(int snr)
    {
        if (snr is < -60 or > 60)
        {
            return string.Empty;
        }

        return snr >= 0 ? $"+{snr:00}" : snr.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string HuffDecode(IReadOnlyList<bool> bitvec)
    {
        var bits = new StringBuilder(bitvec.Count);
        foreach (var bit in bitvec)
        {
            bits.Append(bit ? '1' : '0');
        }

        var remaining = bits.ToString();
        var text = new StringBuilder();
        while (remaining.Length > 0)
        {
            var found = false;
            foreach (var pair in HuffTable)
            {
                if (!remaining.StartsWith(pair.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                text.Append(pair.Key);
                remaining = remaining[pair.Value.Length..];
                found = true;
                break;
            }

            if (!found)
            {
                break;
            }
        }

        return text.ToString();
    }

    private static bool TryJscDecompress(IReadOnlyList<bool> bitvec, string? dictionaryDirectory, out string message)
    {
        message = string.Empty;
        var map = LoadJscMap(dictionaryDirectory);
        if (map.Length == 0)
        {
            return false;
        }

        const uint s = 7;
        const uint c = 9;
        uint[] bases =
        [
            0,
            s,
            s + (s * c),
            s + (s * c) + (s * c * c),
            s + (s * c) + (s * c * c) + (s * c * c * c),
            s + (s * c) + (s * c * c) + (s * c * c * c) + (s * c * c * c * c),
            s + (s * c) + (s * c * c) + (s * c * c * c) + (s * c * c * c * c) + (s * c * c * c * c * c),
            s + (s * c) + (s * c * c) + (s * c * c * c) + (s * c * c * c * c) + (s * c * c * c * c * c) + (s * c * c * c * c * c * c),
        ];

        var bytes = new List<ulong>();
        var separators = new Queue<int>();
        var i = 0;
        while (i < bitvec.Count)
        {
            if (bitvec.Count - i < 4)
            {
                break;
            }

            var b = BitsToInt(bitvec, i, 4);
            bytes.Add(b);
            i += 4;

            if (b < s)
            {
                if (bitvec.Count - i > 0 && bitvec[i])
                {
                    separators.Enqueue(bytes.Count - 1);
                }

                i += 1;
            }
        }

        var output = new StringBuilder();
        var start = 0;
        while (start < bytes.Count)
        {
            uint k = 0;
            uint j = 0;

            while (start + k < bytes.Count && bytes[(int)(start + k)] >= s)
            {
                j = (uint)((j * c) + (bytes[(int)(start + k)] - s));
                k++;
            }

            if (j >= map.Length || start + k >= bytes.Count || k >= bases.Length)
            {
                break;
            }

            j = (uint)((j * s) + bytes[(int)(start + k)] + bases[k]);
            if (j >= map.Length)
            {
                break;
            }

            output.Append(map[j]);
            if (separators.Count > 0 && separators.Peek() == start + k)
            {
                output.Append(' ');
                separators.Dequeue();
            }

            start += (int)k + 1;
        }

        message = output.ToString().TrimEnd();
        return !string.IsNullOrWhiteSpace(message);
    }

    private static string[] LoadJscMap(string? dictionaryDirectory)
    {
        var path = ResolveJscMapPath(dictionaryDirectory);
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        lock (JscMapLock)
        {
            if (JscMapCache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var content = File.ReadAllText(path, Encoding.Latin1);
            var matches = Regex.Matches(
                content,
                "\\{\\s*\"((?:\\\\.|[^\"\\\\])*)\"\\s*,\\s*\\d+\\s*,\\s*\\d+\\s*\\}",
                RegexOptions.CultureInvariant);
            var map = matches.Select(match => UnescapeCString(match.Groups[1].Value)).ToArray();
            JscMapCache[path] = map;
            return map;
        }
    }

    private static string? ResolveJscMapPath(string? dictionaryDirectory)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(dictionaryDirectory))
        {
            candidates.Add(Path.Combine(dictionaryDirectory, "jsc_map.cpp"));
            var parent = Directory.GetParent(dictionaryDirectory);
            if (parent is not null)
            {
                candidates.Add(Path.Combine(parent.FullName, "jsc_map.cpp"));
            }
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "jsc_map.cpp"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "js8call-tools", "runtime", "bin", "jsc_map.cpp"));

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string UnescapeCString(string value)
    {
        var output = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i == value.Length - 1)
            {
                output.Append(value[i]);
                continue;
            }

            i++;
            output.Append(value[i] switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                '"' => '"',
                _ => value[i],
            });
        }

        return output.ToString();
    }

    private static IReadOnlyList<bool> IntToBits(BigInteger value, int expected)
    {
        var bits = new List<bool>(expected);
        while (value > 0)
        {
            bits.Insert(0, !value.IsEven);
            value >>= 1;
        }

        while (bits.Count < expected)
        {
            bits.Insert(0, false);
        }

        return bits;
    }

    private static ulong BitsToInt(IReadOnlyList<bool> bits, int start, int count)
    {
        ulong value = 0;
        for (var i = 0; i < count; i++)
        {
            value = (value << 1) + (bits[start + i] ? 1UL : 0UL);
        }

        return value;
    }

    private static int LastIndexOf(IReadOnlyList<bool> bits, bool value)
    {
        for (var i = bits.Count - 1; i >= 0; i--)
        {
            if (bits[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    private static int SafeIndex(int index, int length) => Math.Clamp(index, 0, length - 1);
}
