namespace ShackStack.Core.Abstractions.Models;

public sealed record CwSkimmerConfiguration(
    int LowerAudioHz = 250,
    int UpperAudioHz = 2800,
    int LaneSpacingHz = 45,
    int MaxLanes = 8,
    int WpmHint = 20,
    bool NeuralSecondPassEnabled = false,
    string ScoutEngine = "Spectral Scout"
);
