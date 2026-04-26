namespace ShackStack.DecoderHost.Sstv.Core;

internal sealed class MmsstvFskIdDecoder
{
    private const int FskGuardMs = 100;
    private const int FskIntervalMs = 22;
    private const double MarkHz = 1900.0;
    private const double SpaceHz = 2100.0;
    private const double MinGuardRatio = 1.35;
    private const double MinSymbolRatio = 1.08;
    private readonly int _sampleRate;
    private readonly List<float> _samples = [];
    private string? _lastCallsign;
    private int _lastAcceptedAbsoluteSample = -1;
    private int _absoluteSampleBase;

    public MmsstvFskIdDecoder(int sampleRate)
    {
        _sampleRate = sampleRate;
    }

    public string? LastCallsign => _lastCallsign;

    public void Reset()
    {
        _samples.Clear();
        _lastCallsign = null;
        _lastAcceptedAbsoluteSample = -1;
        _absoluteSampleBase = 0;
    }

    public string? Process(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return null;
        }

        Append(samples);
        return TryDecode();
    }

    private void Append(ReadOnlySpan<float> samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            _samples.Add(samples[i]);
        }

        var maxSamples = _sampleRate * 12;
        if (_samples.Count <= maxSamples)
        {
            return;
        }

        var remove = _samples.Count - maxSamples;
        _samples.RemoveRange(0, remove);
        _absoluteSampleBase += remove;
    }

    private string? TryDecode()
    {
        var guardSamples = MillisecondsToSamples(FskGuardMs);
        var intervalSamples = MillisecondsToSamples(FskIntervalMs);
        var minPayloadSamples = intervalSamples * 6 * 4;
        if (_samples.Count < guardSamples + intervalSamples + minPayloadSamples)
        {
            return null;
        }

        var stepSamples = Math.Max(1, MillisecondsToSamples(4));
        var latestStart = _samples.Count - guardSamples - intervalSamples - minPayloadSamples;
        for (var start = 0; start <= latestStart; start += stepSamples)
        {
            var absoluteStart = _absoluteSampleBase + start;
            if (_lastAcceptedAbsoluteSample >= 0 && absoluteStart <= _lastAcceptedAbsoluteSample + _sampleRate)
            {
                continue;
            }

            if (!IsSpaceGuard(start, guardSamples))
            {
                continue;
            }

            var leadStart = start + guardSamples;
            if (ReadBit(leadStart, intervalSamples) != 1)
            {
                continue;
            }

            var symbolStart = leadStart + intervalSamples;
            var callsign = TryDecodePayload(symbolStart, intervalSamples);
            if (callsign is null)
            {
                continue;
            }

            _lastCallsign = callsign;
            _lastAcceptedAbsoluteSample = absoluteStart;
            return callsign;
        }

        return null;
    }

    private string? TryDecodePayload(int start, int intervalSamples)
    {
        var offset = start;
        if (!TryReadSymbol(offset, intervalSamples, out var symbol) || symbol != 0x2a)
        {
            return null;
        }

        offset += intervalSamples * 6;
        var checksum = 0;
        Span<char> buffer = stackalloc char[17];
        var count = 0;
        while (count < 17)
        {
            if (!TryReadSymbol(offset, intervalSamples, out symbol))
            {
                return null;
            }

            offset += intervalSamples * 6;
            if (symbol == 0x01)
            {
                if (count == 0 || !TryReadSymbol(offset, intervalSamples, out var expected))
                {
                    return null;
                }

                if ((checksum & 0x3f) != expected)
                {
                    return null;
                }

                var callsign = new string(buffer[..count]).Trim().ToUpperInvariant();
                return LooksLikeCallsign(callsign) ? callsign : null;
            }

            if (symbol > 0x3f)
            {
                return null;
            }

            checksum ^= symbol;
            buffer[count++] = (char)(symbol + 0x20);
        }

        return null;
    }

    private bool TryReadSymbol(int start, int intervalSamples, out int symbol)
    {
        symbol = 0;
        for (var bit = 0; bit < 6; bit++)
        {
            var value = ReadBit(start + (bit * intervalSamples), intervalSamples);
            if (value < 0)
            {
                return false;
            }

            symbol |= value << bit;
        }

        return true;
    }

    private int ReadBit(int start, int intervalSamples)
    {
        if (start < 0 || start + intervalSamples > _samples.Count)
        {
            return -1;
        }

        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_samples).Slice(start, intervalSamples);
        var mark = SstvAudioMath.TonePower(span, _sampleRate, MarkHz);
        var space = SstvAudioMath.TonePower(span, _sampleRate, SpaceHz);
        var ratio = Math.Max(mark, space) / Math.Max(1e-9, Math.Min(mark, space));
        if (ratio < MinSymbolRatio)
        {
            return -1;
        }

        return mark > space ? 1 : 0;
    }

    private bool IsSpaceGuard(int start, int guardSamples)
    {
        if (start < 0 || start + guardSamples > _samples.Count)
        {
            return false;
        }

        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_samples).Slice(start, guardSamples);
        var mark = SstvAudioMath.TonePower(span, _sampleRate, MarkHz);
        var space = SstvAudioMath.TonePower(span, _sampleRate, SpaceHz);
        return space > mark * MinGuardRatio;
    }

    private int MillisecondsToSamples(int ms)
        => Math.Max(1, (int)Math.Round(ms * _sampleRate / 1000.0));

    private static bool LooksLikeCallsign(string value)
    {
        if (value.Length is < 3 or > 16)
        {
            return false;
        }

        var hasDigit = false;
        var hasLetter = false;
        foreach (var c in value)
        {
            if (char.IsAsciiDigit(c))
            {
                hasDigit = true;
            }
            else if (char.IsAsciiLetterUpper(c))
            {
                hasLetter = true;
            }
            else if (c != '/')
            {
                return false;
            }
        }

        return hasDigit && hasLetter;
    }
}
