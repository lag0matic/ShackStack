namespace ShackStack.DecoderHost.Sstv.Core;

internal static class MmsstvTimingEngine
{
    public static int CalculateLineSamples(SstvModeProfile profile, int sampleRate)
        => MmsstvPictureGeometry.CalculateLineSamples(profile, sampleRate);

    public static string Summarize(SstvModeProfile profile, int sampleRate)
    {
        var lineSamples = CalculateLineSamples(profile, sampleRate);
        var family = profile.Narrow ? "narrow" : "normal";
        return $"{profile.Name} | VIS 0x{profile.VisCode:X2} | {profile.Width}x{profile.Height} | {family} | line {lineSamples} samples";
    }
}
