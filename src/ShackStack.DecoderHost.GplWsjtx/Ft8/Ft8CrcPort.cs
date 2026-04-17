namespace ShackStack.DecoderHost.GplWsjtx.Ft8;

internal static class Ft8CrcPort
{
    private static readonly int[] Polynomial = [1, 1, 0, 0, 1, 1, 1, 0, 1, 0, 1, 0, 1, 1, 1];

    public static bool CheckCrc14(int[] decoded91)
    {
        if (decoded91.Length < 91)
        {
            return false;
        }

        var m96 = new int[96];
        Array.Copy(decoded91, 0, m96, 0, 77);
        Array.Copy(decoded91, 77, m96, 82, 14);

        return GetCrc14Status(m96) == 0;
    }

    private static int GetCrc14Status(int[] bits)
    {
        if (bits.Length < 96)
        {
            return 1;
        }

        var received = 0;
        for (var i = 82; i < 96; i++)
        {
            received = (received << 1) | (bits[i] & 1);
        }

        var working = new int[96];
        Array.Copy(bits, working, 96);
        for (var i = 82; i < 96; i++)
        {
            working[i] = 0;
        }

        var r = new int[15];
        Array.Copy(working, 0, r, 0, 15);
        for (var i = 0; i <= working.Length - 15; i++)
        {
            r[14] = working[i + 14];
            var lead = r[0];
            for (var j = 0; j < 15; j++)
            {
                r[j] = (r[j] + lead * Polynomial[j]) & 1;
            }

            Array.Copy(r, 1, r, 0, 14);
        }

        var crc = 0;
        for (var i = 0; i < 14; i++)
        {
            crc = (crc << 1) | (r[i] & 1);
        }

        return crc == received ? 0 : 1;
    }
}
