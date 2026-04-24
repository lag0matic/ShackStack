namespace ShackStack.DecoderHost.Sstv.Core;

public sealed class SstvTransmitClipBuilder
{
    private const double LeadSilenceSeconds = 0.15;
    private const double TrailSilenceSeconds = 0.15;
    private readonly int _sampleRate;

    public SstvTransmitClipBuilder(int sampleRate = SstvWorkingConfig.WorkingSampleRate)
    {
        _sampleRate = sampleRate;
    }

    public SstvTransmitClip Build(string mode, byte[] rgb24, int width, int height, MmsstvTxOptions? options = null)
    {
        if (!MmsstvModeCatalog.TryResolve(mode, out var profile))
        {
            throw new InvalidOperationException($"Unsupported SSTV TX mode '{mode}'.");
        }

        if (rgb24.Length != width * height * 3)
        {
            throw new InvalidOperationException("RGB24 payload length does not match width and height.");
        }

        var preparedRgb = width == profile.Width && height == profile.Height
            ? rgb24
            : ResizeRgb24(rgb24, width, height, profile.Width, profile.Height);

        var tx = MmsstvTxConfiguration.Create(profile, _sampleRate);
        var tonePlan = MmsstvTxSequenceBuilder.BuildImageTones(preparedRgb, tx, options);
        var modulator = new MmsstvTxModulator(_sampleRate);
        var pcm = AddSilencePadding(modulator.RenderQueuedPcm(tonePlan, tx), _sampleRate, LeadSilenceSeconds, TrailSilenceSeconds);
        var pcmBytes = new byte[pcm.Length * 2];

        for (var i = 0; i < pcm.Length; i++)
        {
            var sample = (short)Math.Clamp((int)Math.Round(pcm[i] * short.MaxValue), short.MinValue, short.MaxValue);
            pcmBytes[(i * 2)] = (byte)(sample & 0xff);
            pcmBytes[(i * 2) + 1] = (byte)((sample >> 8) & 0xff);
        }

        return new SstvTransmitClip(pcmBytes, _sampleRate, 1, profile.Name);
    }

    private static float[] AddSilencePadding(float[] audio, int sampleRate, double leadSeconds, double trailSeconds)
    {
        var lead = Math.Max(1, (int)Math.Round(leadSeconds * sampleRate));
        var trail = Math.Max(1, (int)Math.Round(trailSeconds * sampleRate));
        var combined = new float[lead + audio.Length + trail];
        Array.Copy(audio, 0, combined, lead, audio.Length);
        return combined;
    }

    private static byte[] ResizeRgb24(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var resized = new byte[targetWidth * targetHeight * 3];
        for (var y = 0; y < targetHeight; y++)
        {
            var srcY = Math.Clamp((int)Math.Round(y * (sourceHeight - 1) / (double)Math.Max(1, targetHeight - 1)), 0, sourceHeight - 1);
            for (var x = 0; x < targetWidth; x++)
            {
                var srcX = Math.Clamp((int)Math.Round(x * (sourceWidth - 1) / (double)Math.Max(1, targetWidth - 1)), 0, sourceWidth - 1);
                var src = ((srcY * sourceWidth) + srcX) * 3;
                var dst = ((y * targetWidth) + x) * 3;
                resized[dst] = source[src];
                resized[dst + 1] = source[src + 1];
                resized[dst + 2] = source[src + 2];
            }
        }

        return resized;
    }
}

public sealed record SstvTransmitClip(
    byte[] PcmBytes,
    int SampleRate,
    int Channels,
    string ModeName);
