using System.Collections.Generic;

namespace ShackStack.DecoderHost.GplWsjtx.Ft4;

internal sealed class Ft4CycleBuffer
{
    private readonly List<float> _pending = new(Ft4Constants.InputSamplesPerCycle * 2);
    private int? _trimSamplesBeforeFirstCycle;

    public int PendingSamples => _pending.Count;

    public int TrimRemainingSamples => Math.Max(0, _trimSamplesBeforeFirstCycle ?? 0);

    public void Reset()
    {
        _pending.Clear();
        _trimSamplesBeforeFirstCycle = null;
    }

    public IReadOnlyList<float[]> Append(float[] interleavedSamples, int sampleRate, int channels, DateTimeOffset utcNow)
    {
        if (sampleRate != Ft4Constants.InputSampleRate || channels <= 0)
        {
            return Array.Empty<float[]>();
        }

        if (_trimSamplesBeforeFirstCycle is null)
        {
            _trimSamplesBeforeFirstCycle = SamplesUntilNextCycleBoundary(utcNow);
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
        while (_pending.Count >= Ft4Constants.InputSamplesPerCycle)
        {
            var cycle = _pending.GetRange(0, Ft4Constants.InputFrameSamples).ToArray();
            _pending.RemoveRange(0, Ft4Constants.InputSamplesPerCycle);
            cycles.Add(cycle);
        }

        return cycles;
    }

    private static int SamplesUntilNextCycleBoundary(DateTimeOffset utcNow)
    {
        var cycleTicks = (long)(TimeSpan.TicksPerSecond * Ft4Constants.CycleSeconds);
        var ticksIntoCycle = utcNow.UtcTicks % cycleTicks;
        var ticksUntilNext = ticksIntoCycle == 0 ? 0 : cycleTicks - ticksIntoCycle;
        var secondsUntilNext = ticksUntilNext / (double)TimeSpan.TicksPerSecond;
        return (int)Math.Round(secondsUntilNext * Ft4Constants.InputSampleRate, MidpointRounding.AwayFromZero);
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
