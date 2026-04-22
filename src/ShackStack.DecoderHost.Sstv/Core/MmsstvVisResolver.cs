namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Source-faithful resolver for the raw VIS bytes used inside MMSSTV's
/// CSSTVDEM::Do state machine. This is distinct from the simplified profile
/// VisCode values because MMSSTV resolves on the parity-bearing byte values
/// gathered during bit decoding.
/// </summary>
internal static class MmsstvVisResolver
{
    public const int ExtendedVisMarker = 0x23;

    private static readonly IReadOnlyDictionary<int, SstvModeId> StandardMap =
        new Dictionary<int, SstvModeId>
        {
            [0x82] = SstvModeId.Bw8,
            [0x86] = SstvModeId.Bw12,
            [0x84] = SstvModeId.Robot24,
            [0x88] = SstvModeId.Robot36,
            [0x0C] = SstvModeId.Robot72,
            [0x44] = SstvModeId.Avt90,
            [0x3C] = SstvModeId.Scottie1,
            [0xB8] = SstvModeId.Scottie2,
            [0xCC] = SstvModeId.ScottieDx,
            [0xAC] = SstvModeId.Martin1,
            [0x28] = SstvModeId.Martin2,
            [0xB7] = SstvModeId.Sc2_180,
            [0x3F] = SstvModeId.Sc2_120,
            [0xBB] = SstvModeId.Sc2_60,
            [0xDD] = SstvModeId.Pd50,
            [0x63] = SstvModeId.Pd90,
            [0x5F] = SstvModeId.Pd120,
            [0xE2] = SstvModeId.Pd160,
            [0x60] = SstvModeId.Pd180,
            [0xE1] = SstvModeId.Pd240,
            [0xDE] = SstvModeId.Pd290,
            [0x71] = SstvModeId.Pasokon3,
            [0x72] = SstvModeId.Pasokon5,
            [0xF3] = SstvModeId.Pasokon7,
        };

    private static readonly IReadOnlyDictionary<int, SstvModeId> ExtendedMap =
        new Dictionary<int, SstvModeId>
        {
            [0x45] = SstvModeId.WraseMr73,
            [0x46] = SstvModeId.WraseMr90,
            [0x49] = SstvModeId.WraseMr115,
            [0x4A] = SstvModeId.WraseMr140,
            [0x4C] = SstvModeId.WraseMr175,
            [0x25] = SstvModeId.WraseMp73,
            [0x29] = SstvModeId.WraseMp115,
            [0x2A] = SstvModeId.WraseMp140,
            [0x2C] = SstvModeId.WraseMp175,
            [0x85] = SstvModeId.WraseMl180,
            [0x86] = SstvModeId.WraseMl240,
            [0x89] = SstvModeId.WraseMl280,
            [0x8A] = SstvModeId.WraseMl320,
        };

    public static bool TryResolve(int rawVisData, bool extended, out SstvModeId modeId)
    {
        var map = extended ? ExtendedMap : StandardMap;
        return map.TryGetValue(rawVisData, out modeId);
    }
}
