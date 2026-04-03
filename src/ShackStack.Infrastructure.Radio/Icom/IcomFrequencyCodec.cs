namespace ShackStack.Infrastructure.Radio.Icom;

internal static class IcomFrequencyCodec
{
    public static long Decode(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return 0L;
        }

        var digits = new List<char>(payload.Length * 2);
        foreach (var value in payload)
        {
            digits.Insert(0, (char)('0' + (value & 0x0F)));
            digits.Insert(0, (char)('0' + ((value >> 4) & 0x0F)));
        }

        var text = new string(digits.ToArray()).TrimStart('0');
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0L;
        }

        return long.TryParse(text, out var hz) ? hz : 0L;
    }

    public static byte[] Encode(long hz)
    {
        var normalized = Math.Max(0L, hz).ToString("D10");
        var result = new byte[5];
        for (var i = 0; i < 5; i++)
        {
            var pair = normalized.Substring(normalized.Length - ((i + 1) * 2), 2);
            var tens = pair[0] - '0';
            var ones = pair[1] - '0';
            result[i] = (byte)((tens << 4) | ones);
        }

        return result;
    }
}
