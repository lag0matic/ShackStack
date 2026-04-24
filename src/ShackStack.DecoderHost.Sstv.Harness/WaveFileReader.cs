namespace ShackStack.DecoderHost.Sstv.Harness;

internal sealed record WaveClip(float[] Samples, int SampleRate, int Channels);

internal static class WaveFileReader
{
    public static WaveClip ReadMonoFloat(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "RIFF")
        {
            throw new InvalidDataException("Expected RIFF WAV.");
        }

        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE")
        {
            throw new InvalidDataException("Expected WAVE file.");
        }

        short channels = 0;
        int sampleRate = 0;
        short bitsPerSample = 0;
        byte[]? data = null;

        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadInt32();
            var chunkEnd = stream.Position + chunkSize;
            if (chunkId == "fmt ")
            {
                var audioFormat = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bitsPerSample = reader.ReadInt16();
                if (audioFormat != 1 || bitsPerSample != 16)
                {
                    throw new NotSupportedException($"Only PCM16 WAV is supported. Format {audioFormat}, bits {bitsPerSample}.");
                }
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(chunkSize);
            }

            stream.Position = chunkEnd + (chunkSize & 1);
        }

        if (channels <= 0 || sampleRate <= 0 || bitsPerSample != 16 || data is null)
        {
            throw new InvalidDataException("WAV is missing fmt or data chunk.");
        }

        var frameCount = data.Length / (channels * sizeof(short));
        var samples = new float[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sum = 0.0;
            for (var channel = 0; channel < channels; channel++)
            {
                var byteOffset = ((frame * channels) + channel) * sizeof(short);
                sum += BitConverter.ToInt16(data, byteOffset) / 32768.0;
            }

            samples[frame] = (float)(sum / channels);
        }

        return new WaveClip(samples, sampleRate, channels);
    }
}
