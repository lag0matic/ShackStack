namespace ShackStack.Core.Abstractions.Models;

public sealed record FreedvDigitalVoiceConfiguration(
    string ModeLabel,
    string FrequencyLabel,
    bool UseCurrentRadioFrequency = true,
    int RxFrequencyOffsetHz = 0,
    string TransmitCallsign = "");
