using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;
using System.Text.Json;

namespace ShackStack.UI.ViewModels;

public partial class MainWindowViewModel
{
    private static readonly TimeSpan CwSkimmerRecorderWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CwSkimmerRecorderMinimumAutoWindow = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan CwSkimmerRecorderLaneCooldown = TimeSpan.FromSeconds(8);

    private readonly object _cwSkimmerRecorderLock = new();
    private readonly Queue<CwSkimmerRecordedAudioChunk> _cwSkimmerRecordedAudio = new();
    private readonly Dictionary<int, DateTimeOffset> _cwSkimmerLastRecordedLaneUtc = new();
    private string _cwSkimmerRecorderDirectory = string.Empty;
    private string _cwSkimmerRecorderAudioDirectory = string.Empty;
    private string _cwSkimmerRecorderManifestPath = string.Empty;
    private string _cwSkimmerRecorderSessionAudioPath = string.Empty;
    private long _cwSkimmerRecorderTotalSamples;
    private int _cwSkimmerRecorderSessionSampleRate;
    private int _cwSkimmerRecorderSessionChannels;

    [ObservableProperty]
    private bool cwSkimmerRecorderEnabled;

    [ObservableProperty]
    private string cwSkimmerRecorderStatus = "Recorder idle";

    [ObservableProperty]
    private int cwSkimmerRecorderSampleCount;

    [ObservableProperty]
    private string cwSkimmerRecorderPath = string.Empty;

    [RelayCommand]
    private void StartCwSkimmerRecorder()
    {
        if (_audioService is null)
        {
            CwSkimmerRecorderStatus = "Recorder unavailable: audio service missing.";
            return;
        }

        if (CwSkimmerRecorderEnabled)
        {
            CwSkimmerRecorderStatus = "Recorder already running.";
            return;
        }

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ShackStack", "cw-skimmer-captures");
        var sessionName = $"session-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}";
        _cwSkimmerRecorderDirectory = Path.Combine(root, sessionName);
        _cwSkimmerRecorderAudioDirectory = Path.Combine(_cwSkimmerRecorderDirectory, "audio");
        _cwSkimmerRecorderManifestPath = Path.Combine(_cwSkimmerRecorderDirectory, "manifest.jsonl");
        _cwSkimmerRecorderSessionAudioPath = Path.Combine(_cwSkimmerRecorderDirectory, "wideband.f32");
        Directory.CreateDirectory(_cwSkimmerRecorderAudioDirectory);
        File.WriteAllText(
            _cwSkimmerRecorderManifestPath,
            JsonSerializer.Serialize(new
            {
                type = "session",
                createdUtc = DateTimeOffset.UtcNow,
                format = "float32-le raw receive audio",
                sessionAudioFile = "wideband.f32",
                recorderWindowSeconds = CwSkimmerRecorderWindow.TotalSeconds
            }) + Environment.NewLine);
        File.WriteAllBytes(_cwSkimmerRecorderSessionAudioPath, []);

        lock (_cwSkimmerRecorderLock)
        {
            _cwSkimmerRecordedAudio.Clear();
            _cwSkimmerLastRecordedLaneUtc.Clear();
            _cwSkimmerRecorderTotalSamples = 0;
            _cwSkimmerRecorderSessionSampleRate = 0;
            _cwSkimmerRecorderSessionChannels = 0;
        }

        CwSkimmerRecorderSampleCount = 0;
        CwSkimmerRecorderPath = _cwSkimmerRecorderDirectory;
        CwSkimmerRecorderEnabled = true;
        _cwSkimmerRecorderAudioSubscription = _audioService.ReceiveStream.Subscribe(new Observer<AudioBuffer>(RecordCwSkimmerAudioBuffer));
        CwSkimmerRecorderStatus = $"Recording skimmer samples to {_cwSkimmerRecorderDirectory}";
    }

    [RelayCommand]
    private void StopCwSkimmerRecorder()
    {
        CwSkimmerRecorderEnabled = false;
        _cwSkimmerRecorderAudioSubscription?.Dispose();
        _cwSkimmerRecorderAudioSubscription = null;
        CwSkimmerRecorderStatus = CwSkimmerRecorderSampleCount == 1
            ? $"Recorder stopped. 1 sample saved."
            : $"Recorder stopped. {CwSkimmerRecorderSampleCount} samples saved.";
    }

    [RelayCommand]
    private void CaptureCwSkimmerSnapshot()
    {
        if (!CwSkimmerRecorderEnabled)
        {
            StartCwSkimmerRecorder();
        }

        var candidate = new CwSkimmerCandidate(
            "manual snapshot",
            0.0,
            0.0,
            CwWpm,
            "manual capture",
            "CW Experiment Desk");
        SaveCwSkimmerSample(candidate, force: true);
    }

    private void RecordCwSkimmerAudioBuffer(AudioBuffer buffer)
    {
        if (!CwSkimmerRecorderEnabled || buffer.Samples.Length == 0 || buffer.SampleRate <= 0 || buffer.Channels <= 0)
        {
            return;
        }

        var samples = new float[buffer.Samples.Length];
        Array.Copy(buffer.Samples, samples, samples.Length);
        var chunk = new CwSkimmerRecordedAudioChunk(samples, buffer.SampleRate, buffer.Channels, DateTimeOffset.UtcNow);

        lock (_cwSkimmerRecorderLock)
        {
            AppendCwSkimmerSessionAudioLocked(samples, buffer.SampleRate, buffer.Channels);
            _cwSkimmerRecordedAudio.Enqueue(chunk);
            TrimCwSkimmerRecorderBufferLocked();
        }
    }

    private void RecordCwSkimmerCandidate(CwSkimmerCandidate candidate)
    {
        if (!CwSkimmerRecorderEnabled)
        {
            return;
        }

        var hasCopy = !string.IsNullOrWhiteSpace(candidate.Text) || !string.IsNullOrWhiteSpace(candidate.LaneTranscript);
        if (!hasCopy || candidate.LaneId <= 0 || candidate.Confidence < 0.33)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_cwSkimmerLastRecordedLaneUtc.TryGetValue(candidate.LaneId, out var last) && now - last < CwSkimmerRecorderLaneCooldown)
        {
            return;
        }

        _cwSkimmerLastRecordedLaneUtc[candidate.LaneId] = now;
        SaveCwSkimmerSample(candidate, force: false);
    }

    private void SaveCwSkimmerSample(CwSkimmerCandidate candidate, bool force)
    {
        CwSkimmerRecordedAudioChunk[] chunks;
        long totalSessionSamples;
        int sessionSampleRate;
        int sessionChannels;
        lock (_cwSkimmerRecorderLock)
        {
            TrimCwSkimmerRecorderBufferLocked();
            chunks = _cwSkimmerRecordedAudio.ToArray();
            totalSessionSamples = _cwSkimmerRecorderTotalSamples;
            sessionSampleRate = _cwSkimmerRecorderSessionSampleRate;
            sessionChannels = _cwSkimmerRecorderSessionChannels;
        }

        if (chunks.Length == 0)
        {
            CwSkimmerRecorderStatus = "Recorder has no receive audio buffered yet.";
            return;
        }

        var sampleRate = chunks[^1].SampleRate;
        var channels = chunks[^1].Channels;
        var samples = chunks
            .Where(chunk => chunk.SampleRate == sampleRate && chunk.Channels == channels)
            .SelectMany(chunk => chunk.Samples)
            .ToArray();

        if (samples.Length == 0)
        {
            CwSkimmerRecorderStatus = "Recorder buffer did not contain compatible audio.";
            return;
        }

        var durationSeconds = samples.Length / (double)Math.Max(1, sampleRate * channels);
        if (!force && durationSeconds < CwSkimmerRecorderMinimumAutoWindow.TotalSeconds)
        {
            return;
        }

        Directory.CreateDirectory(_cwSkimmerRecorderAudioDirectory);
        var timestamp = DateTimeOffset.UtcNow;
        var id = $"{timestamp:yyyyMMddTHHmmssfffZ}-lane{Math.Max(0, candidate.LaneId):00}";
        var audioFileName = $"{id}.f32";
        var audioPath = Path.Combine(_cwSkimmerRecorderAudioDirectory, audioFileName);

        using (var stream = File.Create(audioPath))
        using (var writer = new BinaryWriter(stream))
        {
            foreach (var sample in samples)
            {
                writer.Write(sample);
            }
        }

        var manifest = new
        {
            type = "cw-skimmer-sample",
            id,
            capturedUtc = timestamp,
            audioFile = Path.Combine("audio", audioFileName).Replace('\\', '/'),
            sessionAudioFile = "wideband.f32",
            audioFormat = "float32-le",
            sampleRate,
            channels,
            durationSeconds,
            rollingStartSample = Math.Max(0, totalSessionSamples - samples.Length),
            rollingEndSample = totalSessionSamples,
            sessionSampleRate,
            sessionChannels,
            force,
            radioFrequencyHz = CurrentFrequencyHz,
            radioMode = ModeDisplay,
            laneId = candidate.LaneId,
            audioToneHz = candidate.AudioToneHz,
            confidence = candidate.Confidence,
            estimatedWpm = candidate.EstimatedWpm,
            laneAge = candidate.LaneAge,
            toneProbability = candidate.ToneProbability,
            sidebandCount = candidate.SidebandCount,
            occupiedBandwidthHz = candidate.OccupiedBandwidthHz,
            copyRateCharactersPerSecond = candidate.CopyRateCharactersPerSecond,
            firstPassText = candidate.Text,
            laneTranscript = candidate.LaneTranscript,
            neuralText = candidate.NeuralText,
            neuralTokenText = candidate.NeuralTokenText,
            neuralBadLaneScore = candidate.NeuralBadLaneScore,
            neuralTokenCandidates = candidate.NeuralTokenCandidates,
            neuralStatus = candidate.NeuralStatus,
            scoutText = candidate.ScoutText,
            timingHint = candidate.TimingHint,
            skimmer = new
            {
                lowerAudioHz = CwSkimmerLowerAudioHz,
                upperAudioHz = CwSkimmerUpperAudioHz,
                laneSpacingHz = CwSkimmerLaneSpacingHz,
                maxLanes = CwSkimmerMaxLanes,
                neuralSecondPassEnabled = CwSkimmerNeuralSecondPassEnabled
            }
        };

        File.AppendAllText(_cwSkimmerRecorderManifestPath, JsonSerializer.Serialize(manifest) + Environment.NewLine);
        CwSkimmerRecorderSampleCount++;
        CwSkimmerRecorderStatus = $"Saved sample {CwSkimmerRecorderSampleCount}: lane {candidate.LaneId} {candidate.AudioToneHz:0} Hz";
    }

    private void AppendCwSkimmerSessionAudioLocked(float[] samples, int sampleRate, int channels)
    {
        if (string.IsNullOrWhiteSpace(_cwSkimmerRecorderSessionAudioPath) || samples.Length == 0)
        {
            return;
        }

        if (_cwSkimmerRecorderSessionSampleRate == 0)
        {
            _cwSkimmerRecorderSessionSampleRate = sampleRate;
            _cwSkimmerRecorderSessionChannels = channels;
        }

        if (_cwSkimmerRecorderSessionSampleRate != sampleRate || _cwSkimmerRecorderSessionChannels != channels)
        {
            return;
        }

        using var stream = new FileStream(_cwSkimmerRecorderSessionAudioPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new BinaryWriter(stream);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        _cwSkimmerRecorderTotalSamples += samples.Length;
    }

    private void TrimCwSkimmerRecorderBufferLocked()
    {
        var keepSamples = _cwSkimmerRecordedAudio.Count == 0
            ? 0
            : (int)Math.Ceiling(CwSkimmerRecorderWindow.TotalSeconds * _cwSkimmerRecordedAudio.Peek().SampleRate * _cwSkimmerRecordedAudio.Peek().Channels);
        var totalSamples = _cwSkimmerRecordedAudio.Sum(chunk => chunk.Samples.Length);
        while (_cwSkimmerRecordedAudio.Count > 1 && totalSamples > keepSamples)
        {
            var removed = _cwSkimmerRecordedAudio.Dequeue();
            totalSamples -= removed.Samples.Length;
        }
    }

    private sealed record CwSkimmerRecordedAudioChunk(float[] Samples, int SampleRate, int Channels, DateTimeOffset CapturedUtc);
}
