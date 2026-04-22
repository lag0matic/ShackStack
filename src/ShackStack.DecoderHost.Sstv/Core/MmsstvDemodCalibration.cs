namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested from MMSSTV's default demodulation color settings:
/// DemOff = 0, DemWhite = 128/16384, DemBlack = 128/16384.
/// This gives the native path an explicit source-shaped calibration object
/// instead of burying those defaults as unrelated constants.
/// </summary>
internal sealed record MmsstvDemodCalibration(
    double Offset,
    double WhiteGain,
    double BlackGain)
{
    public static MmsstvDemodCalibration Default { get; } =
        new(
            Offset: 0.0,
            WhiteGain: 128.0,
            BlackGain: 128.0);
}
