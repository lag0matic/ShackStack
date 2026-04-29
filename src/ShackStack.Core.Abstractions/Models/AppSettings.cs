namespace ShackStack.Core.Abstractions.Models;

public sealed record AppSettings(
    StationSettings Station,
    RadioSettings Radio,
    AudioSettings Audio,
    InteropSettings Interop,
    UiSettings Ui,
    LongwaveSettings Longwave)
{
    public static AppSettings Default => new(
        new StationSettings(string.Empty, string.Empty),
        new RadioSettings("direct", "auto", 115200, 0x94, 200),
        new AudioSettings(string.Empty, string.Empty, string.Empty, string.Empty, 48000, 2048, 75, 100, 0, false, 50, string.Empty, 75),
        new InteropSettings(false, "127.0.0.1", 12345),
        new UiSettings("dark", 1920, 1080, "classic", true, false, 8, 92),
        new LongwaveSettings(false, string.Empty, string.Empty, "ShackStack Home", "LONGWAVE_KIND=standard;POTA_MODE=hunting"));
}
