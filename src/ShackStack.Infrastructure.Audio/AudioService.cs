using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;
using ShackStack.Infrastructure.Audio.WindowsAudio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ShackStack.Infrastructure.Audio;

public sealed class AudioService : IAudioService, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly WindowsAudioDeviceCatalog _deviceCatalog = new();
    private readonly AudioDeviceNotificationMonitor _notificationMonitor = new();
    private readonly SimpleSubject<AudioBuffer> _receiveStream = new();
    private readonly SimpleSubject<AudioLevels> _levelStream = new();
    private readonly object _sync = new();
    private WasapiCapture? _rxCapture;
    private WasapiOut? _rxPlayback;
    private BufferedWaveProvider? _rxBuffer;
    private WasapiCapture? _micCapture;
    private WasapiOut? _txPlayback;
    private BufferedWaveProvider? _txBuffer;
    private WasapiOut? _micMonitorPlayback;
    private BufferedWaveProvider? _micMonitorBuffer;
    private float _monitorVolume = 0.75f;
    private float _micGain = 1.0f;
    private float _voiceCompression = 0.0f;
    private bool _micMonitorEnabled;
    private float _micMonitorLevel = 0.5f;
    private float _rxLevel;
    private float _txLevel;
    private float _micLevel;

    public AudioService()
    {
        _ = _notificationMonitor;
    }

    public IObservable<AudioBuffer> ReceiveStream => _receiveStream;

    public IObservable<AudioLevels> LevelStream => _levelStream;

    public Task<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(CancellationToken ct) =>
        Task.FromResult(_deviceCatalog.Enumerate());

    public Task StartReceiveAsync(AudioRoute route, CancellationToken ct)
    {
        lock (_sync)
        {
            StopReceiveLocked();

            if (string.IsNullOrWhiteSpace(route.RxDeviceId))
            {
                throw new InvalidOperationException("RX audio device is not configured.");
            }

            if (string.IsNullOrWhiteSpace(route.MonitorDeviceId))
            {
                throw new InvalidOperationException("Monitor audio device is not configured.");
            }

            var inputDevice = _enumerator.GetDevice(route.RxDeviceId);
            var outputDevice = _enumerator.GetDevice(route.MonitorDeviceId);

            _rxCapture = new WasapiCapture(inputDevice);
            _rxBuffer = new BufferedWaveProvider(_rxCapture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
            };
            _rxPlayback = new WasapiOut(outputDevice, AudioClientShareMode.Shared, false, 100);
            _rxPlayback.Init(_rxBuffer);

            _rxCapture.DataAvailable += OnReceiveCaptureDataAvailable;
            _rxCapture.RecordingStopped += OnReceiveCaptureStopped;

            _rxPlayback.Play();
            _rxCapture.StartRecording();
        }

        return Task.CompletedTask;
    }

    public Task StopReceiveAsync(CancellationToken ct)
    {
        lock (_sync)
        {
            StopReceiveLocked();
        }

        return Task.CompletedTask;
    }

    public Task StartTransmitAsync(AudioRoute route, CancellationToken ct)
    {
        lock (_sync)
        {
            StopTransmitLocked();

            if (string.IsNullOrWhiteSpace(route.MicDeviceId))
            {
                throw new InvalidOperationException("Microphone audio device is not configured.");
            }

            if (string.IsNullOrWhiteSpace(route.TxDeviceId))
            {
                throw new InvalidOperationException("TX audio device is not configured.");
            }

            var micDevice = _enumerator.GetDevice(route.MicDeviceId);
            var txDevice = _enumerator.GetDevice(route.TxDeviceId);

            _micCapture = new WasapiCapture(micDevice);
            _txBuffer = new BufferedWaveProvider(_micCapture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
            };
            _txPlayback = new WasapiOut(txDevice, AudioClientShareMode.Shared, false, 80);
            _txPlayback.Init(_txBuffer);

            if (_micMonitorEnabled && !string.IsNullOrWhiteSpace(route.MonitorDeviceId))
            {
                var monitorDevice = _enumerator.GetDevice(route.MonitorDeviceId);
                _micMonitorBuffer = new BufferedWaveProvider(_micCapture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                };
                _micMonitorPlayback = new WasapiOut(monitorDevice, AudioClientShareMode.Shared, false, 80);
                _micMonitorPlayback.Init(_micMonitorBuffer);
            }

            _micCapture.DataAvailable += OnMicCaptureDataAvailable;
            _micCapture.RecordingStopped += OnMicCaptureStopped;

            _txPlayback.Play();
            _micMonitorPlayback?.Play();
            _micCapture.StartRecording();
        }

        return Task.CompletedTask;
    }

    public Task StartTransmitPcmAsync(AudioRoute route, Pcm16AudioClip clip, CancellationToken ct)
    {
        lock (_sync)
        {
            StopTransmitLocked();

            if (clip.PcmBytes.Length == 0)
            {
                throw new InvalidOperationException("PCM transmit clip is empty.");
            }

            if (string.IsNullOrWhiteSpace(route.TxDeviceId))
            {
                throw new InvalidOperationException("TX audio device is not configured.");
            }

            var txDevice = _enumerator.GetDevice(route.TxDeviceId);
            var format = new WaveFormat(clip.SampleRate, 16, clip.Channels);

            _txBuffer = new BufferedWaveProvider(format)
            {
                DiscardOnBufferOverflow = false,
                BufferLength = Math.Max(clip.PcmBytes.Length, format.AverageBytesPerSecond)
            };
            _txBuffer.AddSamples(clip.PcmBytes, 0, clip.PcmBytes.Length);

            _txPlayback = new WasapiOut(txDevice, AudioClientShareMode.Shared, false, 80);
            _txPlayback.Init(_txBuffer);
            _txPlayback.Play();
        }

        return Task.CompletedTask;
    }

    public Task StopTransmitAsync(CancellationToken ct)
    {
        lock (_sync)
        {
            StopTransmitLocked();
        }

        return Task.CompletedTask;
    }

    public Task SetMonitorVolumeAsync(float volume, CancellationToken ct)
    {
        lock (_sync)
        {
            _monitorVolume = Math.Clamp(volume, 0f, 1f);
        }

        return Task.CompletedTask;
    }

    public Task SetMicGainAsync(float gain, CancellationToken ct)
    {
        lock (_sync)
        {
            _micGain = Math.Clamp(gain, 0f, 3f);
        }

        return Task.CompletedTask;
    }

    public Task SetVoiceCompressionAsync(float amount, CancellationToken ct)
    {
        lock (_sync)
        {
            _voiceCompression = Math.Clamp(amount, 0f, 1f);
        }

        return Task.CompletedTask;
    }

    public Task SetMicMonitorAsync(bool enabled, float level, CancellationToken ct)
    {
        lock (_sync)
        {
            _micMonitorEnabled = enabled;
            _micMonitorLevel = Math.Clamp(level, 0f, 1f);
        }

        return Task.CompletedTask;
    }

    private void OnReceiveCaptureDataAvailable(object? sender, WaveInEventArgs e)
    {
        BufferedWaveProvider? buffer;
        WasapiCapture? capture;
        float monitorVolume;

        lock (_sync)
        {
            buffer = _rxBuffer;
            capture = _rxCapture;
            monitorVolume = _monitorVolume;
        }

        if (buffer is null || capture is null || e.BytesRecorded <= 0)
        {
            return;
        }

        var monitorBuffer = ApplyMonitorVolume(e.Buffer, e.BytesRecorded, capture.WaveFormat, monitorVolume);
        buffer.AddSamples(monitorBuffer, 0, monitorBuffer.Length);

        var samples = ConvertToFloatSamples(e.Buffer, e.BytesRecorded, capture.WaveFormat);
        if (samples.Length == 0)
        {
            return;
        }

        _receiveStream.OnNext(new AudioBuffer(samples, capture.WaveFormat.SampleRate, capture.WaveFormat.Channels));

        var peak = samples.Max(static s => MathF.Abs(s));
        lock (_sync)
        {
            _rxLevel = peak;
            PublishLevelsLocked();
        }
    }

    private void OnReceiveCaptureStopped(object? sender, StoppedEventArgs e)
    {
        lock (_sync)
        {
            StopReceiveLocked();
        }
    }

    private void OnMicCaptureDataAvailable(object? sender, WaveInEventArgs e)
    {
        BufferedWaveProvider? txBuffer;
        BufferedWaveProvider? micMonitorBuffer;
        WasapiCapture? micCapture;
        float micGain;
        float compression;
        float micMonitorLevel;
        bool micMonitorEnabled;

        lock (_sync)
        {
            txBuffer = _txBuffer;
            micMonitorBuffer = _micMonitorBuffer;
            micCapture = _micCapture;
            micGain = _micGain;
            compression = _voiceCompression;
            micMonitorLevel = _micMonitorLevel;
            micMonitorEnabled = _micMonitorEnabled;
        }

        if (txBuffer is null || micCapture is null || e.BytesRecorded <= 0)
        {
            return;
        }

        var samples = ConvertToFloatSamples(e.Buffer, e.BytesRecorded, micCapture.WaveFormat);
        if (samples.Length == 0)
        {
            return;
        }

        var micPeak = samples.Max(static s => MathF.Abs(s));
        var processed = ProcessMicSamples(samples, micGain, compression);
        var txPeak = processed.Max(static s => MathF.Abs(s));
        var txBytes = ConvertFloatSamplesToBytes(processed, micCapture.WaveFormat);

        txBuffer.AddSamples(txBytes, 0, txBytes.Length);

        if (micMonitorEnabled && micMonitorBuffer is not null)
        {
            var monitorBytes = ConvertFloatSamplesToBytes(ScaleSamples(processed, micMonitorLevel), micCapture.WaveFormat);
            micMonitorBuffer.AddSamples(monitorBytes, 0, monitorBytes.Length);
        }

        lock (_sync)
        {
            _micLevel = micPeak;
            _txLevel = txPeak;
            PublishLevelsLocked();
        }
    }

    private void OnMicCaptureStopped(object? sender, StoppedEventArgs e)
    {
        lock (_sync)
        {
            StopTransmitLocked();
        }
    }

    private void StopReceiveLocked()
    {
        if (_rxCapture is not null)
        {
            _rxCapture.DataAvailable -= OnReceiveCaptureDataAvailable;
            _rxCapture.RecordingStopped -= OnReceiveCaptureStopped;

            try
            {
                _rxCapture.StopRecording();
            }
            catch
            {
            }

            _rxCapture.Dispose();
            _rxCapture = null;
        }

        if (_rxPlayback is not null)
        {
            try
            {
                _rxPlayback.Stop();
            }
            catch
            {
            }

            _rxPlayback.Dispose();
            _rxPlayback = null;
        }

        _rxBuffer = null;
        _rxLevel = 0f;
        PublishLevelsLocked();
    }

    private void StopTransmitLocked()
    {
        if (_micCapture is not null)
        {
            _micCapture.DataAvailable -= OnMicCaptureDataAvailable;
            _micCapture.RecordingStopped -= OnMicCaptureStopped;

            try
            {
                _micCapture.StopRecording();
            }
            catch
            {
            }

            _micCapture.Dispose();
            _micCapture = null;
        }

        if (_txPlayback is not null)
        {
            try
            {
                _txPlayback.Stop();
            }
            catch
            {
            }

            _txPlayback.Dispose();
            _txPlayback = null;
        }

        if (_micMonitorPlayback is not null)
        {
            try
            {
                _micMonitorPlayback.Stop();
            }
            catch
            {
            }

            _micMonitorPlayback.Dispose();
            _micMonitorPlayback = null;
        }

        _txBuffer = null;
        _micMonitorBuffer = null;
        _txLevel = 0f;
        _micLevel = 0f;
        PublishLevelsLocked();
    }

    private static float[] ConvertToFloatSamples(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (format.BitsPerSample == 16)
        {
            var sampleCount = bytesRecorded / 2;
            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                var value = BitConverter.ToInt16(buffer, i * 2);
                samples[i] = value / 32768f;
            }

            return samples;
        }

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var sampleCount = bytesRecorded / 4;
            var samples = new float[sampleCount];
            Buffer.BlockCopy(buffer, 0, samples, 0, bytesRecorded);
            return samples;
        }

        return Array.Empty<float>();
    }

    private static byte[] ApplyMonitorVolume(byte[] buffer, int bytesRecorded, WaveFormat format, float volume)
    {
        if (Math.Abs(volume - 1f) < 0.001f)
        {
            var passthrough = new byte[bytesRecorded];
            Buffer.BlockCopy(buffer, 0, passthrough, 0, bytesRecorded);
            return passthrough;
        }

        if (format.BitsPerSample == 16)
        {
            var scaled = new byte[bytesRecorded];
            for (var i = 0; i < bytesRecorded; i += 2)
            {
                var sample = BitConverter.ToInt16(buffer, i);
                var adjusted = (short)Math.Clamp(sample * volume, short.MinValue, short.MaxValue);
                var bytes = BitConverter.GetBytes(adjusted);
                scaled[i] = bytes[0];
                scaled[i + 1] = bytes[1];
            }

            return scaled;
        }

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var scaled = new byte[bytesRecorded];
            for (var i = 0; i < bytesRecorded; i += 4)
            {
                var sample = BitConverter.ToSingle(buffer, i);
                var adjusted = Math.Clamp(sample * volume, -1f, 1f);
                var bytes = BitConverter.GetBytes(adjusted);
                Buffer.BlockCopy(bytes, 0, scaled, i, 4);
            }

            return scaled;
        }

        var fallback = new byte[bytesRecorded];
        Buffer.BlockCopy(buffer, 0, fallback, 0, bytesRecorded);
        return fallback;
    }

    private static float[] ProcessMicSamples(float[] samples, float gain, float compression)
    {
        var processed = new float[samples.Length];
        var threshold = 1f - (compression * 0.65f);
        var ratio = 1f + (compression * 9f);

        for (var i = 0; i < samples.Length; i++)
        {
            var value = samples[i] * gain;
            var abs = MathF.Abs(value);
            if (compression > 0f && abs > threshold)
            {
                var excess = abs - threshold;
                abs = threshold + (excess / ratio);
                value = MathF.Sign(value) * abs;
            }

            processed[i] = Math.Clamp(value, -1f, 1f);
        }

        return processed;
    }

    private static float[] ScaleSamples(float[] samples, float level)
    {
        var scaled = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            scaled[i] = Math.Clamp(samples[i] * level, -1f, 1f);
        }

        return scaled;
    }

    private static byte[] ConvertFloatSamplesToBytes(float[] samples, WaveFormat format)
    {
        if (format.BitsPerSample == 16)
        {
            var bytes = new byte[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
            {
                var sample = (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
                var pair = BitConverter.GetBytes(sample);
                bytes[i * 2] = pair[0];
                bytes[(i * 2) + 1] = pair[1];
            }

            return bytes;
        }

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var bytes = new byte[samples.Length * 4];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        return Array.Empty<byte>();
    }

    private void PublishLevelsLocked()
    {
        _levelStream.OnNext(new AudioLevels(_rxLevel, _txLevel, _micLevel));
    }

    public void Dispose()
    {
        lock (_sync)
        {
            StopReceiveLocked();
            StopTransmitLocked();
        }

        _notificationMonitor.Dispose();
        _enumerator.Dispose();
    }
}
