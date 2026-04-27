namespace ShackStack.Core.Abstractions.Models;

public sealed record WefaxDecoderConfiguration(
    string ModeLabel,
    int Ioc,
    int Lpm,
    string FrequencyLabel,
    int ManualSlant,
    int ManualOffset,
    int CenterHz,
    int ShiftHz,
    int MaxRows,
    string FilterName,
    bool AutoAlign,
    int AutoAlignAfterRows,
    int AutoAlignEveryRows,
    int AutoAlignStopRows,
    double CorrelationThreshold,
    int CorrelationRows,
    bool InvertImage,
    bool BinaryImage,
    int BinaryThreshold,
    bool NoiseRemoval,
    int NoiseThreshold,
    int NoiseMargin
);
