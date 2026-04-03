using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Infrastructure.Radio.Icom;

internal sealed class IcomScopeAssembler
{
    private const int MaxAmplitude = 160;

    private readonly Dictionary<int, byte[]> _chunks = [];
    private int _centerHz;
    private int _spanHz;
    private int _expectedChunks;

    public WaterfallRow? TryProcess(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return null;
        }

        var subcommand = payload[0];
        if (subcommand != 0x00)
        {
            return null;
        }

        var seqNum = DecodePackedDecimal(payload[2]);
        var totalSeq = DecodePackedDecimal(payload[3]);
        if (seqNum <= 0 || totalSeq <= 0)
        {
            return null;
        }

        if (totalSeq == 1)
        {
            return TryProcessSinglePacket(payload);
        }

        return TryProcessChunkedPacket(payload, seqNum, totalSeq);
    }

    private WaterfallRow? TryProcessSinglePacket(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 20)
        {
            return null;
        }

        _centerHz = BcdToHz(payload.Slice(5, 5));
        _spanHz = BcdToHz(payload.Slice(10, 5));
        var waveform = payload[15..];
        return BuildRow(waveform, _centerHz, _spanHz);
    }

    private WaterfallRow? TryProcessChunkedPacket(ReadOnlySpan<byte> payload, int seqNum, int totalSeq)
    {
        if (seqNum == 1)
        {
            if (_chunks.Count > 0)
            {
                _chunks.Clear();
            }

            _expectedChunks = totalSeq;

            if (payload.Length >= 15)
            {
                _centerHz = BcdToHz(payload.Slice(5, 5));
                _spanHz = BcdToHz(payload.Slice(10, 5));
            }

            return null;
        }

        if (payload.Length <= 4)
        {
            return null;
        }

        _chunks[seqNum] = payload[4..].ToArray();

        if (seqNum != (_expectedChunks == 0 ? totalSeq : _expectedChunks))
        {
            return null;
        }

        var expected = _expectedChunks == 0 ? totalSeq : _expectedChunks;
        var waveBytes = new List<byte>(512);
        for (var i = 2; i <= expected; i++)
        {
            if (_chunks.TryGetValue(i, out var chunk))
            {
                waveBytes.AddRange(chunk);
            }
        }

        _chunks.Clear();
        _expectedChunks = 0;

        return BuildRow(waveBytes.ToArray(), _centerHz, _spanHz);
    }

    private static WaterfallRow? BuildRow(ReadOnlySpan<byte> waveform, int centerHz, int spanHz)
    {
        if (waveform.Length == 0)
        {
            return null;
        }

        var bins = new float[waveform.Length];
        for (var i = 0; i < waveform.Length; i++)
        {
            bins[i] = Math.Clamp(waveform[i] / (float)MaxAmplitude, 0f, 1f);
        }

        return new WaterfallRow(bins, centerHz, spanHz);
    }

    private static int DecodePackedDecimal(byte value) => ((value >> 4) & 0x0F) * 10 + (value & 0x0F);

    private static int BcdToHz(ReadOnlySpan<byte> bcdBytes)
    {
        Span<char> chars = stackalloc char[bcdBytes.Length * 2];
        var offset = 0;
        for (var i = bcdBytes.Length - 1; i >= 0; i--)
        {
            var value = bcdBytes[i];
            chars[offset++] = GetHexChar((value >> 4) & 0x0F);
            chars[offset++] = GetHexChar(value & 0x0F);
        }

        return int.TryParse(chars[..offset], out var hz) ? hz : 0;
    }

    private static char GetHexChar(int value) => (char)(value < 10 ? '0' + value : 'A' + (value - 10));
}
