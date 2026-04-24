using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ShackStack.DecoderHost.Sstv.Core;

internal static class SstvAudioMath
{
    // MMSSTV's CSSTVDEM::Do receives signed PCM-scale samples and marks
    // overflow outside +/-24578. Keep that boundary explicit at the sidecar
    // decoder seam so the source-shaped demod path is not fed normalized audio.
    public const double MmsstvPcmPeak = 24578.0;

    public static float[] DecodeBase64FloatSamples(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        if (bytes.Length == 0)
        {
            return [];
        }

        var samples = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        return samples;
    }

    public static float[] ToMono(float[] samples, int channels)
    {
        if (channels <= 1 || samples.Length == 0)
        {
            return samples;
        }

        var frameCount = samples.Length / channels;
        var mono = new float[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sum = 0.0f;
            var offset = frame * channels;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += samples[offset + channel];
            }

            mono[frame] = sum / channels;
        }

        return mono;
    }

    public static double ToMmsstvPcmScale(float normalizedSample)
        => Math.Clamp(normalizedSample, -1.0f, 1.0f) * MmsstvPcmPeak;

    public static float FromMmsstvPcmScale(double pcmSample)
        => (float)Math.Clamp(pcmSample / MmsstvPcmPeak, -1.0, 1.0);

    public static float[] Resample(float[] input, int sampleRate, int targetRate)
    {
        if (input.Length == 0 || sampleRate <= 0 || sampleRate == targetRate)
        {
            return input;
        }

        var outputLength = Math.Max(1, (int)Math.Round(input.Length * (targetRate / (double)sampleRate)));
        var output = new float[outputLength];
        if (input.Length == 1)
        {
            Array.Fill(output, input[0]);
            return output;
        }

        for (var i = 0; i < outputLength; i++)
        {
            var sourcePosition = (double)i * (input.Length - 1) / Math.Max(1, outputLength - 1);
            var left = Math.Clamp((int)Math.Floor(sourcePosition), 0, input.Length - 1);
            var right = Math.Min(input.Length - 1, left + 1);
            var fraction = (float)(sourcePosition - left);
            output[i] = input[left] + ((input[right] - input[left]) * fraction);
        }

        return output;
    }

    public static double TonePower(ReadOnlySpan<float> block, int sampleRate, double freqHz)
    {
        if (block.Length == 0)
        {
            return 0.0;
        }

        var coeff = 2.0 * Math.Cos(2.0 * Math.PI * freqHz / sampleRate);
        double q0 = 0.0, q1 = 0.0, q2 = 0.0;
        for (var i = 0; i < block.Length; i++)
        {
            q0 = coeff * q1 - q2 + block[i];
            q2 = q1;
            q1 = q0;
        }

        return (q1 * q1) + (q2 * q2) - (coeff * q1 * q2);
    }

    public static int EstimateSignalLevelPercent(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return 0;
        }

        var peak = 0.0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var value = MathF.Abs(samples[i]);
            if (value > peak)
            {
                peak = value;
            }
        }

        return Math.Clamp((int)MathF.Round(peak * 100.0f), 0, 100);
    }

    public static double[] InstantaneousFrequency(ReadOnlySpan<float> segment, int sampleRate)
    {
        if (segment.Length < 8)
        {
            return [];
        }

        var analytic = BuildAnalyticSignal(segment);
        var phase = new double[analytic.Length];
        for (var i = 0; i < analytic.Length; i++)
        {
            phase[i] = Math.Atan2(analytic[i].Imaginary, analytic[i].Real);
        }

        UnwrapPhase(phase);
        var frequency = new double[Math.Max(0, phase.Length - 1)];
        for (var i = 1; i < phase.Length; i++)
        {
            var inst = (phase[i] - phase[i - 1]) * sampleRate / (2.0 * Math.PI);
            frequency[i - 1] = Math.Clamp(inst, 1000.0, 2500.0);
        }

        SmoothInPlace(frequency);
        return frequency;
    }

    private static Complex[] BuildAnalyticSignal(ReadOnlySpan<float> segment)
    {
        var spectrum = new Complex[segment.Length];
        for (var i = 0; i < segment.Length; i++)
        {
            spectrum[i] = new Complex(segment[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);

        if (spectrum.Length > 0)
        {
            for (var i = 1; i < spectrum.Length / 2; i++)
            {
                spectrum[i] *= 2.0;
            }

            var start = (spectrum.Length / 2) + 1;
            for (var i = start; i < spectrum.Length; i++)
            {
                spectrum[i] = Complex.Zero;
            }
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        return spectrum;
    }

    private static void UnwrapPhase(double[] phase)
    {
        if (phase.Length < 2)
        {
            return;
        }

        var correction = 0.0;
        for (var i = 1; i < phase.Length; i++)
        {
            var delta = phase[i] - phase[i - 1];
            if (delta > Math.PI)
            {
                correction -= 2.0 * Math.PI;
            }
            else if (delta < -Math.PI)
            {
                correction += 2.0 * Math.PI;
            }

            phase[i] += correction;
        }
    }

    private static void SmoothInPlace(double[] values)
    {
        if (values.Length < 5)
        {
            return;
        }

        var source = (double[])values.Clone();
        var kernel = new[] { 0.08, 0.24, 0.36, 0.24, 0.08 };
        for (var i = 0; i < values.Length; i++)
        {
            var sum = 0.0;
            for (var k = -2; k <= 2; k++)
            {
                var index = Math.Clamp(i + k, 0, source.Length - 1);
                sum += source[index] * kernel[k + 2];
            }

            values[i] = sum;
        }
    }
}
