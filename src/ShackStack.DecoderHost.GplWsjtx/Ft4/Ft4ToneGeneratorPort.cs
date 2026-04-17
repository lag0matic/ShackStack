namespace ShackStack.DecoderHost.GplWsjtx.Ft4;

using ShackStack.DecoderHost.GplWsjtx.Ft8;

internal static class Ft4ToneGeneratorPort
{
    private static readonly int[] Icos4A = [0, 1, 3, 2];
    private static readonly int[] Icos4B = [1, 0, 2, 3];
    private static readonly int[] Icos4C = [2, 3, 1, 0];
    private static readonly int[] Icos4D = [3, 2, 0, 1];
    private static readonly int[] Rvec =
    [
        0,1,0,0,1,0,1,0,0,1,0,1,1,1,1,0,1,0,0,0,1,0,0,1,1,0,1,1,0,
        1,0,0,1,0,1,1,0,0,0,0,1,0,0,0,1,0,1,0,0,1,1,1,1,0,0,1,0,1,
        0,1,0,1,0,1,1,0,1,1,1,1,1,0,0,0,1,0,1
    ];

    public static int[] GetTonesFromMessageBits(int[] messageBits77)
    {
        if (messageBits77.Length < 77)
        {
            return [];
        }

        var scrambled = new int[77];
        for (var i = 0; i < 77; i++)
        {
            scrambled[i] = (messageBits77[i] + Rvec[i]) & 1;
        }

        var codeword = Encode174_91Port.Encode(scrambled);
        var tones = new int[Ft4Constants.ChannelSymbols];
        var data = new int[Ft4Constants.DataSymbols];
        for (var i = 0; i < Ft4Constants.DataSymbols; i++)
        {
            var isym = (codeword[2 * i + 1] * 2) + codeword[2 * i];
            data[i] = isym switch
            {
                0 or 1 => isym,
                2 => 3,
                _ => 2,
            };
        }

        Array.Copy(Icos4A, 0, tones, 0, 4);
        Array.Copy(data, 0, tones, 4, 29);
        Array.Copy(Icos4B, 0, tones, 33, 4);
        Array.Copy(data, 29, tones, 37, 29);
        Array.Copy(Icos4C, 0, tones, 66, 4);
        Array.Copy(data, 58, tones, 70, 29);
        Array.Copy(Icos4D, 0, tones, 99, 4);
        return tones;
    }
}
