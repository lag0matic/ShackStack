using ShackStack.Core.Abstractions.Models;

namespace ShackStack.UI.ViewModels;

internal static class PskBpskWaveformGenerator
{
    public const int SampleRate = 8000;
    private const double Amplitude = 0.72;

    public static Pcm16AudioClip Generate(string modeLabel, string text, double audioCenterHz)
    {
        var symbolSamples = string.Equals(modeLabel, "BPSK63", StringComparison.OrdinalIgnoreCase) ? 128 : 256;
        var dcdBits = symbolSamples == 128 ? 64 : 32;
        var samples = new List<short>();
        var phase = 0.0;
        var previousSymbol = 1.0;
        var shape = BuildRaisedCosineShape(symbolSamples);

        for (var i = 0; i < dcdBits * 3; i++)
        {
            WriteSymbol(samples, symbol: -previousSymbol, ref previousSymbol, ref phase, audioCenterHz, shape);
        }

        foreach (var ch in text)
        {
            foreach (var bit in EncodeVaricodeBits(ch))
            {
                var nextSymbol = bit ? previousSymbol : -previousSymbol;
                WriteSymbol(samples, nextSymbol, ref previousSymbol, ref phase, audioCenterHz, shape);
            }

            WriteSymbol(samples, previousSymbol, ref previousSymbol, ref phase, audioCenterHz, shape);
            WriteSymbol(samples, previousSymbol, ref previousSymbol, ref phase, audioCenterHz, shape);
        }

        for (var i = 0; i < dcdBits * 2; i++)
        {
            WriteSymbol(samples, previousSymbol, ref previousSymbol, ref phase, audioCenterHz, shape);
        }

        WriteSilence(samples, SampleRate / 4);
        var bytes = new byte[samples.Count * sizeof(short)];
        Buffer.BlockCopy(samples.ToArray(), 0, bytes, 0, bytes.Length);
        return new Pcm16AudioClip(bytes, SampleRate, 1);
    }

    private static void WriteSymbol(List<short> samples, double symbol, ref double previousSymbol, ref double carrierPhase, double audioCenterHz, double[] shape)
    {
        for (var i = 0; i < shape.Length; i++)
        {
            var shaped = (shape[i] * previousSymbol) + ((1.0 - shape[i]) * symbol);
            var value = shaped * Math.Cos(carrierPhase) * Amplitude;
            samples.Add((short)Math.Clamp(Math.Round(value * short.MaxValue), short.MinValue, short.MaxValue));
            carrierPhase += Math.Tau * audioCenterHz / SampleRate;
            if (carrierPhase > Math.Tau)
            {
                carrierPhase -= Math.Tau;
            }
        }

        previousSymbol = symbol;
    }

    private static double[] BuildRaisedCosineShape(int symbolSamples)
    {
        var shape = new double[symbolSamples];
        for (var i = 0; i < symbolSamples; i++)
        {
            shape[i] = (0.5 * Math.Cos(i * Math.PI / symbolSamples)) + 0.5;
        }

        return shape;
    }

    private static IEnumerable<bool> EncodeVaricodeBits(char ch)
    {
        var code = ch < 256 ? Varicode[(byte)ch] : Varicode[(byte)'?'];
        var started = false;
        for (var bit = 15; bit >= 0; bit--)
        {
            var one = ((code >> bit) & 1u) != 0;
            if (one || started)
            {
                started = true;
                yield return one;
            }
        }
    }

    private static void WriteSilence(List<short> samples, int count)
    {
        for (var i = 0; i < count; i++)
        {
            samples.Add(0);
        }
    }

    // fldigi src/psk/pskvaricode.cxx varicodetab2, reused in reverse for TX.
    private static readonly uint[] Varicode =
    [
        0x2AB, 0x2DB, 0x2ED, 0x377, 0x2EB, 0x35F, 0x2EF, 0x2FD,
        0x2FF, 0x0EF, 0x01D, 0x36F, 0x2DD, 0x01F, 0x375, 0x3AB,
        0x2F7, 0x2F5, 0x3AD, 0x3AF, 0x35B, 0x36B, 0x36D, 0x357,
        0x37B, 0x37D, 0x3B7, 0x355, 0x35D, 0x3BB, 0x2FB, 0x37F,
        0x001, 0x1FF, 0x15F, 0x1F5, 0x1DB, 0x2D5, 0x2BB, 0x17F,
        0x0FB, 0x0F7, 0x16F, 0x1DF, 0x075, 0x035, 0x057, 0x1AF,
        0x0B7, 0x0BD, 0x0ED, 0x0FF, 0x177, 0x15B, 0x16B, 0x1AD,
        0x1AB, 0x1B7, 0x0F5, 0x1BD, 0x1ED, 0x055, 0x1D7, 0x2AF,
        0x2BD, 0x07D, 0x0EB, 0x0AD, 0x0B5, 0x077, 0x0DB, 0x0FD,
        0x155, 0x07F, 0x1FD, 0x17D, 0x0D7, 0x0BB, 0x0DD, 0x0AB,
        0x0D5, 0x1DD, 0x0AF, 0x06F, 0x06D, 0x157, 0x1B5, 0x15D,
        0x175, 0x17B, 0x2AD, 0x1F7, 0x1EF, 0x1FB, 0x2BF, 0x16D,
        0x2DF, 0x00B, 0x05F, 0x02F, 0x02D, 0x003, 0x03D, 0x05B,
        0x02B, 0x00D, 0x1EB, 0x0BF, 0x01B, 0x03B, 0x00F, 0x007,
        0x03F, 0x1BF, 0x015, 0x017, 0x005, 0x037, 0x07B, 0x06B,
        0x0DF, 0x05D, 0x1D5, 0x2B7, 0x1BB, 0x2B5, 0x2D7, 0x3B5,
        0x3BD, 0x3BF, 0x3D5, 0x3D7, 0x3DB, 0x3DD, 0x3DF, 0x3EB,
        0x3ED, 0x3EF, 0x3F5, 0x3F7, 0x3FB, 0x3FD, 0x3FF, 0x555,
        0x557, 0x55B, 0x55D, 0x55F, 0x56B, 0x56D, 0x56F, 0x575,
        0x577, 0x57B, 0x57D, 0x57F, 0x5AB, 0x5AD, 0x5AF, 0x5B5,
        0x5B7, 0x5BB, 0x5BD, 0x5BF, 0x5D5, 0x5D7, 0x5DB, 0x5DD,
        0x5DF, 0x5EB, 0x5ED, 0x5EF, 0x5F5, 0x5F7, 0x5FB, 0x5FD,
        0x5FF, 0x6AB, 0x6AD, 0x6AF, 0x6B5, 0x6B7, 0x6BB, 0x6BD,
        0x6BF, 0x6D5, 0x6D7, 0x6DB, 0x6DD, 0x6DF, 0x6EB, 0x6ED,
        0x6EF, 0x6F5, 0x6F7, 0x6FB, 0x6FD, 0x6FF, 0x755, 0x757,
        0x75B, 0x75D, 0x75F, 0x76B, 0x76D, 0x76F, 0x775, 0x777,
        0x77B, 0x77D, 0x77F, 0x7AB, 0x7AD, 0x7AF, 0x7B5, 0x7B7,
        0x7BB, 0x7BD, 0x7BF, 0x7D5, 0x7D7, 0x7DB, 0x7DD, 0x7DF,
        0x7EB, 0x7ED, 0x7EF, 0x7F5, 0x7F7, 0x7FB, 0x7FD, 0x7FF,
        0xAAB, 0xAAD, 0xAAF, 0xAB5, 0xAB7, 0xABB, 0xABD, 0xABF,
        0xAD5, 0xAD7, 0xADB, 0xADD, 0xADF, 0xAEB, 0xAED, 0xAEF,
        0xAF5, 0xAF7, 0xAFB, 0xAFD, 0xAFF, 0xB55, 0xB57, 0xB5B
    ];
}
