using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShackStack.DecoderHost.GplFldigiPsk;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var worker = new FldigiPskWorker();
        worker.EmitTelemetry("fldigi-derived PSK sidecar ready");

        while (Console.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            PskCommand? command;
            try
            {
                command = JsonSerializer.Deserialize<PskCommand>(line, JsonOptions);
            }
            catch (Exception ex)
            {
                worker.EmitTelemetry($"Protocol error: {ex.Message}");
                continue;
            }

            if (command is null)
            {
                continue;
            }

            try
            {
                switch ((command.Type ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "configure":
                        worker.Configure(command);
                        break;
                    case "start":
                        worker.Start();
                        break;
                    case "stop":
                        worker.Stop();
                        break;
                    case "reset":
                        worker.Reset();
                        break;
                    case "audio":
                        worker.HandleAudio(command);
                        break;
                    case "shutdown":
                        worker.Stop();
                        return 0;
                }
            }
            catch (Exception ex)
            {
                worker.EmitTelemetry($"PSK sidecar error: {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine(ex);
            }
        }

        return 0;
    }

    private sealed record PskCommand(
        string? Type,
        string? ModeLabel,
        string? FrequencyLabel,
        double? AudioCenterHz,
        bool? ReversePolarity,
        int? SampleRate,
        int? Channels,
        string? Samples);

    private sealed class FldigiPskWorker
    {
        private const int WorkingSampleRate = 8000;

        private string _modeLabel = "BPSK31";
        private string _frequencyLabel = "Current radio frequency";
        private double _audioCenterHz = 1000.0;
        private bool _reversePolarity;
        private bool _isRunning;
        private int _signalLevelPercent;
        private double _suggestedAudioCenterHz;
        private double _suggestedAudioScoreDb;
        private BpskDemodulator _demodulator = new(WorkingSampleRate, "BPSK31", 1000.0);

        public void Configure(PskCommand command)
        {
            _modeLabel = NormalizeMode(command.ModeLabel ?? _modeLabel);
            _frequencyLabel = command.FrequencyLabel ?? _frequencyLabel;
            _audioCenterHz = Math.Clamp(command.AudioCenterHz ?? _audioCenterHz, 300.0, 3200.0);
            _reversePolarity = command.ReversePolarity ?? _reversePolarity;
            _demodulator = new BpskDemodulator(WorkingSampleRate, _modeLabel, _audioCenterHz);
            EmitTelemetry($"Configured {_modeLabel} | audio center {_audioCenterHz:0} Hz | reverse {_reversePolarity} | fldigi BPSK RX chain");
        }

        public void Start()
        {
            _isRunning = true;
            EmitTelemetry($"PSK listening | {_modeLabel} | audio center {_audioCenterHz:0} Hz");
        }

        public void Stop()
        {
            _isRunning = false;
            EmitTelemetry("PSK receiver stopped");
        }

        public void Reset()
        {
            _signalLevelPercent = 0;
            _demodulator = new BpskDemodulator(WorkingSampleRate, _modeLabel, _audioCenterHz);
            EmitTelemetry("PSK decoder reset");
        }

        public void HandleAudio(PskCommand command)
        {
            if (!_isRunning || string.IsNullOrWhiteSpace(command.Samples))
            {
                return;
            }

            var samples = DecodeFloat32(command.Samples);
            var mono = ToMono(samples, command.Channels ?? 1);
            var resampled = ResampleLinear(mono, command.SampleRate ?? WorkingSampleRate, WorkingSampleRate);
            _signalLevelPercent = EstimateSignalPercent(resampled);
            (_suggestedAudioCenterHz, _suggestedAudioScoreDb) = EstimateStrongestNarrowbandTone(resampled, WorkingSampleRate);
            var decoded = _demodulator.Process(resampled, _reversePolarity);

            if (!string.IsNullOrEmpty(decoded))
            {
                EmitDecode(decoded, 0.68);
            }

            EmitTelemetry(
                $"PSK running | {_modeLabel} | audio center {_audioCenterHz:0} Hz | " +
                $"{GetBaud(_modeLabel):0.##} baud | DCD {(_demodulator.IsDcdOpen ? "open" : "closed")} | " +
                $"metric {_demodulator.Metric:0} | AFC {_demodulator.FrequencyErrorHz:+0.00;-0.00;0.00} Hz | " +
                $"track {_demodulator.TrackedAudioCenterHz:0.0} Hz | peak {_suggestedAudioCenterHz:0} Hz ({_suggestedAudioScoreDb:+0.0;-0.0;0.0} dB) | " +
                $"symbols {_demodulator.SymbolCount} | chars {_demodulator.CharacterCount}");
        }

        public void EmitTelemetry(string status)
        {
            Emit(new
            {
                type = "telemetry",
                isRunning = _isRunning,
                status,
                activeWorker = "fldigi GPL PSK sidecar",
                modeLabel = _modeLabel,
                audioCenterHz = _audioCenterHz,
                trackedAudioCenterHz = _demodulator.TrackedAudioCenterHz,
                suggestedAudioCenterHz = _suggestedAudioCenterHz,
                suggestedAudioScoreDb = _suggestedAudioScoreDb,
                frequencyErrorHz = _demodulator.FrequencyErrorHz,
                isDcdOpen = _demodulator.IsDcdOpen,
                signalLevelPercent = _signalLevelPercent,
            });
        }

        private static string NormalizeMode(string modeLabel)
        {
            var mode = modeLabel.Trim().ToUpperInvariant();
            return mode switch
            {
                "PSK31" or "BPSK31" => "BPSK31",
                "PSK63" or "BPSK63" => "BPSK63",
                _ => "BPSK31",
            };
        }

        private static double GetBaud(string modeLabel) =>
            string.Equals(modeLabel, "BPSK63", StringComparison.OrdinalIgnoreCase) ? 62.5 : 31.25;

        private static void EmitDecode(string text, double confidence)
        {
            Emit(new
            {
                type = "decode",
                text,
                confidence,
            });
        }

        private static float[] DecodeFloat32(string base64)
        {
            var bytes = Convert.FromBase64String(base64);
            var sampleCount = bytes.Length / sizeof(float);
            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                samples[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)));
            }

            return samples;
        }

        private static double[] ToMono(float[] samples, int channels)
        {
            if (samples.Length == 0 || channels <= 0)
            {
                return [];
            }

            if (channels == 1)
            {
                return samples.Select(static sample => (double)sample).ToArray();
            }

            var frameCount = samples.Length / channels;
            var mono = new double[frameCount];
            for (var frame = 0; frame < frameCount; frame++)
            {
                var offset = frame * channels;
                double sum = 0;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += samples[offset + channel];
                }

                mono[frame] = sum / channels;
            }

            return mono;
        }

        private static double[] ResampleLinear(double[] samples, int sourceSampleRate, int targetSampleRate)
        {
            if (samples.Length == 0 || sourceSampleRate <= 0 || targetSampleRate <= 0)
            {
                return [];
            }

            if (sourceSampleRate == targetSampleRate)
            {
                return samples;
            }

            var outputLength = (int)Math.Round(samples.Length * (targetSampleRate / (double)sourceSampleRate), MidpointRounding.AwayFromZero);
            if (outputLength <= 1)
            {
                return [];
            }

            var output = new double[outputLength];
            var step = sourceSampleRate / (double)targetSampleRate;
            for (var i = 0; i < outputLength; i++)
            {
                var sourceIndex = i * step;
                var left = (int)Math.Floor(sourceIndex);
                var right = Math.Min(left + 1, samples.Length - 1);
                var fraction = sourceIndex - left;
                if (left >= samples.Length)
                {
                    output[i] = samples[^1];
                    continue;
                }

                output[i] = (samples[left] * (1.0 - fraction)) + (samples[right] * fraction);
            }

            return output;
        }

        private static int EstimateSignalPercent(double[] samples)
        {
            if (samples.Length == 0)
            {
                return 0;
            }

            var rms = Math.Sqrt(samples.Select(static sample => sample * sample).Average());
            return (int)Math.Clamp(Math.Round(rms * 250.0, MidpointRounding.AwayFromZero), 0, 100);
        }

        private static (double FrequencyHz, double ScoreDb) EstimateStrongestNarrowbandTone(double[] samples, int sampleRate)
        {
            if (samples.Length < sampleRate / 4)
            {
                return (0.0, 0.0);
            }

            var windowLength = Math.Min(samples.Length, sampleRate);
            var start = samples.Length - windowLength;
            var bestFrequency = 0.0;
            var bestPower = 0.0;
            var powers = new List<double>();
            for (var frequency = 300.0; frequency <= 3200.0; frequency += 20.0)
            {
                var power = GoertzelPower(samples, start, windowLength, sampleRate, frequency);
                powers.Add(power);
                if (power > bestPower)
                {
                    bestPower = power;
                    bestFrequency = frequency;
                }
            }

            if (bestPower <= 0 || powers.Count == 0)
            {
                return (0.0, 0.0);
            }

            powers.Sort();
            var medianNoise = powers[powers.Count / 2] + 1e-12;
            return (bestFrequency, 10.0 * Math.Log10(bestPower / medianNoise));
        }

        private static double GoertzelPower(double[] samples, int start, int length, int sampleRate, double frequency)
        {
            var normalized = frequency / sampleRate;
            var coeff = 2.0 * Math.Cos(Math.Tau * normalized);
            var q0 = 0.0;
            var q1 = 0.0;
            var q2 = 0.0;
            for (var i = 0; i < length; i++)
            {
                q0 = coeff * q1 - q2 + samples[start + i];
                q2 = q1;
                q1 = q0;
            }

            return (q1 * q1) + (q2 * q2) - (q1 * q2 * coeff);
        }

        private static void Emit(object payload)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            Console.Out.Flush();
        }
    }

    private sealed class BpskDemodulator
    {
        private const int FirLength = 64;
        private const double SquelchMetricThreshold = 35.0;
        private const double MaximumAfcCorrectionHz = 25.0;

        private readonly int _sampleRate;
        private readonly double _audioCenterHz;
        private readonly int _symbolSamples;
        private readonly int _dcdBits;
        private readonly double[] _syncBuffer = new double[16];
        private readonly ComplexFirFilter _fir1;
        private readonly ComplexFirFilter _fir2;
        private readonly double _singleCarrierBandwidthHz;
        private readonly double _originalAudioCenterHz;
        private double _trackedAudioCenterHz;
        private double _ncoStep;
        private double _ncoPhase;
        private double _bitClock;
        private Complex _previousSymbol = Complex.One;
        private Complex _quality = Complex.Zero;
        private uint _shiftRegister;
        private uint _dcdShiftRegister;
        private int _dcdOffCounter;
        private double _afcMetric;

        public BpskDemodulator(int sampleRate, string modeLabel, double audioCenterHz)
        {
            _sampleRate = sampleRate;
            _audioCenterHz = audioCenterHz;
            _originalAudioCenterHz = audioCenterHz;
            _trackedAudioCenterHz = audioCenterHz;
            _symbolSamples = string.Equals(modeLabel, "BPSK63", StringComparison.OrdinalIgnoreCase) ? 128 : 256;
            _dcdBits = _symbolSamples == 128 ? 64 : 32;
            _singleCarrierBandwidthHz = _sampleRate / (double)_symbolSamples;
            UpdateNcoStep();
            _fir1 = new ComplexFirFilter(CreateRaisedCosineFilter(FirLength), _symbolSamples > 15 ? _symbolSamples / 16 : 1);
            _fir2 = new ComplexFirFilter(PskCoreFilter, 1);
        }

        public int SymbolCount { get; private set; }

        public int CharacterCount { get; private set; }

        public bool IsDcdOpen { get; private set; }

        public double Metric { get; private set; }

        public double FrequencyErrorHz { get; private set; }

        public double TrackedAudioCenterHz => _trackedAudioCenterHz;

        public string Process(double[] samples, bool reversePolarity)
        {
            var decoded = new List<char>();
            foreach (var sample in samples)
            {
                // fldigi psk::rx_process first mixes the selected carrier to baseband
                // before matched filtering and symbol decisions.
                var mixed = new Complex(sample * Math.Cos(_ncoPhase), sample * Math.Sin(_ncoPhase));
                _ncoPhase += _ncoStep;
                if (_ncoPhase > Math.Tau)
                {
                    _ncoPhase -= Math.Tau;
                }

                // fldigi psk::rx_process uses two PSK-core FIR stages: first
                // filter/downsample to 16 samples per symbol, then final shaping.
                if (!_fir1.Run(mixed, out var filtered))
                {
                    continue;
                }

                _fir2.Run(filtered, out var symbol);
                var magnitude = symbol.Magnitude;
                var idx = (int)_bitClock;
                if ((uint)idx >= _syncBuffer.Length)
                {
                    idx = 0;
                    _bitClock = 0;
                }

                _syncBuffer[idx] = (0.8 * _syncBuffer[idx]) + (0.2 * magnitude);

                var bitSteps = _symbolSamples >= 16 ? 16 : _symbolSamples;
                var halfSteps = bitSteps / 2;
                var sum = 0.0;
                var ampSum = 0.0;
                for (var i = 0; i < halfSteps; i++)
                {
                    sum += _syncBuffer[i] - _syncBuffer[i + halfSteps];
                    ampSum += _syncBuffer[i] + _syncBuffer[i + halfSteps];
                }

                var timingError = ampSum == 0 ? 0 : sum / ampSum;
                _bitClock -= timingError / (5.0 * 16 / bitSteps);
                _bitClock += 1;
                if (_bitClock < 0)
                {
                    _bitClock += bitSteps;
                }

                if (_bitClock < bitSteps)
                {
                    continue;
                }

                _bitClock -= bitSteps;
                SymbolCount += 1;
                DecodeSymbol(symbol, reversePolarity, decoded);
            }

            return decoded.Count == 0 ? string.Empty : new string(decoded.ToArray());
        }

        private void DecodeSymbol(Complex symbol, bool reversePolarity, List<char> decoded)
        {
            // fldigi rx_symbol uses differential phase:
            // phase = arg(conj(prevsymbol) * symbol), then BPSK feeds rx_bit(!bits).
            var differential = Complex.Conjugate(_previousSymbol) * symbol;
            var phase = Math.Atan2(differential.Imaginary, differential.Real);
            _previousSymbol = symbol;
            if (phase < 0)
            {
                phase += Math.Tau;
            }

            var bits = (((int)(phase / Math.PI + 0.5)) & 1) << 1;
            UpdateDcd(phase, bits);
            PhaseAfc(phase, bits);

            if (!IsDcdOpen)
            {
                return;
            }

            var bit = bits == 0;
            if (reversePolarity)
            {
                bit = !bit;
            }

            var c = PushVaricodeBit(bit);
            if (c is null)
            {
                return;
            }

            decoded.Add(c.Value);
            CharacterCount += 1;
        }

        private void UpdateDcd(double phase, int bits)
        {
            // fldigi tracks phase quality as a low-pass complex metric.
            // For BPSK, n=2 and symbits=1, so dcdshreg shifts by two bits.
            var cval = Math.Cos(2.0 * phase);
            var sval = Math.Sin(2.0 * phase);
            var realAttackOrDecay = cval > _quality.Real ? 50.0 : 50.0;
            var imagAttackOrDecay = sval > _quality.Real ? 50.0 : 50.0;
            _quality = new Complex(
                DecayAverage(_quality.Real, cval, realAttackOrDecay),
                DecayAverage(_quality.Imaginary, sval, imagAttackOrDecay));
            var qualityNorm = (_quality.Real * _quality.Real) + (_quality.Imaginary * _quality.Imaginary);
            Metric = Math.Clamp(100.0 * qualityNorm, 0.0, 100.0);
            _afcMetric = DecayAverage(_afcMetric, qualityNorm, 50.0);

            unchecked
            {
                _dcdShiftRegister = (_dcdShiftRegister << 2) | (uint)bits;
            }

            var setDcd = -1;
            switch (_dcdShiftRegister)
            {
                case 0xAAAAAAAA:
                    setDcd = 1;
                    break;
                case 0x00000000:
                    setDcd = 0;
                    break;
                default:
                    if (Metric > SquelchMetricThreshold)
                    {
                        IsDcdOpen = true;
                    }
                    else
                    {
                        IsDcdOpen = false;
                    }

                    _dcdOffCounter = Math.Max(0, _dcdOffCounter - 1);
                    break;
            }

            if (setDcd == 1)
            {
                _dcdOffCounter = 0;
                IsDcdOpen = true;
                _quality = Complex.One;
                Metric = 100.0;
            }
            else if (setDcd == 0 && ++_dcdOffCounter > 5)
            {
                _dcdOffCounter = 0;
                IsDcdOpen = false;
                _quality = Complex.Zero;
                Metric = 0.0;
                _shiftRegister = 0;
            }
        }

        private void PhaseAfc(double phase, int bits)
        {
            if (!IsDcdOpen || _afcMetric < 0.05)
            {
                FrequencyErrorHz = 0.0;
                return;
            }

            var error = phase - (bits * Math.PI / 2.0);
            if (error < -Math.PI / 2.0 || error > Math.PI / 2.0)
            {
                return;
            }

            var symbolFrequencyErrorHz = error * _sampleRate / (Math.Tau * _symbolSamples);
            if (Math.Abs(symbolFrequencyErrorHz) >= _singleCarrierBandwidthHz)
            {
                return;
            }

            FrequencyErrorHz = symbolFrequencyErrorHz;
            var correctionStepHz = symbolFrequencyErrorHz / _dcdBits;
            var next = _trackedAudioCenterHz - correctionStepHz;
            _trackedAudioCenterHz = Math.Clamp(
                next,
                _originalAudioCenterHz - MaximumAfcCorrectionHz,
                _originalAudioCenterHz + MaximumAfcCorrectionHz);
            UpdateNcoStep();
        }

        private static double DecayAverage(double average, double value, double weight) =>
            average + ((value - average) / weight);

        private void UpdateNcoStep()
        {
            _ncoStep = Math.Tau * _trackedAudioCenterHz / _sampleRate;
        }

        private char? PushVaricodeBit(bool bit)
        {
            _shiftRegister = (_shiftRegister << 1) | (bit ? 1u : 0u);
            if ((_shiftRegister & 0x3u) != 0)
            {
                return null;
            }

            var decoded = PskVaricode.Decode(_shiftRegister >> 2);
            _shiftRegister = 0;
            if (decoded < 0 || decoded is 0)
            {
                return null;
            }

            return decoded is 10 or 13
                ? '\n'
                : decoded is >= 32 and <= 126
                    ? (char)decoded
                    : null;
        }

        private static double[] CreateRaisedCosineFilter(int length)
        {
            var taps = new double[length + 1];
            var k1 = Math.Tau / length;
            var k2 = 2.0 * length;
            for (var i = 0; i <= length; i++)
            {
                taps[i] = (1.0 - Math.Cos(k1 * i)) / k2;
            }

            return taps;
        }

        // fldigi src/psk/pskcoeff.cxx pskcore_filter, used for PSK31/63 RX.
        private static readonly double[] PskCoreFilter =
        [
            4.3453566e-005, -0.00049122414, -0.00078771292, -0.0013507826,
            -0.0021287814, -0.003133466, -0.004366817, -0.0058112187,
            -0.0074249976, -0.0091398882, -0.010860157, -0.012464086,
            -0.013807772, -0.014731191, -0.015067057, -0.014650894,
            -0.013333425, -0.01099166, -0.0075431246, -0.0029527849,
            0.0027546292, 0.0094932775, 0.017113308, 0.025403511,
            0.034099681, 0.042895839, 0.051458575, 0.059444853,
            0.066521003, 0.072381617, 0.076767694, 0.079481619,
            0.080420311, 0.079481619, 0.076767694, 0.072381617,
            0.066521003, 0.059444853, 0.051458575, 0.042895839,
            0.034099681, 0.025403511, 0.017113308, 0.0094932775,
            0.0027546292, -0.0029527849, -0.0075431246, -0.01099166,
            -0.013333425, -0.014650894, -0.015067057, -0.014731191,
            -0.013807772, -0.012464086, -0.010860157, -0.0091398882,
            -0.0074249976, -0.0058112187, -0.004366817, -0.003133466,
            -0.0021287814, -0.0013507826, -0.00078771292, -0.00049122414,
            4.3453566e-005,
        ];
    }

    private sealed class ComplexFirFilter
    {
        private readonly double[] _taps;
        private readonly Complex[] _buffer = new Complex[4096];
        private readonly int _decimation;
        private int _pointer;
        private int _counter;

        public ComplexFirFilter(double[] taps, int decimation)
        {
            _taps = taps;
            _decimation = Math.Max(1, decimation);
            _pointer = taps.Length;
        }

        public bool Run(Complex input, out Complex output)
        {
            _buffer[_pointer] = input;
            _counter += 1;
            output = Complex.Zero;
            if (_counter == _decimation)
            {
                output = MultiplyAccumulate();
            }

            _pointer += 1;
            if (_pointer == _buffer.Length)
            {
                Array.Copy(_buffer, _buffer.Length - _taps.Length, _buffer, 0, _taps.Length);
                _pointer = _taps.Length;
            }

            if (_counter != _decimation)
            {
                return false;
            }

            _counter = 0;
            return true;
        }

        private Complex MultiplyAccumulate()
        {
            var start = _pointer - _taps.Length;
            var real = 0.0;
            var imag = 0.0;
            for (var i = 0; i < _taps.Length; i++)
            {
                var sample = _buffer[start + i];
                var tap = _taps[i];
                real += sample.Real * tap;
                imag += sample.Imaginary * tap;
            }

            return new Complex(real, imag);
        }
    }

    private static class PskVaricode
    {
        // fldigi src/psk/pskvaricode.cxx varicodetab2, used by psk_varicode_decode.
        private static readonly uint[] DecodeTable =
        [
            0x2AB, 0x2DB, 0x2ED, 0x377, 0x2EB, 0x35F, 0x2EF, 0x2FD,
            0x2FF, 0x0EF, 0x01D, 0x36F, 0x2DD, 0x01F, 0x375, 0x3AB,
            0x2F7, 0x2F5, 0x3AD, 0x3AF, 0x35B, 0x36B, 0x36D, 0x357,
            0x37B, 0x37D, 0x3B7, 0x355, 0x35D, 0x3BB, 0x2FB, 0x37F,
            0x001, 0x1FF, 0x15F, 0x1F5, 0x1DB, 0x2D5, 0x2BB, 0x17F,
            0x0FB, 0x0F7, 0x16F, 0x1DF, 0x075, 0x035, 0x057, 0x1AF,
            0x0B7, 0x0BD, 0x0ED, 0x0FF, 0x177, 0x15B, 0x16B, 0x1AD,
            0x1AB, 0x1B7, 0x0F5, 0x1BD, 0x1ED, 0x055, 0x1D7, 0x2AF,
            0x2BD, 0x07D, 0x0EB, 0x0AD, 0x0B5, 0x077, 0x0DB, 0x0FD,
            0x155, 0x07F, 0x1FD, 0x17D, 0x0D7, 0x0BB, 0x0DD, 0x0AB,
            0x0D5, 0x1DD, 0x0AF, 0x06F, 0x06D, 0x157, 0x1B5, 0x15D,
            0x175, 0x17B, 0x2AD, 0x1F7, 0x1EF, 0x1FB, 0x2BF, 0x16D,
            0x2DF, 0x00B, 0x05F, 0x02F, 0x02D, 0x003, 0x03D, 0x05B,
            0x02B, 0x00D, 0x1EB, 0x0BF, 0x01B, 0x03B, 0x00F, 0x007,
            0x03F, 0x1BF, 0x015, 0x017, 0x005, 0x037, 0x07B, 0x06B,
            0x0DF, 0x05D, 0x1D5, 0x2B7, 0x1BB, 0x2B5, 0x2D7, 0x3B5,
            0x3BD, 0x3BF, 0x3D5, 0x3D7, 0x3DB, 0x3DD, 0x3DF, 0x3EB,
            0x3ED, 0x3EF, 0x3F5, 0x3F7, 0x3FB, 0x3FD, 0x3FF, 0x555,
            0x557, 0x55B, 0x55D, 0x55F, 0x56B, 0x56D, 0x56F, 0x575,
            0x577, 0x57B, 0x57D, 0x57F, 0x5AB, 0x5AD, 0x5AF, 0x5B5,
            0x5B7, 0x5BB, 0x5BD, 0x5BF, 0x5D5, 0x5D7, 0x5DB, 0x5DD,
            0x5DF, 0x5EB, 0x5ED, 0x5EF, 0x5F5, 0x5F7, 0x5FB, 0x5FD,
            0x5FF, 0x6AB, 0x6AD, 0x6AF, 0x6B5, 0x6B7, 0x6BB, 0x6BD,
            0x6BF, 0x6D5, 0x6D7, 0x6DB, 0x6DD, 0x6DF, 0x6EB, 0x6ED,
            0x6EF, 0x6F5, 0x6F7, 0x6FB, 0x6FD, 0x6FF, 0x755, 0x757,
            0x75B, 0x75D, 0x75F, 0x76B, 0x76D, 0x76F, 0x775, 0x777,
            0x77B, 0x77D, 0x77F, 0x7AB, 0x7AD, 0x7AF, 0x7B5, 0x7B7,
            0x7BB, 0x7BD, 0x7BF, 0x7D5, 0x7D7, 0x7DB, 0x7DD, 0x7DF,
            0x7EB, 0x7ED, 0x7EF, 0x7F5, 0x7F7, 0x7FB, 0x7FD, 0x7FF,
            0xAAB, 0xAAD, 0xAAF, 0xAB5, 0xAB7, 0xABB, 0xABD, 0xABF,
            0xAD5, 0xAD7, 0xADB, 0xADD, 0xADF, 0xAEB, 0xAED, 0xAEF,
            0xAF5, 0xAF7, 0xAFB, 0xAFD, 0xAFF, 0xB55, 0xB57, 0xB5B
        ];

        public static int Decode(uint symbol)
        {
            for (var i = 0; i < DecodeTable.Length; i++)
            {
                if (symbol == DecodeTable[i])
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
