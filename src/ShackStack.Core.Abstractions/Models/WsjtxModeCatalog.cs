namespace ShackStack.Core.Abstractions.Models;

public static class WsjtxModeCatalog
{
    public static readonly IReadOnlyList<WsjtxModeDefinition> Modes =
    [
        new("FT8", 15.0, true, true),
        new("FT4", 7.5, true, true),
        new("Q65", 15.0, true, false),
        new("FST4", 15.0, true, false),
        new("FST4W", 120.0, true, false),
        new("JT65", 60.0, true, false),
        new("JT9", 60.0, true, false),
        new("JT4", 60.0, true, false),
        new("WSPR", 120.0, true, false),
        new("MSK144", 15.0, true, false),
        new("JS8 Normal", 15.0, true, false),
        new("JS8 Fast", 10.0, true, false),
        new("JS8 Turbo", 6.0, true, false),
        new("JS8 Slow", 28.0, true, false),
    ];

    public static readonly IReadOnlyList<WsjtxFrequencyPreset> FrequencyPresets =
    [
        new("FT8", "160m FT8 1.840 MHz USB-D", 1_840_000),
        new("FT8", "80m FT8 3.573 MHz USB-D", 3_573_000),
        new("FT8", "40m FT8 7.074 MHz USB-D", 7_074_000),
        new("FT8", "30m FT8 10.136 MHz USB-D", 10_136_000),
        new("FT8", "20m FT8 14.074 MHz USB-D", 14_074_000),
        new("FT8", "17m FT8 18.100 MHz USB-D", 18_100_000),
        new("FT8", "15m FT8 21.074 MHz USB-D", 21_074_000),
        new("FT8", "12m FT8 24.915 MHz USB-D", 24_915_000),
        new("FT8", "10m FT8 28.074 MHz USB-D", 28_074_000),
        new("FT8", "6m FT8 50.313 MHz USB-D", 50_313_000),
        new("FT4", "80m FT4 3.575 MHz USB-D", 3_575_000),
        new("FT4", "40m FT4 7.0475 MHz USB-D", 7_047_500),
        new("FT4", "30m FT4 10.140 MHz USB-D", 10_140_000),
        new("FT4", "20m FT4 14.080 MHz USB-D", 14_080_000),
        new("FT4", "17m FT4 18.104 MHz USB-D", 18_104_000),
        new("FT4", "15m FT4 21.140 MHz USB-D", 21_140_000),
        new("FT4", "12m FT4 24.919 MHz USB-D", 24_919_000),
        new("FT4", "10m FT4 28.180 MHz USB-D", 28_180_000),
        new("Q65", "6m Q65 50.275 MHz USB-D", 50_275_000),
        new("FST4", "20m FST4 14.080 MHz USB-D", 14_080_000),
        new("FST4W", "20m FST4W 14.0956 MHz USB-D", 14_095_600),
        new("JT65", "20m JT65 14.076 MHz USB-D", 14_076_000),
        new("JT9", "20m JT9 14.078 MHz USB-D", 14_078_000),
        new("JT4", "20m JT4 14.076 MHz USB-D", 14_076_000),
        new("WSPR", "160m WSPR 1.8366 MHz USB-D", 1_836_600),
        new("WSPR", "80m WSPR 3.5686 MHz USB-D", 3_568_600),
        new("WSPR", "40m WSPR 7.0386 MHz USB-D", 7_038_600),
        new("WSPR", "30m WSPR 10.1387 MHz USB-D", 10_138_700),
        new("WSPR", "20m WSPR 14.0956 MHz USB-D", 14_095_600),
        new("WSPR", "17m WSPR 18.1046 MHz USB-D", 18_104_600),
        new("WSPR", "15m WSPR 21.0946 MHz USB-D", 21_094_600),
        new("WSPR", "12m WSPR 24.9246 MHz USB-D", 24_924_600),
        new("WSPR", "10m WSPR 28.1246 MHz USB-D", 28_124_600),
        new("WSPR", "6m WSPR 50.2930 MHz USB-D", 50_293_000),
        new("MSK144", "6m MSK144 50.280 MHz USB-D", 50_280_000),
        new("JS8", "160m JS8 1.842 MHz USB-D", 1_842_000),
        new("JS8", "80m JS8 3.578 MHz USB-D", 3_578_000),
        new("JS8", "40m JS8 7.078 MHz USB-D", 7_078_000),
        new("JS8", "30m JS8 10.130 MHz USB-D", 10_130_000),
        new("JS8", "20m JS8 14.078 MHz USB-D", 14_078_000),
        new("JS8", "17m JS8 18.104 MHz USB-D", 18_104_000),
        new("JS8", "15m JS8 21.078 MHz USB-D", 21_078_000),
        new("JS8", "12m JS8 24.922 MHz USB-D", 24_922_000),
        new("JS8", "10m JS8 28.078 MHz USB-D", 28_078_000),
        new("JS8", "6m JS8 50.318 MHz USB-D", 50_318_000),
    ];

    public static WsjtxModeDefinition GetMode(string label) =>
        Modes.FirstOrDefault(mode => string.Equals(mode.Label, label, StringComparison.OrdinalIgnoreCase))
        ?? Modes[0];

    public static IReadOnlyList<string> GetModeLabels() =>
        Modes.Select(mode => mode.Label).ToArray();

    public static IReadOnlyList<string> GetOperatorModeLabels() =>
        Modes
            .Where(mode =>
                string.Equals(mode.Label, "FT8", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode.Label, "FT4", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode.Label, "WSPR", StringComparison.OrdinalIgnoreCase))
            .Select(mode => mode.Label)
            .ToArray();

    public static IReadOnlyList<string> GetJs8ModeLabels() =>
        Modes
            .Where(mode => mode.Label.StartsWith("JS8 ", StringComparison.OrdinalIgnoreCase))
            .Select(mode => mode.Label)
            .ToArray();

    public static IReadOnlyList<string> GetFrequencyLabels(string modeLabel)
    {
        var presetModeLabel = modeLabel.StartsWith("JS8 ", StringComparison.OrdinalIgnoreCase)
            ? "JS8"
            : modeLabel;
        var labels = FrequencyPresets
            .Where(preset => string.Equals(preset.ModeLabel, presetModeLabel, StringComparison.OrdinalIgnoreCase))
            .Select(preset => preset.DisplayLabel)
            .ToArray();

        return labels.Length > 0 ? labels : FrequencyPresets.Select(preset => preset.DisplayLabel).Distinct().ToArray();
    }

    public static string GetDefaultFrequencyLabel(string modeLabel)
    {
        var labels = GetFrequencyLabels(modeLabel);
        return labels.FirstOrDefault(label => label.StartsWith("20m ", StringComparison.OrdinalIgnoreCase))
            ?? labels.FirstOrDefault()
            ?? "20m FT8 14.074 MHz USB-D";
    }
}
