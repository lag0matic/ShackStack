namespace ShackStack.Core.Abstractions.Models;

public sealed record CwDecoderConfiguration(
    int PitchHz,
    int Wpm,
    string Profile,
    int BandwidthHz = 220,
    bool MatchedFilterEnabled = true,
    bool TrackingEnabled = true,
    int TrackingRangeWpm = 8,
    int LowerWpmLimit = 5,
    int UpperWpmLimit = 60,
    string Attack = "Normal",
    string Decay = "Slow",
    string NoiseCharacter = "Suppress",
    bool AutoToneSearchEnabled = true,
    bool AfcEnabled = true,
    int ToneSearchSpanHz = 250,
    string Squelch = "Off",
    string Spacing = "Normal"
);
