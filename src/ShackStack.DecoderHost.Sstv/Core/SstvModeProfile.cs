namespace ShackStack.DecoderHost.Sstv.Core;

internal sealed record SstvModeProfile(
    SstvModeId Id,
    string Name,
    int VisCode,
    int Width,
    int Height,
    double TimingMs,
    bool Narrow,
    bool DecodePlanned,
    bool TransmitPlanned,
    string Family = "rgb",
    double SyncMs = 0.0,
    double ScanMs = 0.0,
    double GapMs = 0.0,
    double AuxScanMs = 0.0,
    double PorchMs = 0.0,
    double SyncPorchMs = 0.0,
    double PixelMs = 0.0);
