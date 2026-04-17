namespace ShackStack.DecoderHost.GplWsjtx.Ft8;

internal static class Ft8ToneGeneratorPort
{
    private static readonly int[] Icos7 = [3, 1, 4, 0, 6, 5, 2];
    private static readonly int[] GrayMap = [0, 1, 3, 2, 5, 6, 4, 7];

    public static int[] GetTonesFromMessageBits(int[] messageBits77)
    {
        if (messageBits77.Length < 77)
        {
            return [];
        }

        var codeword = Encode174_91Port.Encode(messageBits77);
        var tones = new int[79];

        Array.Copy(Icos7, 0, tones, 0, 7);
        Array.Copy(Icos7, 0, tones, 36, 7);
        Array.Copy(Icos7, 0, tones, 72, 7);

        var k = 6;
        for (var j = 0; j < Ft8Constants.DataSymbols; j++)
        {
            var i = 3 * j;
            k += 1;
            if (j == 29)
            {
                k += 7;
            }

            var index = (codeword[i] * 4) + (codeword[i + 1] * 2) + codeword[i + 2];
            tones[k] = GrayMap[index];
        }

        return tones;
    }
}
