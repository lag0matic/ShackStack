namespace ShackStack.Core.Abstractions.Models;

public sealed record CwSkimmerCandidate(
    string Text,
    double Confidence,
    double AudioToneHz,
    int EstimatedWpm,
    string ScoutText,
    string Source,
    int LaneId = 0,
    int LaneAge = 0,
    double ToneProbability = 0.0,
    double EnvelopeLevel = 0.0,
    int SidebandCount = 0,
    double OccupiedBandwidthHz = 0.0,
    string TimingHint = "",
    string TimingPattern = "",
    double EstimatedElementMs = 0.0,
    string LaneTranscript = "",
    bool IsProvisionalText = false,
    double LaneDurationSeconds = 0.0,
    int CommittedCharacterCount = 0,
    double CopyRateCharactersPerSecond = 0.0,
    string NeuralText = "",
    string NeuralTokenText = "",
    double NeuralBadLaneScore = 0.0,
    string NeuralTokenCandidates = "",
    string NeuralStatus = ""
);
