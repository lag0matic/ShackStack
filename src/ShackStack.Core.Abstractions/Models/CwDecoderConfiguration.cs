namespace ShackStack.Core.Abstractions.Models;

public sealed record CwDecoderConfiguration(
    int PitchHz,
    int Wpm,
    string Profile
);
