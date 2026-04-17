using System.Collections.Generic;

namespace ShackStack.DecoderHost.GplWsjtx;

internal sealed class GenericCycleBuffer
{
    private readonly List<float> _pending = new();
    private int? _trimSamplesBeforeFirstCycle;
    private int _inputSamplesPerCycle;

    public int PendingSamples => _pending.Count;

    public int TrimRemainingSamples => Math.Max(0, _trimSamplesBeforeFirstCycle ?? 0);

    public void Reset(int inputSamplesPerCycle)
    {
        _pending.Clear();
        _trimSamplesBeforeFirstCycle = null;
        _inputSamplesPerCycle = inputSamplesPerCycle;
    }

    public IReadOnlyList<float[]> Append(float[] interleavedSamples, int sampleRate, int channels, DateTimeOffset utcNow)
    {
        if (sampleRate <= 0 || channels <= 0 || _inputSamplesPerCycle <= 0)
        {
            return Array.Empty<float[]>();
        }

        if (_trimSamplesBeforeFirstCycle is null)
        {
            _trimSamplesBeforeFirstCycle = SamplesUntilNextCycleBoundary(utcNow, sampleRate, _inputSamplesPerCycle);
        }

        DownmixToMono(interleavedSamples, channels, _pending);

        if (_trimSamplesBeforeFirstCycle > 0)
        {
            var trim = Math.Min(_trimSamplesBeforeFirstCycle.Value, _pending.Count);
            if (trim > 0)
            {
                _pending.RemoveRange(0, trim);
                _trimSamplesBeforeFirstCycle -= trim;
            }
        }

        if (_trimSamplesBeforeFirstCycle > 0)
        {
            return Array.Empty<float[]>();
        }

        var cycles = new List<float[]>();
        while (_pending.Count >= _inputSamplesPerCycle)
        {
            var cycle = _pending.GetRange(0, _inputSamplesPerCycle).ToArray();
            _pending.RemoveRange(0, _inputSamplesPerCycle);
            cycles.Add(cycle);
        }

        return cycles;
    }

    private static int SamplesUntilNextCycleBoundary(DateTimeOffset utcNow, int sampleRate, int inputSamplesPerCycle)
    {
        var cycleSeconds = inputSamplesPerCycle / (double)sampleRate;
        var cycleTicks = (long)Math.Round(TimeSpan.TicksPerSecond * cycleSeconds, MidpointRounding.AwayFromZero);
        var ticksIntoCycle = utcNow.UtcTicks % cycleTicks;
        var ticksUntilNext = ticksIntoCycle == 0 ? 0 : cycleTicks - ticksIntoCycle;
        var secondsUntilNext = ticksUntilNext / (double)TimeSpan.TicksPerSecond;
        return (int)Math.Round(secondsUntilNext * sampleRate, MidpointRounding.AwayFromZero);
    }

    private static void DownmixToMono(float[] samples, int channels, List<float> destination)
    {
        if (channels == 1)
        {
            destination.AddRange(samples);
            return;
        }

        for (var i = 0; i + channels - 1 < samples.Length; i += channels)
        {
            double sum = 0;
            for (var ch = 0; ch < channels; ch++)
            {
                sum += samples[i + ch];
            }

            destination.Add((float)(sum / channels));
        }
    }
}
