namespace ShackStack.DecoderHost.Sstv.Core;

internal static class SstvWorkingConfig
{
    public const int DefaultWorkingSampleRate = 12_000;
    public const int MmsstvNativeSampleRate = 11_025;

    public static int WorkingSampleRate { get; } = ResolveWorkingSampleRate();

    private static int ResolveWorkingSampleRate()
    {
        var value = Environment.GetEnvironmentVariable("SHACKSTACK_SSTV_WORKING_SAMPLE_RATE");
        if (!int.TryParse(value, out var sampleRate))
        {
            return DefaultWorkingSampleRate;
        }

        return sampleRate is MmsstvNativeSampleRate or DefaultWorkingSampleRate
            ? sampleRate
            : DefaultWorkingSampleRate;
    }
}
