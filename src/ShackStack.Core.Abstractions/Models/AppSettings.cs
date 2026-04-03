namespace ShackStack.Core.Abstractions.Models;

public sealed record AppSettings(
    StationSettings Station,
    RadioSettings Radio,
    AudioSettings Audio,
    InteropSettings Interop,
    UiSettings Ui)
{
    public static AppSettings Default => new(
        new StationSettings(string.Empty, string.Empty),
        new RadioSettings("direct", "auto", 115200, 0x94, 200),
        new AudioSettings(string.Empty, string.Empty, string.Empty, string.Empty, 48000, 2048, 75, 100, 0, false, 50),
        new InteropSettings(true, "127.0.0.1", 12345),
        new UiSettings("dark", 1920, 1080, "classic", true, true, 8, 92));
}
