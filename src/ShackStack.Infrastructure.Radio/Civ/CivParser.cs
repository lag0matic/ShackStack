using System.Collections.Generic;

namespace ShackStack.Infrastructure.Radio.Civ;

public sealed class CivParser
{
    private const byte Preamble = 0xFE;
    private const byte Terminator = 0xFD;
    private readonly List<byte> _buffer = [];
    private bool _armed;

    public IReadOnlyList<CivFrame> Feed(ReadOnlySpan<byte> data)
    {
        var frames = new List<CivFrame>();

        foreach (var value in data)
        {
            if (!_armed)
            {
                if (value == Preamble)
                {
                    _buffer.Add(value);
                    if (_buffer.Count >= 2 && _buffer[^2] == Preamble)
                    {
                        _armed = true;
                    }
                }
                else
                {
                    _buffer.Clear();
                }

                continue;
            }

            _buffer.Add(value);

            if (value == Terminator)
            {
                var frame = TryBuildFrame([.. _buffer]);
                if (frame is not null)
                {
                    frames.Add(frame);
                }

                ResetForNextFrame();
            }

            if (_buffer.Count > 4096)
            {
                ResetForNextFrame();
            }
        }

        return frames;
    }

    private static CivFrame? TryBuildFrame(byte[] raw)
    {
        if (raw.Length < 6)
        {
            return null;
        }

        if (raw[0] != Preamble || raw[1] != Preamble || raw[^1] != Terminator)
        {
            return null;
        }

        var destination = raw[2];
        var source = raw[3];
        var command = raw[4];

        byte? subCommand = null;
        var payload = raw.Length > 6 ? raw[5..^1] : [];

        return new CivFrame(raw, destination, source, command, subCommand, payload);
    }

    private void ResetForNextFrame()
    {
        _buffer.Clear();
        _armed = false;
    }
}
