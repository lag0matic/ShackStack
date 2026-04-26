using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShackStack.DecoderHost.GplFldigiRtty;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var worker = new FldigiRttyWorker();
        worker.EmitTelemetry("fldigi-derived RTTY sidecar ready");

        while (Console.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            RttyCommand? command;
            try
            {
                command = JsonSerializer.Deserialize<RttyCommand>(line, JsonOptions);
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
                worker.EmitTelemetry($"RTTY sidecar error: {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine(ex);
            }
        }

        return 0;
    }

    private sealed record RttyCommand(
        string? Type,
        string? ProfileLabel,
        int? ShiftHz,
        double? BaudRate,
        string? FrequencyLabel,
        double? AudioCenterHz,
        bool? ReversePolarity,
        int? SampleRate,
        int? Channels,
        string? Samples);

    private sealed class FldigiRttyWorker
    {
        private const int WorkingSampleRate = 8000;
        private const int MaxBits = (2 * WorkingSampleRate / 23) + 1;
        private static readonly char[] Letters =
        [
            '\0', 'E', '\n', 'A', ' ', 'S', 'I', 'U',
            '\r', 'D', 'R', 'J', 'N', 'F', 'C', 'K',
            'T', 'Z', 'L', 'W', 'H', 'Y', 'P', 'Q',
            'O', 'B', 'G', ' ', 'M', 'X', 'V', ' '
        ];
        private static readonly char[] Figures =
        [
            '\0', '3', '\n', '-', ' ', '\a', '8', '7',
            '\r', '$', '4', '\'', ',', '!', ':', '(',
            '5', '"', ')', '2', '#', '6', '0', '1',
            '9', '?', '&', ' ', '.', '/', ';', ' '
        ];

        private string _profileLabel = "170 Hz / 45.45 baud";
        private string _frequencyLabel = "Current radio frequency";
        private int _shiftHz = 170;
        private double _baudRate = 45.45;
        private double _audioCenterHz = 1700.0;
        private bool _reversePolarity;
        private bool _autoLocked;
        private double _lastAutoScore;
        private double _suggestedAudioCenterHz;
        private int _samplesSinceTuneAttempt;
        private int _symbolLength = (int)(WorkingSampleRate / 45.45 + 0.5);
        private bool _isRunning;
        private int _signalLevelPercent;
        private readonly List<double> _sampleBuffer = [];
        private readonly List<double> _retuneBuffer = [];
        private FldigiRttyDemodulator _demodulator = new(WorkingSampleRate, 1700.0, 170, 45.45);
        private FldigiBaudotStateMachine _baudot = new((int)(WorkingSampleRate / 45.45 + 0.5));

        public void Configure(RttyCommand command)
        {
            _profileLabel = command.ProfileLabel ?? _profileLabel;
            _frequencyLabel = command.FrequencyLabel ?? _frequencyLabel;
            _shiftHz = command.ShiftHz ?? _shiftHz;
            _baudRate = command.BaudRate ?? _baudRate;
            _audioCenterHz = Math.Clamp(command.AudioCenterHz ?? _audioCenterHz, 300.0, 3200.0);
            _reversePolarity = command.ReversePolarity ?? _reversePolarity;
            _symbolLength = Math.Max(8, (int)(WorkingSampleRate / _baudRate + 0.5));
            _demodulator = new FldigiRttyDemodulator(WorkingSampleRate, _audioCenterHz, _shiftHz, _baudRate);
            _baudot = new FldigiBaudotStateMachine(_symbolLength);
            _sampleBuffer.Clear();
            _retuneBuffer.Clear();
            _autoLocked = false;
            _lastAutoScore = 0;
            _suggestedAudioCenterHz = _audioCenterHz;
            _samplesSinceTuneAttempt = 0;
            EmitTelemetry($"Configured {_profileLabel} | audio center {_audioCenterHz:0} Hz | reverse {_reversePolarity} | fldigi RX chain + auto carrier lock");
        }

        public void Start()
        {
            _isRunning = true;
            EmitTelemetry($"RTTY listening | {_profileLabel} | audio center {_audioCenterHz:0} Hz");
        }

        public void Stop()
        {
            _isRunning = false;
            EmitTelemetry("RTTY receiver stopped");
        }

        public void Reset()
        {
            _sampleBuffer.Clear();
            _retuneBuffer.Clear();
            _demodulator = new FldigiRttyDemodulator(WorkingSampleRate, _audioCenterHz, _shiftHz, _baudRate);
            _baudot = new FldigiBaudotStateMachine(_symbolLength);
            _autoLocked = false;
            _lastAutoScore = 0;
            _suggestedAudioCenterHz = _audioCenterHz;
            _samplesSinceTuneAttempt = 0;
            _signalLevelPercent = 0;
            EmitTelemetry("RTTY decoder reset");
        }

        public void HandleAudio(RttyCommand command)
        {
            if (!_isRunning || string.IsNullOrWhiteSpace(command.Samples))
            {
                return;
            }

            var samples = DecodeFloat32(command.Samples);
            var mono = ToMono(samples, command.Channels ?? 1);
            var resampled = ResampleLinear(mono, command.SampleRate ?? WorkingSampleRate, WorkingSampleRate);
            TryAutoLockCarrier(resampled);
            var decoded = new List<char>();

            foreach (var sample in resampled)
            {
                var bit = _demodulator.Process(sample);
                var text = _baudot.Process(_reversePolarity ? !bit : bit);
                if (!string.IsNullOrEmpty(text))
                {
                    decoded.AddRange(text);
                }
            }

            _signalLevelPercent = _demodulator.SignalLevelPercent;
            if (decoded.Count > 0)
            {
                EmitDecode(new string(decoded.ToArray()), 0.72);
            }

            EmitTelemetry(
                $"RTTY running | fldigi ATC | audio center {_audioCenterHz:0} Hz{(_autoLocked ? " auto" : string.Empty)} | reverse {_reversePolarity} | " +
                $"mark {_audioCenterHz + _shiftHz / 2.0:0} Hz space {_audioCenterHz - _shiftHz / 2.0:0} Hz");
        }

        private void TryAutoLockCarrier(double[] samples)
        {
            if (_autoLocked)
            {
                return;
            }

            _retuneBuffer.AddRange(samples);
            var maxSamples = WorkingSampleRate * 3;
            if (_retuneBuffer.Count > maxSamples)
            {
                _retuneBuffer.RemoveRange(0, _retuneBuffer.Count - maxSamples);
            }

            var requiredSamples = WorkingSampleRate * 2;
            if (_retuneBuffer.Count < requiredSamples)
            {
                return;
            }

            _samplesSinceTuneAttempt += samples.Length;
            if (_samplesSinceTuneAttempt < WorkingSampleRate / 2)
            {
                return;
            }

            _samplesSinceTuneAttempt = 0;
            var analysis = _retuneBuffer.TakeLast(requiredSamples).ToArray();
            var estimate = EstimateCarrierCenter(analysis, _shiftHz, _reversePolarity);
            if (estimate is null)
            {
                return;
            }

            var (centerHz, score) = estimate.Value;
            _lastAutoScore = score;
            _suggestedAudioCenterHz = centerHz;
            if (score < 3.0)
            {
                return;
            }

            var distance = Math.Abs(centerHz - _audioCenterHz);
            if (distance < Math.Max(60.0, _shiftHz * 0.75))
            {
                _autoLocked = true;
                return;
            }

            _audioCenterHz = centerHz;
            _suggestedAudioCenterHz = centerHz;
            _demodulator = new FldigiRttyDemodulator(WorkingSampleRate, _audioCenterHz, _shiftHz, _baudRate);
            _baudot = new FldigiBaudotStateMachine(_symbolLength);
            _autoLocked = true;
            EmitTelemetry($"RTTY auto carrier lock: center {_audioCenterHz:0} Hz score {_lastAutoScore:0.0}");
        }

        private static (double CenterHz, double Score)? EstimateCarrierCenter(double[] samples, int shiftHz, bool reversePolarity)
        {
            if (samples.Length == 0)
            {
                return null;
            }

            ApplyHannInPlace(samples);
            var totalPower = samples.Sum(static s => s * s) / samples.Length;
            if (totalPower < 1e-7)
            {
                return null;
            }

            var spectrum = SpectrumPower(samples);
            var binHz = WorkingSampleRate / (double)samples.Length;
            var radiusBins = Math.Max(2, (int)Math.Round(25.0 / binHz));
            var startCenterHz = 500.0 + (shiftHz / 2.0);
            var endCenterHz = 3100.0 - (shiftHz / 2.0);
            var bestCenterHz = 0.0;
            var bestPairScore = 0.0;
            var noiseFloor = EstimateNoiseFloor(spectrum, binHz, 500.0, 3000.0);

            for (var centerHz = startCenterHz; centerHz <= endCenterHz; centerHz += 5.0)
            {
                var highBin = (int)Math.Round((centerHz + (shiftHz / 2.0)) / binHz);
                var lowBin = (int)Math.Round((centerHz - (shiftHz / 2.0)) / binHz);
                if (highBin <= 0 || lowBin <= 0 || highBin >= spectrum.Length || lowBin >= spectrum.Length)
                {
                    continue;
                }

                var highPower = BandPower(spectrum, highBin, radiusBins);
                var lowPower = BandPower(spectrum, lowBin, radiusBins);
                var pairPower = Math.Min(highPower, lowPower);
                var balance = Math.Min(highPower, lowPower) / Math.Max(Math.Max(highPower, lowPower), 1e-12);
                var pairScore = (pairPower / Math.Max(noiseFloor, 1e-12)) * balance;
                if (pairScore > bestPairScore)
                {
                    bestPairScore = pairScore;
                    bestCenterHz = centerHz;
                }
            }

            if (bestCenterHz <= 0)
            {
                return null;
            }

            var roundedCenterHz = Math.Clamp(Math.Round(bestCenterHz / 5.0) * 5.0, startCenterHz, endCenterHz);
            return (roundedCenterHz, bestPairScore);
        }

        private static void ApplyHannInPlace(double[] samples)
        {
            if (samples.Length <= 1)
            {
                return;
            }

            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] *= 0.5 - (0.5 * Math.Cos(2.0 * Math.PI * i / (samples.Length - 1)));
            }
        }

        private static double[] SpectrumPower(double[] samples)
        {
            var bins = (samples.Length / 2) + 1;
            var spectrum = new double[bins];
            for (var k = 0; k < bins; k++)
            {
                var real = 0.0;
                var imag = 0.0;
                var angleStep = -2.0 * Math.PI * k / samples.Length;
                for (var n = 0; n < samples.Length; n++)
                {
                    var angle = angleStep * n;
                    real += samples[n] * Math.Cos(angle);
                    imag += samples[n] * Math.Sin(angle);
                }

                spectrum[k] = (real * real) + (imag * imag);
            }

            return spectrum;
        }

        private static double BandPower(double[] spectrum, int centerBin, int radiusBins)
        {
            var sum = 0.0;
            var start = Math.Max(0, centerBin - radiusBins);
            var end = Math.Min(spectrum.Length - 1, centerBin + radiusBins);
            for (var i = start; i <= end; i++)
            {
                sum += spectrum[i];
            }

            return sum;
        }

        private static double EstimateNoiseFloor(double[] spectrum, double binHz, double startHz, double endHz)
        {
            var startBin = Math.Max(1, (int)Math.Ceiling(startHz / binHz));
            var endBin = Math.Min(spectrum.Length - 1, (int)Math.Floor(endHz / binHz));
            if (endBin <= startBin)
            {
                return 1e-12;
            }

            var values = new List<double>(endBin - startBin + 1);
            for (var bin = startBin; bin <= endBin; bin++)
            {
                values.Add(spectrum[bin]);
            }

            values.Sort();
            return values[values.Count / 2];
        }

        public void EmitTelemetry(string status)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "telemetry",
                isRunning = _isRunning,
                status,
                activeWorker = "fldigi GPL RTTY sidecar",
                signalLevelPercent = _signalLevelPercent,
                estimatedShiftHz = _shiftHz,
                estimatedBaud = _baudRate,
                profileLabel = _profileLabel,
                suggestedAudioCenterHz = _suggestedAudioCenterHz,
                tuneConfidence = _lastAutoScore,
                isCarrierLocked = _autoLocked,
            }, JsonOptions));
        }

        private static void EmitDecode(string text, double confidence)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "decode",
                text,
                confidence,
            }, JsonOptions));
        }

        private static float[] DecodeFloat32(string base64)
        {
            var bytes = Convert.FromBase64String(base64);
            var samples = new float[bytes.Length / sizeof(float)];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * 4, 4)));
            }

            return samples;
        }

        private static double[] ToMono(float[] samples, int channels)
        {
            channels = Math.Max(1, channels);
            if (channels == 1)
            {
                return samples.Select(static s => (double)s).ToArray();
            }

            var frames = samples.Length / channels;
            var mono = new double[frames];
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0.0;
                for (var ch = 0; ch < channels; ch++)
                {
                    sum += samples[(frame * channels) + ch];
                }

                mono[frame] = sum / channels;
            }

            return mono;
        }

        private static double[] ResampleLinear(double[] input, int inputRate, int outputRate)
        {
            if (input.Length == 0 || inputRate == outputRate)
            {
                return input;
            }

            var outputLength = Math.Max(1, (int)Math.Round(input.Length * (outputRate / (double)inputRate)));
            var output = new double[outputLength];
            var ratio = inputRate / (double)outputRate;
            for (var i = 0; i < output.Length; i++)
            {
                var position = i * ratio;
                var index = (int)position;
                var fraction = position - index;
                var a = input[Math.Clamp(index, 0, input.Length - 1)];
                var b = input[Math.Clamp(index + 1, 0, input.Length - 1)];
                output[i] = a + ((b - a) * fraction);
            }

            return output;
        }

        private sealed class FldigiRttyDemodulator
        {
            private readonly int _sampleRate;
            private readonly double _markFrequency;
            private readonly double _spaceFrequency;
            private readonly FirFilter _markFilter;
            private readonly FirFilter _spaceFilter;
            private double _markPhase;
            private double _spacePhase;
            private double _markEnv;
            private double _spaceEnv;
            private double _markNoise;
            private double _spaceNoise;

            public FldigiRttyDemodulator(int sampleRate, double centerHz, int shiftHz, double baudRate)
            {
                _sampleRate = sampleRate;
                _markFrequency = centerHz + (shiftHz / 2.0);
                _spaceFrequency = centerHz - (shiftHz / 2.0);
                var filterLength = baudRate >= 200 ? 128 : baudRate >= 150 ? 256 : 512;
                _markFilter = new FirFilter(DesignLowPass(filterLength, baudRate / sampleRate));
                _spaceFilter = new FirFilter(DesignLowPass(filterLength, baudRate / sampleRate));
                SymbolLength = Math.Max(8, (int)(sampleRate / baudRate + 0.5));
            }

            public int SignalLevelPercent { get; private set; }

            private int SymbolLength { get; }

            public bool Process(double sample)
            {
                var z = new Complex(sample, sample);
                var mark = Mix(ref _markPhase, _markFrequency, z);
                var space = Mix(ref _spacePhase, _spaceFrequency, z);
                var markFiltered = _markFilter.Process(mark);
                var spaceFiltered = _spaceFilter.Process(space);

                var markMag = markFiltered.Magnitude;
                var spaceMag = spaceFiltered.Magnitude;

                _markEnv = DecayAvg(_markEnv, markMag, markMag > _markEnv ? SymbolLength / 4 : SymbolLength * 16);
                _markNoise = DecayAvg(_markNoise, markMag, markMag < _markNoise ? SymbolLength / 4 : SymbolLength * 48);
                _spaceEnv = DecayAvg(_spaceEnv, spaceMag, spaceMag > _spaceEnv ? SymbolLength / 4 : SymbolLength * 16);
                _spaceNoise = DecayAvg(_spaceNoise, spaceMag, spaceMag < _spaceNoise ? SymbolLength / 4 : SymbolLength * 48);

                var noiseFloor = Math.Min(_spaceNoise, _markNoise);
                var markClipped = Math.Max(noiseFloor, Math.Min(markMag, _markEnv));
                var spaceClipped = Math.Max(noiseFloor, Math.Min(spaceMag, _spaceEnv));

                var v3 =
                    ((markClipped - noiseFloor) * (_markEnv - noiseFloor)) -
                    ((spaceClipped - noiseFloor) * (_spaceEnv - noiseFloor)) -
                    (0.25 * (
                        ((_markEnv - noiseFloor) * (_markEnv - noiseFloor)) -
                        ((_spaceEnv - noiseFloor) * (_spaceEnv - noiseFloor))));

                SignalLevelPercent = (int)Math.Clamp(Math.Round((markMag + spaceMag) * 2400.0), 0, 100);
                return v3 > 0;
            }

            private Complex Mix(ref double phase, double frequency, Complex input)
            {
                var mixed = new Complex(Math.Cos(phase), Math.Sin(phase)) * input;
                phase -= 2.0 * Math.PI * frequency / _sampleRate;
                if (phase < -2.0 * Math.PI)
                {
                    phase += 2.0 * Math.PI;
                }

                return mixed;
            }

            private static double DecayAvg(double average, double input, int weight)
            {
                if (weight <= 1)
                {
                    return input;
                }

                return ((input - average) / weight) + average;
            }

            private static double[] DesignLowPass(int length, double cutoff)
            {
                if (length % 2 == 0)
                {
                    length++;
                }

                var taps = new double[length];
                var mid = length / 2;
                var sum = 0.0;
                for (var i = 0; i < length; i++)
                {
                    var n = i - mid;
                    var sinc = n == 0
                        ? 2.0 * cutoff
                        : Math.Sin(2.0 * Math.PI * cutoff * n) / (Math.PI * n);
                    var x = i / (double)(length - 1);
                    var blackman = 0.42 - (0.50 * Math.Cos(2.0 * Math.PI * x)) + (0.08 * Math.Cos(4.0 * Math.PI * x));
                    taps[i] = sinc * blackman;
                    sum += taps[i];
                }

                for (var i = 0; i < taps.Length; i++)
                {
                    taps[i] /= sum;
                }

                return taps;
            }
        }

        private sealed class FirFilter
        {
            private readonly double[] _taps;
            private readonly Complex[] _history;
            private int _index;

            public FirFilter(double[] taps)
            {
                _taps = taps;
                _history = new Complex[taps.Length];
            }

            public Complex Process(Complex sample)
            {
                _history[_index] = sample;
                var acc = Complex.Zero;
                var historyIndex = _index;
                for (var i = 0; i < _taps.Length; i++)
                {
                    acc += _history[historyIndex] * _taps[i];
                    if (--historyIndex < 0)
                    {
                        historyIndex = _history.Length - 1;
                    }
                }

                if (++_index >= _history.Length)
                {
                    _index = 0;
                }

                return acc;
            }
        }

        private sealed class FldigiBaudotStateMachine
        {
            private enum RxState
            {
                Idle,
                Start,
                Data,
                Stop,
            }

            private readonly bool[] _bitBuffer = new bool[MaxBits];
            private readonly int _symbolLength;
            private RxState _rxState = RxState.Idle;
            private int _counter;
            private int _bitCounter;
            private int _rxData;
            private int _rxMode;
            private char _lastChar;
            private char _lastOutputChar;
            private bool _hasOutput;
            private int _samplesSinceLastOutput;

            public FldigiBaudotStateMachine(int symbolLength)
            {
                _symbolLength = Math.Clamp(symbolLength, 8, MaxBits);
            }

            public string? Process(bool bit)
            {
                _samplesSinceLastOutput++;
                Array.Copy(_bitBuffer, 1, _bitBuffer, 0, _symbolLength - 1);
                _bitBuffer[_symbolLength - 1] = bit;

                switch (_rxState)
                {
                    case RxState.Idle:
                        if (IsMarkSpace(out var correction))
                        {
                            _rxState = RxState.Start;
                            _counter = correction;
                        }
                        break;
                    case RxState.Start:
                        if (--_counter == 0)
                        {
                            if (!IsMark())
                            {
                                _rxState = RxState.Data;
                                _counter = _symbolLength;
                                _bitCounter = 0;
                                _rxData = 0;
                            }
                            else
                            {
                                _rxState = RxState.Idle;
                            }
                        }
                        break;
                    case RxState.Data:
                        if (--_counter == 0)
                        {
                            if (IsMark())
                            {
                                _rxData |= 1 << _bitCounter;
                            }

                            _bitCounter++;
                            _counter = _symbolLength;
                        }

                        if (_bitCounter == 5)
                        {
                            _rxState = RxState.Stop;
                        }
                        break;
                    case RxState.Stop:
                        if (--_counter == 0)
                        {
                            if (IsMark())
                            {
                                var ch = DecodeBaudot(_rxData);
                                _rxState = RxState.Idle;
                                if (ch != '\0')
                                {
                                    if ((ch == '\r' && _lastChar == '\r') || (ch == '\n' && _lastChar == '\n'))
                                    {
                                        return null;
                                    }

                                    _lastChar = ch;
                                    return FormatOutput(ch);
                                }
                            }

                            _rxState = RxState.Idle;
                        }
                        break;
                }

                return null;
            }

            private string FormatOutput(char ch)
            {
                var shouldRecoverMissedSpace =
                    _hasOutput &&
                    ch is not (' ' or '\r' or '\n') &&
                    _lastOutputChar is not (' ' or '\r' or '\n') &&
                    _samplesSinceLastOutput > _symbolLength * 10;

                _samplesSinceLastOutput = 0;
                _lastOutputChar = ch;
                _hasOutput = true;

                return shouldRecoverMissedSpace ? $" {ch}" : ch.ToString();
            }

            private bool IsMarkSpace(out int correction)
            {
                correction = 0;
                if (!_bitBuffer[0] || _bitBuffer[_symbolLength - 1])
                {
                    return false;
                }

                for (var i = 0; i < _symbolLength; i++)
                {
                    if (_bitBuffer[i])
                    {
                        correction++;
                    }
                }

                return Math.Abs((_symbolLength / 2) - correction) < 6;
            }

            private bool IsMark() => _bitBuffer[_symbolLength / 2];

            private char DecodeBaudot(int value)
            {
                value &= 0x1F;
                if (value == 0x1F)
                {
                    _rxMode = 0;
                    return '\0';
                }

                if (value == 0x1B)
                {
                    _rxMode = 1;
                    return '\0';
                }

                if (value == 0x04)
                {
                    _rxMode = 0;
                    return ' ';
                }

                return _rxMode == 0 ? Letters[value] : Figures[value];
            }
        }
    }
}
