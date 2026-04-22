namespace ShackStack.DecoderHost.Sstv.Protocol;

internal sealed record DecoderCommand(
    string? Type,
    string? Mode,
    string? FrequencyLabel,
    int? ManualSlant,
    int? ManualOffset,
    int? SampleRate,
    int? Channels,
    string? Samples);

