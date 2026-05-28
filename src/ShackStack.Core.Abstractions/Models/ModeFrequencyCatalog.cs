namespace ShackStack.Core.Abstractions.Models;

public static class ModeFrequencyCatalog
{
    public const string Rtty = "RTTY";
    public const string Keyboard = "Keyboard";
    public const string Freedv = "FreeDV";
    public const string Sstv = "SSTV";
    public const string Wefax = "WeFAX";

    public static readonly IReadOnlyList<ModeFrequencyPreset> Presets =
    [
        new(Rtty, "80m RTTY 3.580 MHz LSB-D", 3_580_000),
        new(Rtty, "80m RTTY/Data DX 3.590 MHz LSB-D", 3_590_000),
        new(Rtty, "40m RTTY/Data DX 7.040 MHz LSB-D", 7_040_000),
        new(Rtty, "40m RTTY 7.080 MHz LSB-D", 7_080_000),
        new(Rtty, "30m RTTY 10.140 MHz USB-D", 10_140_000),
        new(Rtty, "20m RTTY 14.080 MHz USB-D", 14_080_000),
        new(Rtty, "20m RTTY alt 14.083 MHz USB-D", 14_083_000),
        new(Rtty, "20m RTTY contest 14.095 MHz USB-D", 14_095_000),
        new(Rtty, "17m RTTY 18.102 MHz USB-D", 18_102_000),
        new(Rtty, "15m RTTY 21.080 MHz USB-D", 21_080_000),
        new(Rtty, "12m RTTY 24.920 MHz USB-D", 24_920_000),
        new(Rtty, "10m RTTY 28.080 MHz USB-D", 28_080_000),

        new(Keyboard, "160m PSK31 1.838 MHz USB-D", 1_838_000),
        new(Keyboard, "80m PSK31 3.580 MHz LSB-D", 3_580_000),
        new(Keyboard, "40m PSK31 7.070 MHz LSB-D", 7_070_000),
        new(Keyboard, "30m PSK31 10.140 MHz USB-D", 10_140_000),
        new(Keyboard, "20m PSK31 14.070 MHz USB-D", 14_070_000),
        new(Keyboard, "17m PSK31 18.100 MHz USB-D", 18_100_000),
        new(Keyboard, "15m PSK31 21.070 MHz USB-D", 21_070_000),
        new(Keyboard, "12m PSK31 24.920 MHz USB-D", 24_920_000),
        new(Keyboard, "10m PSK31 28.120 MHz USB-D", 28_120_000),

        new(Freedv, "160m FreeDV 1.870 MHz LSB-D", 1_870_000),
        new(Freedv, "80m FreeDV 3.625 MHz LSB-D", 3_625_000),
        new(Freedv, "80m FreeDV alt 3.643 MHz LSB-D", 3_643_000),
        new(Freedv, "80m FreeDV alt 3.693 MHz LSB-D", 3_693_000),
        new(Freedv, "80m FreeDV alt 3.697 MHz LSB-D", 3_697_000),
        new(Freedv, "80m FreeDV alt 3.803 MHz LSB-D", 3_803_000),
        new(Freedv, "60m FreeDV 5.4035 MHz USB-D", 5_403_500),
        new(Freedv, "60m FreeDV alt 5.3685 MHz USB-D", 5_368_500),
        new(Freedv, "40m FreeDV 7.177 MHz LSB-D", 7_177_000),
        new(Freedv, "40m FreeDV alt 7.197 MHz LSB-D", 7_197_000),
        new(Freedv, "20m FreeDV common 14.236 MHz USB-D", 14_236_000),
        new(Freedv, "20m FreeDV alt 14.240 MHz USB-D", 14_240_000),
        new(Freedv, "17m FreeDV 18.118 MHz USB-D", 18_118_000),
        new(Freedv, "15m FreeDV 21.313 MHz USB-D", 21_313_000),
        new(Freedv, "12m FreeDV 24.933 MHz USB-D", 24_933_000),
        new(Freedv, "10m FreeDV 28.330 MHz USB-D", 28_330_000),
        new(Freedv, "10m FreeDV alt 28.720 MHz USB-D", 28_720_000),

        new(Sstv, "80m SSTV 3.845 MHz LSB-D", 3_845_000),
        new(Sstv, "40m SSTV 7.171 MHz LSB-D", 7_171_000),
        new(Sstv, "20m SSTV 14.230 MHz USB-D", 14_230_000),
        new(Sstv, "20m SSTV alt 14.233 MHz USB-D", 14_233_000),
        new(Sstv, "15m SSTV 21.340 MHz USB-D", 21_340_000),
        new(Sstv, "10m SSTV 28.680 MHz USB-D", 28_680_000),

        new(Wefax, "NOAA Atlantic 4235.0 kHz USB-D", 4_235_000),
        new(Wefax, "NOAA Atlantic 6340.5 kHz USB-D", 6_340_500),
        new(Wefax, "NOAA Atlantic 9110.0 kHz USB-D", 9_110_000),
        new(Wefax, "NOAA Atlantic 12750.0 kHz USB-D", 12_750_000),
        new(Wefax, "NOAA Pacific 4346.0 kHz USB-D", 4_346_000),
        new(Wefax, "NOAA Pacific 8682.0 kHz USB-D", 8_682_000),
        new(Wefax, "NOAA Pacific 12750.0 kHz USB-D", 12_750_000),
        new(Wefax, "NOAA Pacific 12786.0 kHz USB-D", 12_786_000),
        new(Wefax, "NOAA Pacific 17151.2 kHz USB-D", 17_151_200),
        new(Wefax, "NOAA Pacific 22527.0 kHz USB-D", 22_527_000),
        new(Wefax, "NOAA Kodiak 2054.0 kHz USB-D", 2_054_000),
        new(Wefax, "NOAA Kodiak 4298.0 kHz USB-D", 4_298_000),
        new(Wefax, "NOAA Kodiak 8459.0 kHz USB-D", 8_459_000),
        new(Wefax, "NOAA Kodiak 12412.5 kHz USB-D", 12_412_500),
        new(Wefax, "NOAA Gulf 4317.9 kHz USB-D", 4_317_900),
        new(Wefax, "NOAA Gulf 8503.9 kHz USB-D", 8_503_900),
        new(Wefax, "NOAA Gulf 12789.9 kHz USB-D", 12_789_900),
        new(Wefax, "NOAA Gulf 17146.4 kHz USB-D", 17_146_400),
        new(Wefax, "NOAA Hawaii 9982.5 kHz USB-D", 9_982_500),
        new(Wefax, "NOAA Hawaii 11090.0 kHz USB-D", 11_090_000),
        new(Wefax, "NOAA Hawaii 16135.0 kHz USB-D", 16_135_000),
    ];

    public static IReadOnlyList<string> GetLabels(string modeGroup)
    {
        var labels = Presets
            .Where(preset => string.Equals(preset.ModeGroup, modeGroup, StringComparison.OrdinalIgnoreCase))
            .Select(preset => preset.DisplayLabel)
            .ToArray();

        return labels.Length > 0 ? labels : Presets.Select(preset => preset.DisplayLabel).Distinct().ToArray();
    }

    public static string GetDefaultLabel(string modeGroup, string? startsWith = "20m ")
    {
        var labels = GetLabels(modeGroup);
        if (!string.IsNullOrWhiteSpace(startsWith))
        {
            var match = labels.FirstOrDefault(label => label.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return labels.FirstOrDefault() ?? string.Empty;
    }
}
