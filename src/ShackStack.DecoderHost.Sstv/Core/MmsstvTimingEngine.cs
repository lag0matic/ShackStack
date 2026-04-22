namespace ShackStack.DecoderHost.Sstv.Core;

internal static class MmsstvTimingEngine
{
    public static int CalculateLineSamples(SstvModeProfile profile, int sampleRate)
    {
        var timingMs = profile.Family switch
        {
            "martin" when profile.ScanMs > 0.0
                => (profile.ScanMs * 3.0) + (profile.GapMs * 4.0) + profile.SyncMs,
            "scottie" or "rgb" when profile.ScanMs > 0.0
                => (profile.ScanMs * 3.0) + (profile.GapMs * 3.0) + profile.SyncMs,
            "avt" when profile.ScanMs > 0.0
                => profile.ScanMs * 3.0,
            "robot36"
                => profile.SyncMs + profile.SyncPorchMs + profile.ScanMs + profile.GapMs + profile.PorchMs + profile.AuxScanMs,
            "pd"
                => profile.SyncMs + profile.PorchMs + (profile.PixelMs * profile.Width * 4.0),
            _ => profile.TimingMs,
        };

        return Math.Max(1, (int)Math.Round(timingMs * sampleRate / 1000.0));
    }

    public static string Summarize(SstvModeProfile profile, int sampleRate)
    {
        var lineSamples = CalculateLineSamples(profile, sampleRate);
        var family = profile.Narrow ? "narrow" : "normal";
        return $"{profile.Name} | VIS 0x{profile.VisCode:X2} | {profile.Width}x{profile.Height} | {family} | line {lineSamples} samples";
    }
}
