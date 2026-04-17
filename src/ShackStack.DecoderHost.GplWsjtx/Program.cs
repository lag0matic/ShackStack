using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShackStack.DecoderHost.GplWsjtx.Ft4;
using ShackStack.DecoderHost.GplWsjtx.Ft8;

namespace ShackStack.DecoderHost.GplWsjtx;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private const double Ft8CandidateStartHz = 200.0;
    private const double Ft8CandidateEndHz = 3000.0;
    private const double Ft8SyncMinimum = 1.6;

    private static async Task<int> Main()
    {
        var worker = new GplWsjtxWorker();
        worker.EmitTelemetry("WSJT-X GPL sidecar ready");

        while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            Envelope? message;
            try
            {
                message = JsonSerializer.Deserialize<Envelope>(line, JsonOptions);
            }
            catch (Exception ex)
            {
                worker.EmitTelemetry($"JSON parse error: {ex.Message}");
                continue;
            }

            if (message?.Type is null)
            {
                continue;
            }

            try
            {
                switch (message.Type)
                {
                    case "configure":
                        worker.Configure(message);
                        break;
                    case "start":
                        worker.Start(message);
                        break;
                    case "stop":
                        worker.Stop();
                        break;
                    case "reset":
                        worker.Reset();
                        break;
                    case "audio":
                        worker.HandleAudio(message);
                        break;
                    case "shutdown":
                        worker.Stop();
                        return 0;
                }
            }
            catch (Exception ex)
            {
                worker.EmitTelemetry($"WSJT-X GPL sidecar error: {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine(ex);
            }
        }

        return 0;
    }

    private sealed class GplWsjtxWorker
    {
        private readonly GenericCycleBuffer _genericCycleBuffer = new();
        private readonly Ft8CandidateSearchPort _candidateSearch = new();
        private readonly Ft8DownsamplePort _downsample = new();
        private readonly Ft8SyncPort _sync = new();
        private readonly Ft8SymbolMetricsPort _metrics = new();
        private readonly Ft8BpDecoderPort _bpDecoder = new();
        private readonly Ft8OsdDecoderPort _osdDecoder = new();
        private readonly Ft8MessageUnpackerPort _messageUnpacker = new();
        private readonly Ft8SubtractorPort _ft8Subtractor = new();
        private readonly Jt9ExternalDecoderPort? _jt9ExternalDecoder = Jt9ExternalDecoderPort.CreateDefault();
        private readonly Ft4CandidateSearchPort _ft4CandidateSearch = new();
        private readonly Ft4DownsamplePort _ft4Downsample = new();
        private readonly Ft4SyncPort _ft4Sync = new();
        private readonly Ft4BitMetricsPort _ft4Metrics = new();
        private static readonly int[] Ft4Rvec =
        [
            0,1,0,0,1,0,1,0,0,1,0,1,1,1,1,0,1,0,0,0,1,0,0,1,1,0,1,1,0,
            1,0,0,1,0,1,1,0,0,0,0,1,0,0,0,1,0,1,0,0,1,1,1,1,0,0,1,0,1,
            0,1,0,1,0,1,1,0,1,1,1,1,1,0,0,0,1,0,1
        ];

        private string _modeLabel = "FT8";
        private string _frequencyLabel = "20m FT8 14.074 MHz USB-D";
        private bool _ft8SubtractionEnabled;
        private bool _ft8ApEnabled;
        private bool _ft8OsdEnabled;
        private string _stationCallsign = string.Empty;
        private string _stationGridSquare = string.Empty;
        private readonly List<string> _recentFt8HisCalls = [];
        private bool _isRunning;
        private int _decodeCount;
        private int _cycleCount;
        private int _lastSampleRate = Ft8Constants.InputSampleRate;
        private int _lastChannels = 1;
        private DateTimeOffset _lastBoundaryWaitTelemetryUtc = DateTimeOffset.MinValue;

        public void Configure(Envelope payload)
        {
            _modeLabel = payload.ModeLabel ?? _modeLabel;
            _frequencyLabel = payload.FrequencyLabel ?? _frequencyLabel;
            _ft8SubtractionEnabled = payload.Ft8SubtractionEnabled ?? _ft8SubtractionEnabled;
            _ft8ApEnabled = payload.Ft8ApEnabled ?? _ft8ApEnabled;
            _ft8OsdEnabled = payload.Ft8OsdEnabled ?? _ft8OsdEnabled;
            _stationCallsign = payload.StationCallsign ?? _stationCallsign;
            _stationGridSquare = payload.StationGridSquare ?? _stationGridSquare;
            EmitTelemetry($"Configured {_modeLabel} ({_frequencyLabel}) | Sub {_ft8SubtractionEnabled} | AP {_ft8ApEnabled} | OSD {_ft8OsdEnabled}");
        }

        public void Start(Envelope? payload = null)
        {
            if (payload is not null)
            {
                _modeLabel = payload.ModeLabel ?? _modeLabel;
                _frequencyLabel = payload.FrequencyLabel ?? _frequencyLabel;
                _ft8SubtractionEnabled = payload.Ft8SubtractionEnabled ?? _ft8SubtractionEnabled;
                _ft8ApEnabled = payload.Ft8ApEnabled ?? _ft8ApEnabled;
                _ft8OsdEnabled = payload.Ft8OsdEnabled ?? _ft8OsdEnabled;
                _stationCallsign = payload.StationCallsign ?? _stationCallsign;
                _stationGridSquare = payload.StationGridSquare ?? _stationGridSquare;
            }

            _isRunning = true;
            _genericCycleBuffer.Reset(GetInputSamplesPerCycle(_modeLabel));
            _recentFt8HisCalls.Clear();
            EmitTelemetry($"WSJT-X GPL sidecar started ({_modeLabel})");
        }

        public void Stop()
        {
            _isRunning = false;
            EmitTelemetry("WSJT-X GPL sidecar stopped");
        }

        public void Reset()
        {
            _decodeCount = 0;
            _cycleCount = 0;
            _genericCycleBuffer.Reset(GetInputSamplesPerCycle(_modeLabel));
            _recentFt8HisCalls.Clear();
            EmitTelemetry("WSJT-X GPL sidecar reset");
        }

        public void HandleAudio(Envelope payload)
        {
            if (!_isRunning)
            {
                return;
            }

            _lastSampleRate = payload.SampleRate.GetValueOrDefault(_lastSampleRate);
            _lastChannels = payload.Channels.GetValueOrDefault(_lastChannels);
            if (string.IsNullOrWhiteSpace(payload.Samples))
            {
                return;
            }

            var decoded = DecodeFloatSamples(payload.Samples);
            if (decoded.Length == 0)
            {
                return;
            }

            var cycles = GetCycles(decoded, DateTimeOffset.UtcNow);
            if (cycles.Count == 0)
            {
                EmitBoundaryWaitTelemetryIfNeeded();
            }

            foreach (var cycle in cycles)
            {
                try
                {
                    ProcessExternalModeCycle(cycle);
                }
                catch (Exception ex)
                {
                    EmitTelemetry($"{_modeLabel} cycle error: {ex.GetType().Name}: {ex.Message}");
                    Console.Error.WriteLine(ex);
                }
            }
        }

        public void EmitTelemetry(string status)
        {
            Emit(new
            {
                type = "telemetry",
                isRunning = _isRunning,
                status,
                activeWorker = "WSJT-X GPL sidecar",
                modeLabel = _modeLabel,
                signalLevelPercent = 0,
                decodeCount = _decodeCount,
                autoSequenceEnabled = false,
                isTransmitArmed = false,
            });
        }

        private void EmitBoundaryWaitTelemetryIfNeeded()
        {
            var now = DateTimeOffset.UtcNow;
            if ((now - _lastBoundaryWaitTelemetryUtc) < TimeSpan.FromSeconds(1))
            {
                return;
            }

            _lastBoundaryWaitTelemetryUtc = now;
            if (_genericCycleBuffer.TrimRemainingSamples > 0)
            {
                var trimSeconds = _genericCycleBuffer.TrimRemainingSamples / (double)Ft8Constants.InputSampleRate;
                var pendingSeconds = _genericCycleBuffer.PendingSamples / (double)Ft8Constants.InputSampleRate;
                EmitTelemetry($"{_modeLabel} waiting for cycle boundary | trim {trimSeconds:0.0}s | buffered {pendingSeconds:0.0}s");
            }
        }

        private IReadOnlyList<float[]> GetCycles(float[] decoded, DateTimeOffset utcNow)
        {
            return _genericCycleBuffer.Append(decoded, _lastSampleRate, _lastChannels, utcNow);
        }

        private static int GetInputSamplesPerCycle(string modeLabel) => modeLabel.Trim().ToUpperInvariant() switch
        {
            "FT4" => Ft4Constants.InputSamplesPerCycle,
            "FT8" => Ft8Constants.InputSamplesPerCycle,
            "Q65" => Ft8Constants.InputSampleRate * 15,
            "FST4" => Ft8Constants.InputSampleRate * 15,
            "FST4W" => Ft8Constants.InputSampleRate * 120,
            "JT65" => Ft8Constants.InputSampleRate * 60,
            "JT9" => Ft8Constants.InputSampleRate * 60,
            "JT4" => Ft8Constants.InputSampleRate * 60,
            "WSPR" => Ft8Constants.InputSampleRate * 120,
            "MSK144" => Ft8Constants.InputSampleRate * 15,
            _ => Ft8Constants.InputSamplesPerCycle,
        };

        private void ProcessExternalModeCycle(float[] cycle)
        {
            _cycleCount += 1;
            if (_jt9ExternalDecoder is null)
            {
                EmitTelemetry($"{_modeLabel} cycle {_cycleCount}: decoder binary missing");
                return;
            }

            var result = _jt9ExternalDecoder.DecodeCycle(_modeLabel, cycle, _stationCallsign, _stationGridSquare, _cycleCount);
            foreach (var decode in result.Decodes)
            {
                _decodeCount += 1;
                var decodeMode = string.IsNullOrWhiteSpace(decode.ModeLabel) ? _modeLabel : decode.ModeLabel;
                var isDirectedToMe = !string.IsNullOrWhiteSpace(_stationCallsign)
                    && decode.MessageText.Contains(_stationCallsign.Trim(), StringComparison.OrdinalIgnoreCase);
                Emit(new
                {
                    type = "decode",
                    timestampUtc = DateTime.UtcNow,
                    modeLabel = decodeMode,
                    frequencyOffsetHz = decode.FrequencyHz,
                    snrDb = decode.SnrDb,
                    deltaTimeSeconds = decode.DtSeconds,
                    messageText = decode.MessageText,
                    confidence = 1.0,
                    isDirectedToMe = isDirectedToMe,
                    isCq = decode.MessageText.StartsWith("CQ ", StringComparison.OrdinalIgnoreCase),
                });

                if (string.Equals(decodeMode, "FT8", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateRecentFt8HisCalls(decode.MessageText);
                }
            }

            EmitTelemetry(result.Summary);
        }

        private Ft4SyncCandidate? SearchFt4Candidate(float[] cycle, Ft4PreCandidate seed)
        {
            var coarseLane = _ft4Downsample.ExtractLane(seed.FrequencyHz);
            NormalizeInPlace(coarseLane);

            var bestSync = double.MinValue;
            var bestStart = -1;
            var bestDf = 0.0;
            var bestRefinedHz = seed.FrequencyHz;
            double? firstSegmentSync = null;
            var segmentRanges = new (int Start, int End)[]
            {
                (108, 560),
                (560, 1012),
                (-344, 108),
            };

            for (var segmentIndex = 0; segmentIndex < segmentRanges.Length; segmentIndex++)
            {
                var (segmentStart, segmentEnd) = segmentRanges[segmentIndex];
                var coarse = SearchFt4Window(coarseLane, segmentStart, segmentEnd, startStep: 4, -12, 12, 3);
                if (coarse.SyncScore < 1.2)
                {
                    continue;
                }

                var refined = SearchFt4Window(coarseLane, coarse.StartIndex - 5, coarse.StartIndex + 5, startStep: 1, coarse.DfIndex - 4, coarse.DfIndex + 4, 1);
                if (refined.SyncScore < 1.2)
                {
                    continue;
                }

                if (segmentIndex == 0)
                {
                    firstSegmentSync = refined.SyncScore;
                }
                else if (firstSegmentSync is not null && refined.SyncScore < firstSegmentSync.Value)
                {
                    continue;
                }

                if (refined.SyncScore < bestSync)
                {
                    continue;
                }

                bestSync = refined.SyncScore;
                bestStart = refined.StartIndex;
                bestDf = refined.DfHz;
                bestRefinedHz = seed.FrequencyHz + refined.DfHz;
            }

            if (bestStart < 0 || bestSync < 1.2)
            {
                return null;
            }

            _ft4Downsample.Prepare(cycle);
            var finalLane = _ft4Downsample.ExtractLane(bestRefinedHz);
            NormalizeInPlace(finalLane);
            var finalSync = _ft4Sync.Compute(finalLane, bestStart, tweak: null);
            return new Ft4SyncCandidate(bestRefinedHz, bestStart, finalSync, bestDf, seed.SyncScore);
        }

        private Ft4SearchResult SearchFt4Window(Complex[] lane, int startMin, int startMax, int startStep, int dfMin, int dfMax, int dfStep)
        {
            var best = new Ft4SearchResult(-1, 0, double.MinValue);
            for (var idf = dfMin; idf <= dfMax; idf += dfStep)
            {
                var tweak = _ft4Sync.BuildFrequencyTweak(idf);
                for (var start = startMin; start <= startMax; start += Math.Max(1, startStep))
                {
                    var sync = _ft4Sync.Compute(lane, start, tweak);
                    if (sync > best.SyncScore)
                    {
                        best = new Ft4SearchResult(start, idf, sync);
                    }
                }
            }

            return best with { DfHz = best.DfIndex };
        }

        private static Complex[] ExtractFt4Frame(Complex[] lane, int startIndex)
        {
            var frameLength = Ft4Constants.ChannelSymbols * Ft4Constants.DownsampledSamplesPerSymbol;
            var cd = new Complex[frameLength];

            if (startIndex >= 0)
            {
                var copyLength = Math.Min(frameLength, Math.Max(0, lane.Length - startIndex));
                if (copyLength > 0)
                {
                    Array.Copy(lane, startIndex, cd, 0, copyLength);
                }
            }
            else
            {
                var destinationIndex = -startIndex;
                var copyLength = Math.Min(frameLength - destinationIndex, lane.Length);
                if (copyLength > 0 && destinationIndex < frameLength)
                {
                    Array.Copy(lane, 0, cd, destinationIndex, copyLength);
                }
            }

            return cd;
        }

        private SyncCandidate? SearchFt8Candidate(Ft8PreCandidate seed)
        {
            var coarseLane = _downsample.ExtractLane(seed.FrequencyHz);
            var coarseCenter = (int)Math.Round((0.5 + seed.XdtSeconds) * Ft8Constants.DownsampledSampleRate, MidpointRounding.AwayFromZero);
            var (coarseOffset, coarseSync) = SearchStartOffset(coarseLane, coarseCenter - 10, coarseCenter + 10);
            if (coarseSync <= 0)
            {
                return null;
            }

            var (frequencyRefineHz, peakedSync) = SearchFrequencyOffset(coarseLane, coarseOffset);
            var refinedFrequencyHz = seed.FrequencyHz + frequencyRefineHz;
            var refinedLane = _downsample.ExtractLane(refinedFrequencyHz);
            var (finalOffset, finalSync) = SearchStartOffset(refinedLane, coarseOffset - 4, coarseOffset + 4);

            return new SyncCandidate(refinedFrequencyHz, finalOffset, finalSync > 0 ? finalSync : peakedSync, frequencyRefineHz, seed.SyncScore);
        }

        private (int Offset, double SyncScore) SearchStartOffset(System.Numerics.Complex[] lane, int start, int end)
        {
            var bestSync = double.MinValue;
            var bestOffset = start;
            for (var idt = start; idt <= end; idt++)
            {
                var sync = _sync.Compute(lane, idt, tweak: null);
                if (sync > bestSync)
                {
                    bestSync = sync;
                    bestOffset = idt;
                }
            }

            return (bestOffset, bestSync);
        }

        private (double FrequencyOffsetHz, double SyncScore) SearchFrequencyOffset(System.Numerics.Complex[] lane, int startOffset)
        {
            var bestSync = double.MinValue;
            var bestFrequencyOffset = 0.0;
            for (var ifr = -5; ifr <= 5; ifr++)
            {
                var delf = ifr * 0.5;
                var tweak = _sync.BuildFrequencyTweak(delf);
                var sync = _sync.Compute(lane, startOffset, tweak);
                if (sync > bestSync)
                {
                    bestSync = sync;
                    bestFrequencyOffset = delf;
                }
            }

            return (bestFrequencyOffset, bestSync);
        }

        private Ft8MetricResult? ExtractBestMetrics(System.Numerics.Complex[] lane, int centerOffset)
        {
            Ft8MetricResult? best = null;
            for (var delta = -4; delta <= 4; delta++)
            {
                var candidate = _metrics.Extract(lane, centerOffset + delta);
                if (candidate is null)
                {
                    continue;
                }

                if (best is null)
                {
                    best = candidate;
                    continue;
                }

                var candidateFull = candidate.Llra.Length == 174;
                var bestFull = best.Llra.Length == 174;
                if (candidateFull && !bestFull)
                {
                    best = candidate;
                    continue;
                }

                if (candidateFull == bestFull && candidate.HardSyncCount > best.HardSyncCount)
                {
                    best = candidate;
                }
            }

            return best;
        }

        private static float[] DecodeFloatSamples(string base64)
        {
            var rented = ArrayPool<byte>.Shared.Rent(base64.Length);
            try
            {
                if (!Convert.TryFromBase64String(base64, rented, out var written) || written < sizeof(float))
                {
                    return Array.Empty<float>();
                }

                var alignedBytes = written - (written % sizeof(float));
                if (alignedBytes <= 0)
                {
                    return Array.Empty<float>();
                }

                var samples = new float[alignedBytes / sizeof(float)];
                Buffer.BlockCopy(rented, 0, samples, 0, alignedBytes);
                return samples;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        private static void ConsiderCandidate(List<Ft8CandidateProbe> topCandidates, Ft8CandidateProbe candidate, int retainedCandidateLimit)
        {
            topCandidates.Add(candidate);
            topCandidates.Sort(static (left, right) =>
            {
                var hardSyncCompare = (right.Metrics?.HardSyncCount ?? 0).CompareTo(left.Metrics?.HardSyncCount ?? 0);
                if (hardSyncCompare != 0)
                {
                    return hardSyncCompare;
                }

                var fullCompare = (right.Metrics?.Llra.Length == 174 ? 1 : 0).CompareTo(left.Metrics?.Llra.Length == 174 ? 1 : 0);
                if (fullCompare != 0)
                {
                    return fullCompare;
                }

                return right.Candidate.SyncScore.CompareTo(left.Candidate.SyncScore);
            });
            if (topCandidates.Count > retainedCandidateLimit)
            {
                topCandidates.RemoveAt(topCandidates.Count - 1);
            }
        }

        private static void ConsiderFt4Candidate(List<Ft4SyncCandidate> candidates, Ft4SyncCandidate candidate)
        {
            candidates.Add(candidate);
            candidates.Sort((left, right) => right.SyncScore.CompareTo(left.SyncScore));
            if (candidates.Count > 5)
            {
                candidates.RemoveAt(candidates.Count - 1);
            }
        }

        private static void Emit(object payload)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            Console.Out.Flush();
        }

        private static string SummarizeLlr(double[] llr)
        {
            if (llr.Length == 0)
            {
                return "0";
            }

            var average = llr.Select(Math.Abs).Average();
            return average.ToString("0.00");
        }

        private static void NormalizeInPlace(Complex[] values)
        {
            var power = values.Sum(value => value.Magnitude * value.Magnitude) / Math.Max(1, values.Length);
            if (!(power > 0.0))
            {
                return;
            }

            var scale = 1.0 / Math.Sqrt(power);
            for (var i = 0; i < values.Length; i++)
            {
                values[i] *= scale;
            }
        }

        private void UpdateRecentFt8HisCalls(string decodedMessage)
        {
            if (string.IsNullOrWhiteSpace(decodedMessage))
            {
                return;
            }

            var tokens = decodedMessage
                .ToUpperInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length < 2)
            {
                return;
            }

            var stationCall = _stationCallsign.Trim().ToUpperInvariant();
            var firstCall = LooksLikeStandardCall(tokens[0]) ? tokens[0] : null;
            var secondCall = LooksLikeStandardCall(tokens[1]) ? tokens[1] : null;
            if (string.IsNullOrWhiteSpace(firstCall) || string.IsNullOrWhiteSpace(secondCall))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(stationCall))
            {
                if (string.Equals(firstCall, stationCall, StringComparison.OrdinalIgnoreCase))
                {
                    RememberRecentFt8HisCall(secondCall);
                    return;
                }

                if (string.Equals(secondCall, stationCall, StringComparison.OrdinalIgnoreCase))
                {
                    RememberRecentFt8HisCall(firstCall);
                    return;
                }
            }

            RememberRecentFt8HisCall(secondCall);
        }

        private void RememberRecentFt8HisCall(string? hisCall)
        {
            if (string.IsNullOrWhiteSpace(hisCall))
            {
                return;
            }

            _recentFt8HisCalls.RemoveAll(existing => string.Equals(existing, hisCall, StringComparison.OrdinalIgnoreCase));
            _recentFt8HisCalls.Insert(0, hisCall.Trim().ToUpperInvariant());
            if (_recentFt8HisCalls.Count > 4)
            {
                _recentFt8HisCalls.RemoveRange(4, _recentFt8HisCalls.Count - 4);
            }
        }

        private static bool LooksLikeStandardCall(string token)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(token, "^[A-Z0-9]{1,2}[0-9][A-Z]{1,3}$");
        }

        private Ft8LdpcEvaluation EvaluateLdpc(Ft8MetricResult metrics)
        {
            Ft8BpDecodeResult DecodePass(string name, double[] llr, int[]? apmask = null)
            {
                try
                {
                    return _bpDecoder.Decode(llr, apmask: apmask, maxOsdSnapshots: _ft8OsdEnabled ? 3 : 0);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"LDPC {name} failed: {ex.Message}", ex);
                }
            }

            var decodeInputs = new List<(string Label, double[] Llr, int[]? ApMask)>
            {
                ("llr1", metrics.Llra, null),
                ("llr2", metrics.Llrb, null),
                ("llr3", metrics.Llrc, null),
                ("llr4", metrics.Llrd, null),
            };

            if (_ft8ApEnabled)
            {
                foreach (var apPass in Ft8ApPort.BuildPasses(metrics, _stationCallsign, _recentFt8HisCalls))
                {
                    decodeInputs.Add((apPass.Label, apPass.Llr, apPass.ApMask));
                }
            }

            var passes = decodeInputs
                .Select(input => (input.Label, input.Llr, input.ApMask, Result: DecodePass(input.Label, input.Llr, input.ApMask)))
                .ToList();

            var best = passes
                .OrderByDescending(p => p.Result.CrcOk)
                .ThenByDescending(p => p.Result.HasCodeword)
                .ThenBy(p => p.Result.UnsatisfiedParityChecks < 0 ? int.MaxValue : p.Result.UnsatisfiedParityChecks)
                .ThenBy(p => p.Result.Iterations)
                .First();

            Ft8OsdDecodeResult? bestOsd = null;
            if (_ft8OsdEnabled && !best.Result.CrcOk)
            {
                foreach (var pass in passes)
                {
                    var osdInputs = new List<double[]> { pass.Llr };
                    foreach (var snapshot in pass.Result.OsdSnapshots)
                    {
                        osdInputs.Add(snapshot);
                    }

                    foreach (var osdInput in osdInputs)
                    {
                        var osd = _osdDecoder.Decode(osdInput, pass.ApMask, order: 1);
                        if (!osd.CrcOk)
                        {
                            continue;
                        }

                        if (bestOsd is null || osd.Distance < bestOsd.Distance)
                        {
                            bestOsd = osd;
                        }
                    }
                }
            }

            string? decodedMessage = null;
            int[]? messageBits77 = null;
            if (bestOsd is not null)
            {
                if (bestOsd.Decoded91 is { Length: >= 91 })
                {
                    messageBits77 = new int[77];
                    Array.Copy(bestOsd.Decoded91, messageBits77, 77);
                    if (_messageUnpacker.TryUnpack(messageBits77, out var text))
                    {
                        decodedMessage = text;
                    }
                }

                return new Ft8LdpcEvaluation(
                    $"osd ok order {bestOsd.Order}",
                    true,
                    decodedMessage,
                    messageBits77,
                    0);
            }

            if (best.Result.CrcOk)
            {
                if (best.Result.Decoded91 is { Length: >= 91 })
                {
                    messageBits77 = new int[77];
                    Array.Copy(best.Result.Decoded91, messageBits77, 77);
                    if (_messageUnpacker.TryUnpack(messageBits77, out var text))
                    {
                        decodedMessage = text;
                    }
                }

                return new Ft8LdpcEvaluation(
                    $"ldpc ok iter {best.Result.Iterations}",
                    true,
                    decodedMessage,
                    messageBits77,
                    0);
            }

            if (best.Result.HasCodeword)
            {
                return new Ft8LdpcEvaluation(
                    $"ldpc crc-fail iter {best.Result.Iterations}",
                    false,
                    null,
                    null,
                    1);
            }

            var summary = best.Result.UnsatisfiedParityChecks >= 0
                ? $"ldpc n{best.Result.UnsatisfiedParityChecks} iter {best.Result.Iterations}"
                : $"ldpc fail iter {best.Result.Iterations}";
            var rank = best.Result.UnsatisfiedParityChecks >= 0 ? 10 + best.Result.UnsatisfiedParityChecks : 999;
            return new Ft8LdpcEvaluation(summary, false, null, null, rank);
        }

        private string SummarizeLdpcFt4(Ft4MetricResult metrics, out string? decodedMessage, out bool decodeSucceeded, out int[]? messageBits77)
        {
            decodedMessage = null;
            decodeSucceeded = false;
            messageBits77 = null;

            Ft8BpDecodeResult DecodePass(string name, double[] llr)
            {
                try
                {
                    return _bpDecoder.Decode(llr, 40);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"FT4 LDPC {name} failed: {ex.Message}", ex);
                }
            }

            var passes = new[]
            {
                DecodePass("llr1", metrics.Llra),
                DecodePass("llr2", metrics.Llrb),
                DecodePass("llr3", metrics.Llrc),
            };

            var best = passes
                .OrderByDescending(p => p.CrcOk)
                .ThenByDescending(p => p.HasCodeword)
                .ThenBy(p => p.UnsatisfiedParityChecks < 0 ? int.MaxValue : p.UnsatisfiedParityChecks)
                .ThenBy(p => p.Iterations)
                .First();

            if (best.CrcOk)
            {
                decodeSucceeded = true;
                if (best.Decoded91 is { Length: >= 91 })
                {
                    messageBits77 = new int[77];
                    Array.Copy(best.Decoded91, messageBits77, 77);
                    for (var i = 0; i < messageBits77.Length; i++)
                    {
                        messageBits77[i] = (messageBits77[i] + Ft4Rvec[i]) & 1;
                    }

                    if (_messageUnpacker.TryUnpack(messageBits77, out var text))
                    {
                        decodedMessage = text;
                    }
                }

                return $"ldpc ok iter {best.Iterations}";
            }

            if (best.HasCodeword)
            {
                return $"ldpc crc-fail iter {best.Iterations}";
            }

            return best.UnsatisfiedParityChecks >= 0
                ? $"ldpc n{best.UnsatisfiedParityChecks} iter {best.Iterations}"
                : $"ldpc fail iter {best.Iterations}";
        }
    }

    private sealed record SyncCandidate(double FrequencyOffsetHz, int StartOffsetSamples, double SyncScore, double FrequencyRefineHz, double PreSyncScore);
    private sealed record Ft8CandidateProbe(SyncCandidate Candidate, Ft8MetricResult? Metrics);
    private sealed record Ft4SyncCandidate(double FrequencyHz, int StartIndex, double SyncScore, double FrequencyRefineHz, double PreSyncScore);
    private sealed record Ft8LdpcEvaluation(string Summary, bool DecodeSucceeded, string? DecodedMessage, int[]? MessageBits77, int Rank);
    private sealed record Ft4SearchResult(int StartIndex, int DfIndex, double SyncScore)
    {
        public double DfHz { get; init; }
    }

    private sealed class Envelope
    {
        public string? Type { get; set; }
        public string? ModeLabel { get; set; }
        public string? FrequencyLabel { get; set; }
        public bool? Ft8SubtractionEnabled { get; set; }
        public bool? Ft8ApEnabled { get; set; }
        public bool? Ft8OsdEnabled { get; set; }
        public string? StationCallsign { get; set; }
        public string? StationGridSquare { get; set; }
        public int? SampleRate { get; set; }
        public int? Channels { get; set; }
        public string? Samples { get; set; }
    }
}
