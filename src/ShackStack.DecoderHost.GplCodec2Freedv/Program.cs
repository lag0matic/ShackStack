using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ShackStack.DecoderHost.GplCodec2Freedv;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task Main()
    {
        var worker = new FreeDvWorker();
        worker.EmitTelemetry("FreeDV sidecar ready; waiting for Codec2 runtime");

        string? line;
        while ((line = await Console.In.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                switch (type)
                {
                    case "configure":
                        worker.Configure(root);
                        break;
                    case "start":
                        worker.Start();
                        break;
                    case "startTx":
                        worker.StartTransmit();
                        break;
                    case "stop":
                        worker.Stop();
                        break;
                    case "stopTx":
                        worker.StopTransmit();
                        break;
                    case "reset":
                        worker.Reset();
                        break;
                    case "audio":
                        worker.PushAudio(root);
                        break;
                    case "speech":
                        worker.PushSpeech(root);
                        break;
                    case "shutdown":
                        worker.Stop();
                        return;
                }
            }
            catch (Exception ex)
            {
                worker.EmitTelemetry($"FreeDV sidecar error: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private sealed class FreeDvWorker : IDisposable
    {
        private Codec2FreeDvRuntime? _runtime;
        private RadeFreeDvRuntime? _radeRuntime;
        private string? _lastRuntimeLoadError;
        private string _modeLabel = "700D";
        private string _transmitCallsign = string.Empty;
        private int _rxFrequencyOffsetHz;
        private float _rxFrequencyOffsetPhaseReal = 1.0f;
        private float _rxFrequencyOffsetPhaseImag;
        private bool _isRunning;
        private bool _isTransmitting;
        private int _speechSampleRate = 8000;
        private int _modemSampleRate = 8000;
        private double _lastSignalLevelPercent;
        private int _lastSyncPercent;
        private float _lastSnrDb;
        private string _lastRadeCallsign = string.Empty;
        private readonly List<short> _modemSamples = [];
        private readonly List<short> _speechSamples = [];

        private bool IsRadeMode => string.Equals(_modeLabel, "RADEV1", StringComparison.OrdinalIgnoreCase);

        public void Configure(JsonElement root)
        {
            var requestedMode = root.TryGetProperty("modeLabel", out var modeEl)
                ? NormalizeMode(modeEl.GetString())
                : "700D";
            if (!string.Equals(_modeLabel, requestedMode, StringComparison.OrdinalIgnoreCase))
            {
                _runtime?.Dispose();
                _runtime = null;
                _radeRuntime?.Dispose();
                _radeRuntime = null;
                _modemSamples.Clear();
                _speechSamples.Clear();
            }

            _modeLabel = requestedMode;
            _transmitCallsign = root.TryGetProperty("transmitCallsign", out var callsignEl)
                ? NormalizeCallsign(callsignEl.GetString())
                : string.Empty;
            var requestedOffset = root.TryGetProperty("rxFrequencyOffsetHz", out var offsetEl)
                ? offsetEl.GetInt32()
                : 0;
            requestedOffset = Math.Clamp(requestedOffset, -1000, 1000);
            if (_rxFrequencyOffsetHz != requestedOffset)
            {
                _rxFrequencyOffsetPhaseReal = 1.0f;
                _rxFrequencyOffsetPhaseImag = 0.0f;
            }

            _rxFrequencyOffsetHz = requestedOffset;

            TryLoadRuntime();
            EmitTelemetry(IsRadeMode
                ? _radeRuntime?.IsReady == true
                    ? $"Configured FreeDV RADEV1 using RADE runtime | RX offset {_rxFrequencyOffsetHz:+0;-0;0} Hz"
                    : $"RADE runtime missing. {_lastRuntimeLoadError ?? "Bundle librade.dll and lpcnet_demo.exe."}"
                : _runtime?.IsReady == true
                    ? $"Configured FreeDV {_modeLabel} using Codec2 runtime | RX offset {_rxFrequencyOffsetHz:+0;-0;0} Hz"
                    : $"Codec2 runtime missing. {_lastRuntimeLoadError ?? "Set SHACKSTACK_FREEDV_CODEC2_PATH or bundle codec2.dll/libcodec2.dll."}");
        }

        public void Start()
        {
            _isRunning = true;
            TryLoadRuntime();
            EmitTelemetry(IsRadeMode
                ? _radeRuntime?.IsReady == true ? "FreeDV RADEV1 RX running" : "FreeDV RADEV1 RX waiting: RADE runtime not found"
                : _runtime?.IsReady == true ? $"FreeDV RX running ({_modeLabel})" : "FreeDV RX waiting: Codec2 runtime not found");
        }

        public void StartTransmit()
        {
            _isTransmitting = true;
            TryLoadRuntime();
            EmitTelemetry(IsRadeMode
                ? _radeRuntime?.IsReady == true ? "FreeDV RADEV1 TX running" : "FreeDV RADEV1 TX waiting: RADE runtime not found"
                : _runtime?.IsReady == true ? $"FreeDV TX running ({_modeLabel})" : "FreeDV TX waiting: Codec2 runtime not found");
        }

        public void Stop()
        {
            _isRunning = false;
            _isTransmitting = false;
            _modemSamples.Clear();
            _speechSamples.Clear();
            EmitTelemetry("FreeDV sidecar stopped");
        }

        public void StopTransmit()
        {
            if (IsRadeMode && _radeRuntime?.IsReady == true)
            {
                var tail = _radeRuntime.FlushTransmit(_transmitCallsign);
                if (tail.Length > 0)
                {
                    EmitModem(tail, RadeFreeDvRuntime.ModemSampleRateHz);
                    EmitTelemetry(string.IsNullOrWhiteSpace(_transmitCallsign)
                        ? "FreeDV RADEV1 TX sent end-of-over frame"
                        : $"FreeDV RADEV1 TX sent end-of-over callsign {_transmitCallsign}");
                }
            }

            _isTransmitting = false;
            _speechSamples.Clear();
            EmitTelemetry("FreeDV TX stopped");
        }

        public void Reset()
        {
            _runtime?.Dispose();
            _runtime = null;
            _radeRuntime?.Dispose();
            _radeRuntime = null;
            _modemSamples.Clear();
            _speechSamples.Clear();
            TryLoadRuntime();
            EmitTelemetry(IsRadeMode
                ? _radeRuntime?.IsReady == true ? "FreeDV RADEV1 runtime reset" : "FreeDV RADEV1 reset; RADE runtime still missing"
                : _runtime?.IsReady == true ? "FreeDV runtime reset" : "FreeDV reset; Codec2 runtime still missing");
        }

        public void PushAudio(JsonElement root)
        {
            if (!_isRunning)
            {
                return;
            }

            if (root.TryGetProperty("samples", out var samplesEl))
            {
                var bytes = Convert.FromBase64String(samplesEl.GetString() ?? string.Empty);
                _lastSignalLevelPercent = EstimateFloatPcmSignalPercent(bytes);
                var sampleRate = root.TryGetProperty("sampleRate", out var rateEl) ? rateEl.GetInt32() : _modemSampleRate;
                var channels = root.TryGetProperty("channels", out var channelsEl) ? channelsEl.GetInt32() : 1;
                var targetRate = IsRadeMode ? RadeFreeDvRuntime.ModemSampleRateHz : _modemSampleRate;
                var modemPcm = ConvertFloatAudioToPcm16Mono(bytes, sampleRate, channels, targetRate);
                _modemSamples.AddRange(modemPcm);
            }

            if (IsRadeMode)
            {
                PushRadeAudio();
                return;
            }

            var emittedSpeech = false;
            while (_runtime?.IsReady == true && _modemSamples.Count >= _runtime.Nin)
            {
                var nin = _runtime.Nin;
                var demod = _modemSamples.GetRange(0, nin).ToArray();
                _modemSamples.RemoveRange(0, nin);
                var speech = _runtime.ReceiveComplex(
                    demod,
                    _rxFrequencyOffsetHz,
                    ref _rxFrequencyOffsetPhaseReal,
                    ref _rxFrequencyOffsetPhaseImag,
                    out var sync,
                    out var snrDb);
                _lastSyncPercent = sync != 0 ? 100 : 0;
                _lastSnrDb = snrDb;
                if (sync != 0 && speech.Length > 0)
                {
                    EmitSpeech(speech, _runtime.SpeechSampleRate);
                    emittedSpeech = true;
                }

                EmitTelemetry($"FreeDV {_modeLabel} RX frame | sync {sync} | SNR {snrDb:0.0} dB");
            }

            EmitTelemetry(_runtime?.IsReady == true
                ? emittedSpeech
                    ? $"FreeDV {_modeLabel} decoded speech frame"
                    : _lastSyncPercent > 0
                        ? $"FreeDV {_modeLabel} synced; waiting for speech frame"
                        : $"FreeDV {_modeLabel} unsynced; RX offset {_rxFrequencyOffsetHz:+0;-0;0} Hz ({_modemSamples.Count}/{_runtime.Nin} modem samples)"
                : "FreeDV audio received; Codec2 runtime missing");
        }

        public void PushSpeech(JsonElement root)
        {
            if (!_isTransmitting)
            {
                return;
            }

            TryLoadRuntime();

            if (root.TryGetProperty("samples", out var samplesEl))
            {
                var bytes = Convert.FromBase64String(samplesEl.GetString() ?? string.Empty);
                _lastSignalLevelPercent = EstimateFloatPcmSignalPercent(bytes);
                var sampleRate = root.TryGetProperty("sampleRate", out var rateEl) ? rateEl.GetInt32() : _speechSampleRate;
                var channels = root.TryGetProperty("channels", out var channelsEl) ? channelsEl.GetInt32() : 1;
                var targetRate = IsRadeMode ? RadeFreeDvRuntime.SpeechSampleRateHz : _speechSampleRate;
                var speechPcm = ConvertFloatAudioToPcm16Mono(bytes, sampleRate, channels, targetRate);
                _speechSamples.AddRange(speechPcm);
            }

            if (IsRadeMode)
            {
                PushRadeSpeech();
                return;
            }

            var emittedModem = false;
            while (_runtime?.IsReady == true && _speechSamples.Count >= _runtime.NSpeechSamples)
            {
                var speech = _speechSamples.GetRange(0, _runtime.NSpeechSamples).ToArray();
                _speechSamples.RemoveRange(0, _runtime.NSpeechSamples);
                var modem = _runtime.Transmit(speech);
                if (modem.Length > 0)
                {
                    EmitModem(modem, _runtime.ModemSampleRate);
                    emittedModem = true;
                }
            }

            EmitTelemetry(_runtime?.IsReady == true
                ? emittedModem
                    ? $"FreeDV {_modeLabel} encoded TX frame"
                    : $"FreeDV {_modeLabel} speech buffered ({_speechSamples.Count}/{_runtime.NSpeechSamples} speech samples)"
                : "FreeDV speech received; Codec2 runtime missing");
        }

        public void EmitTelemetry(string status)
        {
            var payload = new
            {
                type = "telemetry",
                isRunning = _isRunning,
                isTransmitting = _isTransmitting,
                status,
                activeWorker = "Codec2 FreeDV sidecar",
                modeLabel = _modeLabel,
                signalLevelPercent = (int)Math.Clamp(Math.Round(_lastSignalLevelPercent), 0, 100),
                syncPercent = _lastSyncPercent,
                snrDb = _lastSnrDb,
                speechSampleRate = _speechSampleRate,
                modemSampleRate = _modemSampleRate,
                isCodec2RuntimeLoaded = IsRadeMode ? _radeRuntime?.IsReady == true : _runtime?.IsReady == true,
                radeCallsign = _lastRadeCallsign,
            };
            Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            Console.Out.Flush();
        }

        private static short[] ConvertFloatAudioToPcm16Mono(byte[] bytes, int inputSampleRate, int channels, int outputSampleRate)
        {
            if (bytes.Length < sizeof(float) || inputSampleRate <= 0 || outputSampleRate <= 0 || channels <= 0)
            {
                return [];
            }

            var frameCount = bytes.Length / (sizeof(float) * channels);
            if (frameCount <= 0)
            {
                return [];
            }

            var mono = new float[frameCount];
            for (var frame = 0; frame < frameCount; frame++)
            {
                var sum = 0.0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    var offset = ((frame * channels) + channel) * sizeof(float);
                    var sample = BitConverter.ToSingle(bytes, offset);
                    if (float.IsFinite(sample))
                    {
                        sum += sample;
                    }
                }

                mono[frame] = sum / channels;
            }

            return FloatToPcm16(ResampleMono(mono, inputSampleRate, outputSampleRate));
        }

        private static float[] ResampleMono(float[] samples, int inputSampleRate, int outputSampleRate)
        {
            if (samples.Length == 0 || inputSampleRate <= 0 || outputSampleRate <= 0)
            {
                return [];
            }

            if (inputSampleRate == outputSampleRate)
            {
                return samples;
            }

            var outputCount = Math.Max(1, (int)Math.Round(samples.Length * (outputSampleRate / (double)inputSampleRate)));
            var resampled = new float[outputCount];
            var sourcePerOutput = inputSampleRate / (double)outputSampleRate;

            for (var i = 0; i < outputCount; i++)
            {
                var sourceStart = i * sourcePerOutput;
                var sourceEnd = Math.Min(samples.Length, (i + 1) * sourcePerOutput);

                if (sourcePerOutput <= 1.0)
                {
                    var index = (int)Math.Floor(sourceStart);
                    var fraction = sourceStart - index;
                    var a = samples[Math.Clamp(index, 0, samples.Length - 1)];
                    var b = samples[Math.Clamp(index + 1, 0, samples.Length - 1)];
                    resampled[i] = (float)(a + ((b - a) * fraction));
                    continue;
                }

                // Downsampling speech into Codec2's narrow 8 kHz input needs an
                // anti-aliasing step. A weighted area average is cheap, stable,
                // and much cleaner than decimating every Nth sample.
                var first = (int)Math.Floor(sourceStart);
                var last = (int)Math.Ceiling(sourceEnd);
                var sum = 0.0;
                var weightSum = 0.0;
                for (var sourceIndex = first; sourceIndex < last; sourceIndex++)
                {
                    var clamped = Math.Clamp(sourceIndex, 0, samples.Length - 1);
                    var sampleStart = Math.Max(sourceStart, sourceIndex);
                    var sampleEnd = Math.Min(sourceEnd, sourceIndex + 1.0);
                    var weight = sampleEnd - sampleStart;
                    if (weight <= 0.0)
                    {
                        continue;
                    }

                    sum += samples[clamped] * weight;
                    weightSum += weight;
                }

                resampled[i] = weightSum > 0.0 ? (float)(sum / weightSum) : 0f;
            }

            return resampled;
        }

        private static short[] FloatToPcm16(float[] samples)
        {
            var pcm = new short[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                var value = Math.Clamp(samples[i], -1f, 1f);
                pcm[i] = (short)Math.Clamp(Math.Round(value * short.MaxValue), short.MinValue, short.MaxValue);
            }

            return pcm;
        }

        private static void EmitSpeech(short[] speech, int sampleRate)
        {
            var bytes = new byte[speech.Length * sizeof(short)];
            Buffer.BlockCopy(speech, 0, bytes, 0, bytes.Length);
            var payload = new
            {
                type = "speech",
                sampleRate,
                channels = 1,
                pcm16 = Convert.ToBase64String(bytes),
            };
            Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            Console.Out.Flush();
        }

        private static void EmitModem(short[] modem, int sampleRate)
        {
            var bytes = new byte[modem.Length * sizeof(short)];
            Buffer.BlockCopy(modem, 0, bytes, 0, bytes.Length);
            var payload = new
            {
                type = "modem",
                sampleRate,
                channels = 1,
                pcm16 = Convert.ToBase64String(bytes),
            };
            Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            Console.Out.Flush();
        }

        private void PushRadeAudio()
        {
            var emittedSpeech = false;
            while (_radeRuntime?.IsReady == true && _modemSamples.Count >= _radeRuntime.Nin)
            {
                var nin = _radeRuntime.Nin;
                var demod = _modemSamples.GetRange(0, nin).ToArray();
                _modemSamples.RemoveRange(0, nin);
                var speech = _radeRuntime.Receive(
                    demod,
                    _rxFrequencyOffsetHz,
                    ref _rxFrequencyOffsetPhaseReal,
                    ref _rxFrequencyOffsetPhaseImag,
                    out var sync,
                    out var snrDb,
                    out var callsign);
                _lastSyncPercent = sync != 0 ? 100 : 0;
                _lastSnrDb = snrDb;
                if (!string.IsNullOrWhiteSpace(callsign))
                {
                    _lastRadeCallsign = callsign.Trim().ToUpperInvariant();
                    EmitTelemetry($"FreeDV RADEV1 EOO callsign {_lastRadeCallsign}");
                }

                if (sync != 0 && speech.Length > 0)
                {
                    EmitSpeech(speech, RadeFreeDvRuntime.SpeechSampleRateHz);
                    emittedSpeech = true;
                }

                EmitTelemetry($"FreeDV RADEV1 RX frame | sync {sync} | SNR {snrDb:0.0} dB | freq {(_radeRuntime.FrequencyOffsetHz):+0.0;-0.0;0.0} Hz");
            }

            EmitTelemetry(_radeRuntime?.IsReady == true
                ? emittedSpeech
                    ? "FreeDV RADEV1 decoded speech batch"
                    : _lastSyncPercent > 0
                        ? "FreeDV RADEV1 synced; waiting for vocoder batch"
                        : $"FreeDV RADEV1 unsynced; RX offset {_rxFrequencyOffsetHz:+0;-0;0} Hz ({_modemSamples.Count}/{_radeRuntime.Nin} modem samples)"
                : "FreeDV RADEV1 audio received; RADE runtime missing");
        }

        private void PushRadeSpeech()
        {
            var emittedModem = false;
            while (_radeRuntime?.IsReady == true && _speechSamples.Count >= RadeFreeDvRuntime.TransmitSpeechChunkSamples)
            {
                var speech = _speechSamples.GetRange(0, RadeFreeDvRuntime.TransmitSpeechChunkSamples).ToArray();
                _speechSamples.RemoveRange(0, RadeFreeDvRuntime.TransmitSpeechChunkSamples);
                var modem = _radeRuntime.TransmitSpeech(speech);
                if (modem.Length > 0)
                {
                    EmitModem(modem, RadeFreeDvRuntime.ModemSampleRateHz);
                    emittedModem = true;
                }
            }

            EmitTelemetry(_radeRuntime?.IsReady == true
                ? emittedModem
                    ? "FreeDV RADEV1 encoded TX frame"
                    : $"FreeDV RADEV1 speech buffered ({_speechSamples.Count}/{RadeFreeDvRuntime.TransmitSpeechChunkSamples} speech samples)"
                : "FreeDV RADEV1 speech received; RADE runtime missing");
        }

        private void TryLoadRuntime()
        {
            if (IsRadeMode)
            {
                if (_radeRuntime?.IsReady == true)
                {
                    return;
                }

                _radeRuntime?.Dispose();
                _radeRuntime = RadeFreeDvRuntime.TryLoad(out _lastRuntimeLoadError);
                if (_radeRuntime?.IsReady == true)
                {
                    _speechSampleRate = RadeFreeDvRuntime.SpeechSampleRateHz;
                    _modemSampleRate = RadeFreeDvRuntime.ModemSampleRateHz;
                }

                return;
            }

            if (_runtime?.IsReady == true)
            {
                return;
            }

            _runtime?.Dispose();
            _runtime = Codec2FreeDvRuntime.TryLoad(_modeLabel, out _lastRuntimeLoadError);
            if (_runtime?.IsReady == true)
            {
                _speechSampleRate = _runtime.SpeechSampleRate;
                _modemSampleRate = _runtime.ModemSampleRate;
            }
        }

        private static string NormalizeMode(string? modeLabel)
        {
            var normalized = string.IsNullOrWhiteSpace(modeLabel)
                ? "700D"
                : modeLabel.Trim().ToUpperInvariant();
            return normalized switch
            {
                "1600" => "1600",
                "700C" => "700C",
                "700E" => "700E",
                "RADE" => "RADEV1",
                "RADEV1" => "RADEV1",
                _ => "700D",
            };
        }

        private static string NormalizeCallsign(string? callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(capacity: 8);
            foreach (var ch in callsign.Trim().ToUpperInvariant())
            {
                if (builder.Length >= 8)
                {
                    break;
                }

                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '/')
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private static double EstimateFloatPcmSignalPercent(byte[] bytes)
        {
            if (bytes.Length < sizeof(float))
            {
                return 0;
            }

            var peak = 0f;
            for (var offset = 0; offset + sizeof(float) <= bytes.Length; offset += sizeof(float))
            {
                var sample = BitConverter.ToSingle(bytes, offset);
                if (float.IsFinite(sample))
                {
                    peak = MathF.Max(peak, MathF.Abs(sample));
                }
            }

            return Math.Clamp(peak * 100.0, 0.0, 100.0);
        }

        public void Dispose()
        {
            _runtime?.Dispose();
            _radeRuntime?.Dispose();
        }
    }

    private sealed class RadeFreeDvRuntime : IDisposable
    {
        public const int ModemSampleRateHz = 8000;
        public const int SpeechSampleRateHz = 16000;
        public const int TransmitSpeechChunkSamples = 1920;
        private const int RadeUseCDecoder = 0x2;
        private const int RadeUseCEncoder = 0x1;
        private const int RadeVerboseQuiet = 0x8;
        private const int FeatureWidth = 36;
        private const int FeatureBatchThreshold = FeatureWidth * 36;
        private const int HilbertTaps = 127;
        private const int HilbertDelay = (HilbertTaps - 1) / 2;
        private static readonly float[] HilbertCoefficients = CreateHilbertCoefficients();

        private readonly IntPtr _library;
        private readonly IntPtr _rade;
        private readonly string _lpcnetDemoPath;
        private readonly RadeFinalize _finalize;
        private readonly RadeClose _close;
        private readonly RadeNin _nin;
        private readonly RadeRx _rx;
        private readonly RadeSync _sync;
        private readonly RadeFloatStat _freqOffset;
        private readonly RadeSnr _snr;
        private readonly RadeGetInt _nFeatures;
        private readonly RadeGetInt _nEooBits;
        private readonly RadeGetInt _nTxOut;
        private readonly RadeGetInt _nTxEooOut;
        private readonly RadeTx _tx;
        private readonly RadeTxEoo _txEoo;
        private readonly RadeSetEooCallsign _setEooCallsign;
        private readonly RadeDecodeCallsign _decodeCallsign;
        private readonly List<float> _pendingFeatures = [];
        private readonly List<float> _pendingTransmitFeatures = [];
        private readonly List<float> _hilbertHistory = [];

        private RadeFreeDvRuntime(
            IntPtr library,
            IntPtr rade,
            string lpcnetDemoPath,
            RadeFinalize finalize,
            RadeClose close,
            RadeNin nin,
            RadeRx rx,
            RadeSync sync,
            RadeFloatStat freqOffset,
            RadeSnr snr,
            RadeGetInt nFeatures,
            RadeGetInt nEooBits,
            RadeGetInt nTxOut,
            RadeGetInt nTxEooOut,
            RadeTx tx,
            RadeTxEoo txEoo,
            RadeSetEooCallsign setEooCallsign,
            RadeDecodeCallsign decodeCallsign)
        {
            _library = library;
            _rade = rade;
            _lpcnetDemoPath = lpcnetDemoPath;
            _finalize = finalize;
            _close = close;
            _nin = nin;
            _rx = rx;
            _sync = sync;
            _freqOffset = freqOffset;
            _snr = snr;
            _nFeatures = nFeatures;
            _nEooBits = nEooBits;
            _nTxOut = nTxOut;
            _nTxEooOut = nTxEooOut;
            _tx = tx;
            _txEoo = txEoo;
            _setEooCallsign = setEooCallsign;
            _decodeCallsign = decodeCallsign;
            Nin = _nin(_rade);
            FeatureCount = _nFeatures(_rade);
            EooBitCount = _nEooBits(_rade);
            TransmitSampleCount = _nTxOut(_rade);
            TransmitEooSampleCount = _nTxEooOut(_rade);
        }

        public bool IsReady => _library != IntPtr.Zero && _rade != IntPtr.Zero && File.Exists(_lpcnetDemoPath);

        public int Nin { get; private set; }

        public int FeatureCount { get; }

        public int EooBitCount { get; }

        public int TransmitSampleCount { get; }

        public int TransmitEooSampleCount { get; }

        public float FrequencyOffsetHz => IsReady ? _freqOffset(_rade) : 0.0f;

        public short[] Receive(
            short[] demodIn,
            int frequencyOffsetHz,
            ref float phaseReal,
            ref float phaseImag,
            out int sync,
            out float snrDb,
            out string callsign)
        {
            callsign = string.Empty;
            var complexIn = RealToAnalyticComplex(demodIn, frequencyOffsetHz, ModemSampleRateHz, ref phaseReal, ref phaseImag);
            var featuresOut = new float[Math.Max(FeatureCount, FeatureWidth)];
            var eooOut = new float[Math.Max(EooBitCount, 1)];
            var hasEoo = 0;
            var nout = _rx(_rade, featuresOut, ref hasEoo, eooOut, complexIn);
            Nin = _nin(_rade);
            sync = _sync(_rade);
            snrDb = _snr(_rade);

            if (hasEoo != 0 && EooBitCount > 0)
            {
                var buffer = new byte[16];
                var length = _decodeCallsign(eooOut, EooBitCount, buffer);
                if (length > 0)
                {
                    callsign = System.Text.Encoding.ASCII.GetString(buffer, 0, Math.Min(length, buffer.Length)).TrimEnd('\0', ' ');
                }
            }

            if (nout > 0)
            {
                for (var i = 0; i < Math.Min(nout, featuresOut.Length); i++)
                {
                    _pendingFeatures.Add(featuresOut[i]);
                }
            }

            if (_pendingFeatures.Count < FeatureBatchThreshold)
            {
                return [];
            }

            return SynthesizePendingFeatures();
        }

        public short[] TransmitSpeech(short[] speech)
        {
            if (!IsReady || speech.Length == 0)
            {
                return [];
            }

            var features = ExtractFeatures(speech);
            if (features.Length > 0)
            {
                _pendingTransmitFeatures.AddRange(features);
            }

            return ModulatePendingTransmitFeatures(flush: false);
        }

        public short[] FlushTransmit(string callsign)
        {
            if (!IsReady)
            {
                return [];
            }

            var modem = new List<short>();
            modem.AddRange(ModulatePendingTransmitFeatures(flush: true));

            if (!string.IsNullOrWhiteSpace(callsign))
            {
                _setEooCallsign(_rade, callsign);
            }

            var eoo = new RadeComplexSample[Math.Max(TransmitEooSampleCount, 1)];
            var eooSamples = _txEoo(_rade, eoo);
            modem.AddRange(ComplexRealToPcm16(eoo, eooSamples));
            // Matches radae_tx_nopy: trailing silence gives the receiver time
            // to finish demodulating the end-of-over metadata.
            modem.AddRange(new short[Math.Max(eooSamples, 0)]);
            return modem.ToArray();
        }

        private short[] ModulatePendingTransmitFeatures(bool flush)
        {
            if (FeatureCount <= 0 || TransmitSampleCount <= 0)
            {
                _pendingTransmitFeatures.Clear();
                return [];
            }

            if (flush && _pendingTransmitFeatures.Count > 0)
            {
                var remainder = _pendingTransmitFeatures.Count % FeatureCount;
                if (remainder != 0)
                {
                    _pendingTransmitFeatures.AddRange(new float[FeatureCount - remainder]);
                }
            }

            var modem = new List<short>();
            while (_pendingTransmitFeatures.Count >= FeatureCount)
            {
                var frameFeatures = _pendingTransmitFeatures.GetRange(0, FeatureCount).ToArray();
                _pendingTransmitFeatures.RemoveRange(0, FeatureCount);
                var complex = new RadeComplexSample[Math.Max(TransmitSampleCount, 1)];
                var samples = _tx(_rade, complex, frameFeatures);
                modem.AddRange(ComplexRealToPcm16(complex, samples));
            }

            return modem.ToArray();
        }

        private short[] SynthesizePendingFeatures()
        {
            var usable = _pendingFeatures.Count - (_pendingFeatures.Count % FeatureWidth);
            if (usable <= 0)
            {
                return [];
            }

            var features = _pendingFeatures.GetRange(0, usable).ToArray();
            _pendingFeatures.RemoveRange(0, usable);

            var tempDirectory = Path.Combine(Path.GetTempPath(), "ShackStack", "freedv-rade");
            Directory.CreateDirectory(tempDirectory);
            var stem = $"{Environment.ProcessId}_{Guid.NewGuid():N}";
            var featurePath = Path.Combine(tempDirectory, $"{stem}.f32");
            var pcmPath = Path.Combine(tempDirectory, $"{stem}.pcm");

            try
            {
                WriteFloat32File(featurePath, features);
                var startInfo = new ProcessStartInfo
                {
                    FileName = _lpcnetDemoPath,
                    Arguments = $"-fargan-synthesis \"{featurePath}\" \"{pcmPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(_lpcnetDemoPath) ?? AppContext.BaseDirectory,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return [];
                }

                if (!process.WaitForExit(5000) || process.ExitCode != 0 || !File.Exists(pcmPath))
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                    }

                    return [];
                }

                var bytes = File.ReadAllBytes(pcmPath);
                var speech = new short[bytes.Length / sizeof(short)];
                Buffer.BlockCopy(bytes, 0, speech, 0, bytes.Length);
                return speech;
            }
            finally
            {
                TryDelete(featurePath);
                TryDelete(pcmPath);
            }
        }

        private float[] ExtractFeatures(short[] speech)
        {
            var usableSamples = speech.Length - (speech.Length % 160);
            if (usableSamples <= 0)
            {
                return [];
            }

            if (usableSamples != speech.Length)
            {
                Array.Resize(ref speech, usableSamples);
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "ShackStack", "freedv-rade");
            Directory.CreateDirectory(tempDirectory);
            var stem = $"{Environment.ProcessId}_{Guid.NewGuid():N}";
            var speechPath = Path.Combine(tempDirectory, $"{stem}.pcm");
            var featurePath = Path.Combine(tempDirectory, $"{stem}.features.f32");

            try
            {
                var bytes = new byte[speech.Length * sizeof(short)];
                Buffer.BlockCopy(speech, 0, bytes, 0, bytes.Length);
                File.WriteAllBytes(speechPath, bytes);

                var startInfo = new ProcessStartInfo
                {
                    FileName = _lpcnetDemoPath,
                    Arguments = $"-features \"{speechPath}\" \"{featurePath}\"",
                    WorkingDirectory = Path.GetDirectoryName(_lpcnetDemoPath) ?? AppContext.BaseDirectory,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return [];
                }

                if (!process.WaitForExit(5000) || process.ExitCode != 0 || !File.Exists(featurePath))
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                    }

                    return [];
                }

                var featureBytes = File.ReadAllBytes(featurePath);
                var features = new float[featureBytes.Length / sizeof(float)];
                Buffer.BlockCopy(featureBytes, 0, features, 0, featureBytes.Length);
                return features;
            }
            finally
            {
                TryDelete(speechPath);
                TryDelete(featurePath);
            }
        }

        private static void WriteFloat32File(string path, float[] values)
        {
            var bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(path, bytes);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private RadeComplexSample[] RealToAnalyticComplex(
            short[] samples,
            int frequencyOffsetHz,
            int sampleRate,
            ref float phaseReal,
            ref float phaseImag)
        {
            var complex = new RadeComplexSample[samples.Length];
            if (samples.Length == 0)
            {
                return complex;
            }

            var step = 2.0 * Math.PI * frequencyOffsetHz / Math.Max(sampleRate, 1);
            var stepReal = (float)Math.Cos(step);
            var stepImag = (float)Math.Sin(step);
            for (var i = 0; i < samples.Length; i++)
            {
                var input = samples[i] / 32767.0f;
                _hilbertHistory.Add(input);
                if (_hilbertHistory.Count > HilbertTaps)
                {
                    _hilbertHistory.RemoveAt(0);
                }

                var real = _hilbertHistory.Count > HilbertDelay
                    ? _hilbertHistory[_hilbertHistory.Count - 1 - HilbertDelay]
                    : 0.0f;
                var imag = 0.0f;
                var availableTaps = Math.Min(HilbertTaps, _hilbertHistory.Count);
                for (var k = 0; k < availableTaps; k++)
                {
                    imag += HilbertCoefficients[k] * _hilbertHistory[_hilbertHistory.Count - 1 - k];
                }

                var nextReal = (phaseReal * stepReal) - (phaseImag * stepImag);
                var nextImag = (phaseReal * stepImag) + (phaseImag * stepReal);
                phaseReal = nextReal;
                phaseImag = nextImag;
                complex[i] = new RadeComplexSample
                {
                    Real = (real * phaseReal) - (imag * phaseImag),
                    Imag = (real * phaseImag) + (imag * phaseReal),
                };
            }

            var magnitude = MathF.Sqrt((phaseReal * phaseReal) + (phaseImag * phaseImag));
            if (magnitude > 0)
            {
                phaseReal /= magnitude;
                phaseImag /= magnitude;
            }
            else
            {
                phaseReal = 1.0f;
                phaseImag = 0.0f;
            }

            return complex;
        }

        private static float[] CreateHilbertCoefficients()
        {
            var coeffs = new float[HilbertTaps];
            for (var i = 0; i < HilbertTaps; i++)
            {
                var n = i - HilbertDelay;
                if (n == 0 || n % 2 == 0)
                {
                    coeffs[i] = 0.0f;
                    continue;
                }

                var h = 2.0 / (Math.PI * n);
                var w = 0.54 - (0.46 * Math.Cos(2.0 * Math.PI * i / (HilbertTaps - 1)));
                coeffs[i] = (float)(h * w);
            }

            return coeffs;
        }

        private static short[] ComplexRealToPcm16(RadeComplexSample[] samples, int count)
        {
            var output = new short[Math.Clamp(count, 0, samples.Length)];
            for (var i = 0; i < output.Length; i++)
            {
                var value = Math.Clamp(samples[i].Real, -1.0f, 1.0f);
                output[i] = (short)Math.Clamp(Math.Round(value * short.MaxValue), short.MinValue, short.MaxValue);
            }

            return output;
        }

        public static RadeFreeDvRuntime? TryLoad(out string? error)
        {
            error = null;
            var failures = new List<string>();
            foreach (var candidate in EnumerateRadeCandidates())
            {
                if (!File.Exists(candidate))
                {
                    failures.Add($"Not found: {candidate}");
                    continue;
                }

                var lpcnetDemoPath = Path.Combine(Path.GetDirectoryName(candidate) ?? AppContext.BaseDirectory, "lpcnet_demo.exe");
                if (!File.Exists(lpcnetDemoPath))
                {
                    failures.Add($"{candidate}: missing lpcnet_demo.exe for FARGAN synthesis");
                    continue;
                }

                try
                {
                    var candidateDirectory = Path.GetDirectoryName(candidate);
                    if (!string.IsNullOrWhiteSpace(candidateDirectory))
                    {
                        NativeSearchPath.SetDllDirectory(candidateDirectory);
                    }

                    var library = NativeLibrary.Load(candidate);
                    var initialize = GetDelegate<RadeInitialize>(library, "rade_initialize");
                    var finalize = GetDelegate<RadeFinalize>(library, "rade_finalize");
                    var open = GetDelegate<RadeOpen>(library, "rade_open");
                    var close = GetDelegate<RadeClose>(library, "rade_close");
                    var nin = GetDelegate<RadeNin>(library, "rade_nin");
                    var rx = GetDelegate<RadeRx>(library, "rade_rx");
                    var sync = GetDelegate<RadeSync>(library, "rade_sync");
                    var freqOffset = GetDelegate<RadeFloatStat>(library, "rade_freq_offset");
                    var snr = GetDelegate<RadeSnr>(library, "rade_snrdB_3k_est");
                    var nFeatures = GetDelegate<RadeGetInt>(library, "rade_n_features_in_out");
                    var nEooBits = GetDelegate<RadeGetInt>(library, "rade_n_eoo_bits");
                    var nTxOut = GetDelegate<RadeGetInt>(library, "rade_n_tx_out");
                    var nTxEooOut = GetDelegate<RadeGetInt>(library, "rade_n_tx_eoo_out");
                    var tx = GetDelegate<RadeTx>(library, "rade_tx");
                    var txEoo = GetDelegate<RadeTxEoo>(library, "rade_tx_eoo");
                    var setEooCallsign = GetDelegate<RadeSetEooCallsign>(library, "rade_tx_set_eoo_callsign");
                    var decodeCallsign = GetDelegate<RadeDecodeCallsign>(library, "rade_rx_get_eoo_callsign");

                    initialize();
                    var rade = open(string.Empty, RadeUseCEncoder | RadeUseCDecoder | RadeVerboseQuiet);
                    if (rade == IntPtr.Zero)
                    {
                        NativeLibrary.Free(library);
                        failures.Add($"{candidate}: rade_open returned null");
                        continue;
                    }

                    return new RadeFreeDvRuntime(
                        library,
                        rade,
                        lpcnetDemoPath,
                        finalize,
                        close,
                        nin,
                        rx,
                        sync,
                        freqOffset,
                        snr,
                        nFeatures,
                        nEooBits,
                        nTxOut,
                        nTxEooOut,
                        tx,
                        txEoo,
                        setEooCallsign,
                        decodeCallsign);
                }
                catch (Exception ex)
                {
                    failures.Add($"{candidate}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            error = string.Join(" ; ", failures);
            return null;
        }

        private static IEnumerable<string> EnumerateRadeCandidates()
        {
            var overridePath = Environment.GetEnvironmentVariable("SHACKSTACK_FREEDV_RADE_PATH");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                yield return Environment.ExpandEnvironmentVariables(overridePath.Trim());
            }

            var baseDir = AppContext.BaseDirectory;
            yield return Path.Combine(baseDir, "librade.dll");
            yield return Path.Combine(baseDir, "rade", "librade.dll");
        }

        private static T GetDelegate<T>(IntPtr library, string exportName)
            where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, exportName));

        public void Dispose()
        {
            if (_rade != IntPtr.Zero)
            {
                _close(_rade);
            }

            if (_library != IntPtr.Zero)
            {
                _finalize();
                NativeLibrary.Free(_library);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RadeComplexSample
        {
            public float Real;
            public float Imag;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RadeInitialize();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RadeFinalize();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr RadeOpen([MarshalAs(UnmanagedType.LPStr)] string modelFile, int flags);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RadeClose(IntPtr rade);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RadeNin(IntPtr rade);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RadeRx(IntPtr rade, [Out] float[] featuresOut, ref int hasEooOut, [Out] float[] eooOut, [In] RadeComplexSample[] rxIn);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RadeSync(IntPtr rade);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate float RadeFloatStat(IntPtr rade);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RadeSnr(IntPtr rade);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RadeGetInt(IntPtr rade);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RadeTx(IntPtr rade, [Out] RadeComplexSample[] txOut, [In] float[] featuresIn);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RadeTxEoo(IntPtr rade, [Out] RadeComplexSample[] txEooOut);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RadeSetEooCallsign(IntPtr rade, [MarshalAs(UnmanagedType.LPStr)] string callsign);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RadeDecodeCallsign(float[] eooBits, int nEooBits, byte[] callsignOut);
    }

    private sealed class Codec2FreeDvRuntime : IDisposable
    {
        private readonly IntPtr _library;
        private readonly IntPtr _freedv;
        private readonly FreedvClose _close;
        private readonly FreedvRx _rx;
        private readonly FreedvComplexRx _complexRx;
        private readonly FreedvTx _tx;
        private readonly FreedvNin _nin;
        private readonly FreedvGetModemStats _getModemStats;
        private readonly int _maxSpeechSamples;
        private readonly int _nomModemSamples;

        private Codec2FreeDvRuntime(
            IntPtr library,
            IntPtr freedv,
            FreedvClose close,
            FreedvRx rx,
            FreedvComplexRx complexRx,
            FreedvTx tx,
            FreedvNin nin,
            FreedvGetModemStats getModemStats,
            int speechSampleRate,
            int modemSampleRate,
            int speechSamples,
            int nomModemSamples,
            int maxSpeechSamples)
        {
            _library = library;
            _freedv = freedv;
            _close = close;
            _rx = rx;
            _complexRx = complexRx;
            _tx = tx;
            _nin = nin;
            _getModemStats = getModemStats;
            SpeechSampleRate = speechSampleRate;
            ModemSampleRate = modemSampleRate;
            NSpeechSamples = speechSamples;
            _nomModemSamples = nomModemSamples;
            _maxSpeechSamples = maxSpeechSamples;
            Nin = _nin(_freedv);
        }

        public bool IsReady => _library != IntPtr.Zero && _freedv != IntPtr.Zero;

        public int SpeechSampleRate { get; }

        public int ModemSampleRate { get; }

        public int NSpeechSamples { get; }

        public int Nin { get; private set; }

        public short[] Receive(short[] demodIn, out int sync, out float snrDb)
        {
            var speechOut = new short[_maxSpeechSamples];
            var nout = _rx(_freedv, speechOut, demodIn);
            Nin = _nin(_freedv);
            _getModemStats(_freedv, out sync, out snrDb);
            if (nout <= 0)
            {
                return [];
            }

            Array.Resize(ref speechOut, nout);
            return speechOut;
        }

        public short[] ReceiveComplex(
            short[] demodIn,
            int frequencyOffsetHz,
            ref float phaseReal,
            ref float phaseImag,
            out int sync,
            out float snrDb)
        {
            var complexIn = ShiftToComplex(demodIn, frequencyOffsetHz, ModemSampleRate, ref phaseReal, ref phaseImag);
            var speechOut = new short[_maxSpeechSamples];
            var nout = _complexRx(_freedv, speechOut, complexIn);
            Nin = _nin(_freedv);
            _getModemStats(_freedv, out sync, out snrDb);
            if (nout <= 0)
            {
                return [];
            }

            Array.Resize(ref speechOut, nout);
            return speechOut;
        }

        private static ComplexSample[] ShiftToComplex(
            short[] samples,
            int frequencyOffsetHz,
            int sampleRate,
            ref float phaseReal,
            ref float phaseImag)
        {
            var complex = new ComplexSample[samples.Length];
            if (samples.Length == 0)
            {
                return complex;
            }

            if (sampleRate <= 0)
            {
                sampleRate = 8000;
            }

            var step = 2.0 * Math.PI * frequencyOffsetHz / sampleRate;
            var stepReal = (float)Math.Cos(step);
            var stepImag = (float)Math.Sin(step);

            for (var i = 0; i < samples.Length; i++)
            {
                var nextReal = (phaseReal * stepReal) - (phaseImag * stepImag);
                var nextImag = (phaseReal * stepImag) + (phaseImag * stepReal);
                phaseReal = nextReal;
                phaseImag = nextImag;

                complex[i] = new ComplexSample
                {
                    Real = samples[i] * phaseReal,
                    Imag = samples[i] * phaseImag,
                };
            }

            var magnitude = MathF.Sqrt((phaseReal * phaseReal) + (phaseImag * phaseImag));
            if (magnitude > 0.0f)
            {
                phaseReal /= magnitude;
                phaseImag /= magnitude;
            }
            else
            {
                phaseReal = 1.0f;
                phaseImag = 0.0f;
            }

            return complex;
        }

        public short[] Transmit(short[] speechIn)
        {
            var modemOut = new short[_nomModemSamples];
            _tx(_freedv, modemOut, speechIn);
            return modemOut;
        }

        public static Codec2FreeDvRuntime? TryLoad(string modeLabel, out string? error)
        {
            error = null;
            var failures = new List<string>();
            foreach (var candidate in EnumerateRuntimeCandidates())
            {
                if (!File.Exists(candidate))
                {
                    failures.Add($"Not found: {candidate}");
                    continue;
                }

                try
                {
                    var candidateDirectory = Path.GetDirectoryName(candidate);
                    if (!string.IsNullOrWhiteSpace(candidateDirectory))
                    {
                        NativeSearchPath.SetDllDirectory(candidateDirectory);
                    }

                    var library = NativeLibrary.Load(candidate);
                    var open = GetDelegate<FreedvOpen>(library, "freedv_open");
                    var close = GetDelegate<FreedvClose>(library, "freedv_close");
                    var rx = GetDelegate<FreedvRx>(library, "freedv_rx");
                    var complexRx = GetDelegate<FreedvComplexRx>(library, "freedv_comprx");
                    var tx = GetDelegate<FreedvTx>(library, "freedv_tx");
                    var nin = GetDelegate<FreedvNin>(library, "freedv_nin");
                    var getSpeechSamples = GetDelegate<FreedvGetInt>(library, "freedv_get_n_speech_samples");
                    var getNomModemSamples = GetDelegate<FreedvGetInt>(library, "freedv_get_n_nom_modem_samples");
                    var getMaxSpeechSamples = GetDelegate<FreedvGetInt>(library, "freedv_get_n_max_speech_samples");
                    var getSpeechSampleRate = GetDelegate<FreedvGetInt>(library, "freedv_get_speech_sample_rate");
                    var getModemSampleRate = GetDelegate<FreedvGetInt>(library, "freedv_get_modem_sample_rate");
                    var getModemStats = GetDelegate<FreedvGetModemStats>(library, "freedv_get_modem_stats");
                    var freedv = open(ToModeConstant(modeLabel));
                    if (freedv == IntPtr.Zero)
                    {
                        NativeLibrary.Free(library);
                        failures.Add($"{candidate}: freedv_open returned null for mode {modeLabel}");
                        continue;
                    }

                    return new Codec2FreeDvRuntime(
                        library,
                        freedv,
                        close,
                        rx,
                        complexRx,
                        tx,
                        nin,
                        getModemStats,
                        getSpeechSampleRate(freedv),
                        getModemSampleRate(freedv),
                        getSpeechSamples(freedv),
                        getNomModemSamples(freedv),
                        getMaxSpeechSamples(freedv));
                }
                catch (Exception ex)
                {
                    failures.Add($"{candidate}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            error = string.Join(" ; ", failures);
            return null;
        }

        private static IEnumerable<string> EnumerateRuntimeCandidates()
        {
            var overridePath = Environment.GetEnvironmentVariable("SHACKSTACK_FREEDV_CODEC2_PATH");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                yield return Environment.ExpandEnvironmentVariables(overridePath.Trim());
            }

            var baseDir = AppContext.BaseDirectory;
            yield return Path.Combine(baseDir, "codec2.dll");
            yield return Path.Combine(baseDir, "libcodec2.dll");
            yield return Path.Combine(baseDir, "codec2", "codec2.dll");
            yield return Path.Combine(baseDir, "codec2", "libcodec2.dll");
        }

        private static T GetDelegate<T>(IntPtr library, string exportName)
            where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, exportName));

        private static int ToModeConstant(string modeLabel) => modeLabel switch
        {
            "1600" => 0,
            "700C" => 6,
            "700D" => 7,
            "700E" => 13,
            _ => 7,
        };

        public void Dispose()
        {
            if (_freedv != IntPtr.Zero)
            {
                _close(_freedv);
            }

            if (_library != IntPtr.Zero)
            {
                NativeLibrary.Free(_library);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr FreedvOpen(int mode);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FreedvClose(IntPtr freedv);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int FreedvGetInt(IntPtr freedv);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int FreedvNin(IntPtr freedv);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int FreedvRx(IntPtr freedv, short[] speechOut, short[] demodIn);

        [StructLayout(LayoutKind.Sequential)]
        private struct ComplexSample
        {
            public float Real;
            public float Imag;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int FreedvComplexRx(IntPtr freedv, short[] speechOut, ComplexSample[] demodIn);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FreedvTx(IntPtr freedv, short[] modOut, short[] speechIn);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FreedvGetModemStats(IntPtr freedv, out int sync, out float snrEst);
    }

    private static class NativeSearchPath
    {
        [DllImport("kernel32", EntryPoint = "SetDllDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDllDirectory(string pathName);
    }
}
