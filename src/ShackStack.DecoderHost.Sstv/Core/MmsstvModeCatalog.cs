namespace ShackStack.DecoderHost.Sstv.Core;

internal static class MmsstvModeCatalog
{
    private static readonly SstvModeId[] AutoDetectPriorityIds =
    [
        SstvModeId.Martin1,
        SstvModeId.Martin2,
        SstvModeId.Scottie1,
        SstvModeId.Scottie2,
        SstvModeId.ScottieDx,
        SstvModeId.Robot36,
        SstvModeId.Pd50,
        SstvModeId.Pd90,
        SstvModeId.Pd120,
        SstvModeId.Pd160,
        SstvModeId.Pd180,
        SstvModeId.Pd240,
        SstvModeId.Pd290,
        SstvModeId.Avt90,
    ];

    private static readonly IReadOnlyList<SstvModeProfile> ProfilesInternal =
    [
        new(SstvModeId.Robot36, "Robot 36", 0x08, 320, 240, 36_000.0 / 1000.0, false, true, true,
            "robot36", 9.0, 88.0, 4.5, 44.0, 1.5, 3.0),
        new(SstvModeId.Robot72, "Robot 72", 0x0C, 320, 240, 72_000.0 / 1000.0, false, true, true,
            "robot", 9.0, 138.0, 4.5, 69.0, 6.0, 6.0),
        new(SstvModeId.Avt90, "AVT 90", 0x10, 320, 240, 375.0, false, false, true,
            "avt", 0.0, 125.0, 0.0),
        new(SstvModeId.Scottie1, "Scottie 1", 0x3C, 320, 256, 428_220.0 / 1000.0, false, true, true,
            "scottie", 9.0, 138.24, 1.5),
        new(SstvModeId.Scottie2, "Scottie 2", 0x38, 320, 256, 277_692.0 / 1000.0, false, true, true,
            "scottie", 9.0, 88.064, 1.5),
        new(SstvModeId.ScottieDx, "Scottie DX", 0x4C, 320, 256, 1_050_300.0 / 1000.0, false, false, true,
            "scottie", 9.0, 345.6, 1.5),
        new(SstvModeId.Martin1, "Martin 1", 0x2C, 320, 256, 446_446.0 / 1000.0, false, true, true,
            "martin", 4.862, 146.432, 0.572),
        new(SstvModeId.Martin2, "Martin 2", 0x28, 320, 256, 226_798.0 / 1000.0, false, true, true,
            "martin", 4.862, 73.216, 0.572),
        new(SstvModeId.Sc2_180, "SC2 180", 0x37, 320, 256, 180_000.0 / 1000.0, false, false, true),
        new(SstvModeId.Sc2_120, "SC2 120", 0x36, 320, 256, 120_000.0 / 1000.0, false, false, true),
        new(SstvModeId.Sc2_60, "SC2 60", 0x3D, 160, 256, 60_000.0 / 1000.0, false, false, true),
        new(SstvModeId.Pd50, "PD 50", 0x5D, 320, 256, 388_160.0 / 1000.0, false, true, true,
            "pd", 20.0, 0.0, 0.0, 0.0, 2.08, 0.0, 0.286),
        new(SstvModeId.Pd90, "PD 90", 0x63, 320, 256, 703_040.0 / 1000.0, false, true, true,
            "pd", 20.0, 0.0, 0.0, 0.0, 2.08, 0.0, 0.532),
        new(SstvModeId.Pd120, "PD 120", 0x5F, 640, 496, 508_480.0 / 1000.0, false, true, true,
            "pd", 20.0, 0.0, 0.0, 0.0, 2.08, 0.0, 0.19),
        new(SstvModeId.Pd160, "PD 160", 0x62, 512, 400, 804_416.0 / 1000.0, false, true, true,
            "pd", 20.0, 0.0, 0.0, 0.0, 2.08, 0.0, 0.382),
        new(SstvModeId.Pd180, "PD 180", 0x60, 640, 496, 754_240.0 / 1000.0, false, true, true,
            "pd", 20.0, 0.0, 0.0, 0.0, 2.08, 0.0, 0.286),
        new(SstvModeId.Pd240, "PD 240", 0x61, 640, 496, 1_000_000.0 / 1000.0, false, true, true,
            "pd", 20.0, 0.0, 0.0, 0.0, 2.08, 0.0, 0.382),
        new(SstvModeId.Pd290, "PD 290", 0x5E, 800, 616, 937_280.0 / 1000.0, false, true, true,
            "pd", 20.0, 0.0, 0.0, 0.0, 2.08, 0.0, 0.286),
        new(SstvModeId.Pasokon3, "P3", 0x71, 640, 496, 203_328.0 / 1000.0, false, false, true),
        new(SstvModeId.Pasokon5, "P5", 0x72, 640, 496, 304_992.0 / 1000.0, false, false, true),
        new(SstvModeId.Pasokon7, "P7", 0x73, 640, 496, 406_656.0 / 1000.0, false, false, true),
        new(SstvModeId.WraseMr73, "MR 73", 0x80, 320, 256, 73_000.0 / 1000.0, false, false, true),
        new(SstvModeId.WraseMr90, "MR 90", 0x81, 320, 256, 90_000.0 / 1000.0, false, false, true),
        new(SstvModeId.WraseMr115, "MR 115", 0x82, 320, 256, 115_000.0 / 1000.0, false, false, true),
        new(SstvModeId.WraseMr140, "MR 140", 0x83, 320, 256, 140_000.0 / 1000.0, false, false, true),
        new(SstvModeId.WraseMr175, "MR 175", 0x84, 320, 256, 175_000.0 / 1000.0, false, false, true),
        new(SstvModeId.WraseMp73, "MP 73", 0x85, 320, 256, 73_000.0 / 1000.0, false, false, true),
        new(SstvModeId.WraseMp115, "MP 115", 0x86, 320, 256, 115_000.0 / 1000.0, false, false, true),
        new(SstvModeId.WraseMp140, "MP 140", 0x87, 320, 256, 140_000.0 / 1000.0, false, false, true),
        new(SstvModeId.WraseMp175, "MP 175", 0x88, 320, 256, 175_000.0 / 1000.0, false, false, true),
        new(SstvModeId.WraseMl180, "ML 180", 0x89, 320, 256, 180_000.0 / 1000.0, false, false, true),
        new(SstvModeId.WraseMl240, "ML 240", 0x8A, 320, 256, 240_000.0 / 1000.0, false, false, true),
        new(SstvModeId.WraseMl280, "ML 280", 0x8B, 320, 256, 280_000.0 / 1000.0, false, false, true),
        new(SstvModeId.WraseMl320, "ML 320", 0x8C, 320, 256, 320_000.0 / 1000.0, false, false, true),
        new(SstvModeId.Robot24, "Robot 24", 0x04, 320, 240, 24_000.0 / 1000.0, false, true, true,
            "robot", 6.0, 92.0, 2.0, 46.0, 4.0, 4.0),
        new(SstvModeId.Bw8, "B/W 8", 0x01, 160, 120, 8_000.0 / 1000.0, false, false, true),
        new(SstvModeId.Bw12, "B/W 12", 0x02, 160, 120, 12_000.0 / 1000.0, false, false, true),
        new(SstvModeId.WraseMn73, "MN 73", 0x8D, 320, 256, 73_000.0 / 1000.0, true, false, true),
        new(SstvModeId.WraseMn110, "MN 110", 0x8E, 320, 256, 110_000.0 / 1000.0, true, false, true),
        new(SstvModeId.WraseMn140, "MN 140", 0x8F, 320, 256, 140_000.0 / 1000.0, true, false, true),
        new(SstvModeId.WraseMc110, "MC 110", 0x90, 320, 256, 110_000.0 / 1000.0, true, false, true),
        new(SstvModeId.WraseMc140, "MC 140", 0x91, 320, 256, 140_000.0 / 1000.0, true, false, true),
        new(SstvModeId.WraseMc180, "MC 180", 0x92, 320, 256, 180_000.0 / 1000.0, true, false, true),
    ];

    private static readonly IReadOnlyDictionary<string, SstvModeProfile> ByNameInternal =
        ProfilesInternal.ToDictionary(static p => p.Name, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<int, SstvModeProfile> ByVisInternal =
        ProfilesInternal.ToDictionary(static p => p.VisCode);

    private static readonly IReadOnlyDictionary<int, SstvModeProfile> ByAutoDetectVisInternal =
        AutoDetectPriorityIds
            .Select(id => ProfilesInternal.First(p => p.Id == id))
            .Where(static p => p.DecodePlanned)
            .ToDictionary(static p => p.VisCode);

    private static readonly IReadOnlyDictionary<int, SstvModeProfile> ByPlannedVisInternal =
        ProfilesInternal
            .Where(static p => p.DecodePlanned)
            .ToDictionary(static p => p.VisCode);

    public static IReadOnlyList<SstvModeProfile> Profiles => ProfilesInternal;

    public static IReadOnlyList<SstvModeProfile> AutoDetectProfiles { get; } =
        AutoDetectPriorityIds
            .Select(id => ProfilesInternal.First(p => p.Id == id))
            .Where(p => p.DecodePlanned)
            .ToArray();

    public static IReadOnlyList<SstvModeProfile> AutoSyncProfiles { get; } =
        AutoDetectPriorityIds
            .Select(id => ProfilesInternal.First(p => p.Id == id))
            .Where(p => p.DecodePlanned && MmsstvAutoStartResolver.CanStartFromSyncInterval(p.Id))
            .ToArray();

    public static bool TryResolve(string? modeName, out SstvModeProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(modeName) && ByNameInternal.TryGetValue(modeName.Trim(), out profile!))
        {
            return true;
        }

        profile = ByNameInternal["Martin 1"];
        return false;
    }

    public static bool TryResolveVis(int visCode, out SstvModeProfile profile)
        => ByAutoDetectVisInternal.TryGetValue(visCode, out profile!);

    public static bool TryResolvePlannedVis(int visCode, out SstvModeProfile profile)
        => ByPlannedVisInternal.TryGetValue(visCode, out profile!);

    public static string DescribeSupportedModes()
    {
        var ready = ProfilesInternal
            .Where(static p => p.DecodePlanned)
            .Select(static p => p.Name);
        return string.Join(", ", ready);
    }
}
