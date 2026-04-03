namespace ShackStack.Infrastructure.Radio.Civ;

public static class CivCodec
{
    public static byte[] Encode(byte destination, byte source, byte command, ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[6 + payload.Length];
        bytes[0] = 0xFE;
        bytes[1] = 0xFE;
        bytes[2] = destination;
        bytes[3] = source;
        bytes[4] = command;
        payload.CopyTo(bytes.AsSpan(5));
        bytes[^1] = 0xFD;
        return bytes;
    }
}
