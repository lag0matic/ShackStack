namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// The receive state machine in MMSSTV uses the sm* enum values as protocol
/// state. Keep those source values explicit so ports do not depend on the
/// display-oriented ShackStack enum order by accident.
/// </summary>
internal enum MmsstvModeCode
{
    SmR36 = 0,
    SmR72 = 1,
    SmAvt = 2,
    SmSct1 = 3,
    SmSct2 = 4,
    SmSctDx = 5,
    SmMrt1 = 6,
    SmMrt2 = 7,
    SmSc2_180 = 8,
    SmSc2_120 = 9,
    SmSc2_60 = 10,
    SmPd50 = 11,
    SmPd90 = 12,
    SmPd120 = 13,
    SmPd160 = 14,
    SmPd180 = 15,
    SmPd240 = 16,
    SmPd290 = 17,
    SmP3 = 18,
    SmP5 = 19,
    SmP7 = 20,
    SmMr73 = 21,
    SmMr90 = 22,
    SmMr115 = 23,
    SmMr140 = 24,
    SmMr175 = 25,
    SmMp73 = 26,
    SmMp115 = 27,
    SmMp140 = 28,
    SmMp175 = 29,
    SmMl180 = 30,
    SmMl240 = 31,
    SmMl280 = 32,
    SmMl320 = 33,
    SmR24 = 34,
    SmRm8 = 35,
    SmRm12 = 36,
    SmMn73 = 37,
    SmMn110 = 38,
    SmMn140 = 39,
    SmMc110 = 40,
    SmMc140 = 41,
    SmMc180 = 42,
}

internal static class MmsstvModeMap
{
    private static readonly IReadOnlyDictionary<SstvModeId, MmsstvModeCode> ToSourceCode =
        new Dictionary<SstvModeId, MmsstvModeCode>
        {
            [SstvModeId.Robot36] = MmsstvModeCode.SmR36,
            [SstvModeId.Robot72] = MmsstvModeCode.SmR72,
            [SstvModeId.Avt90] = MmsstvModeCode.SmAvt,
            [SstvModeId.Scottie1] = MmsstvModeCode.SmSct1,
            [SstvModeId.Scottie2] = MmsstvModeCode.SmSct2,
            [SstvModeId.ScottieDx] = MmsstvModeCode.SmSctDx,
            [SstvModeId.Martin1] = MmsstvModeCode.SmMrt1,
            [SstvModeId.Martin2] = MmsstvModeCode.SmMrt2,
            [SstvModeId.Sc2_180] = MmsstvModeCode.SmSc2_180,
            [SstvModeId.Sc2_120] = MmsstvModeCode.SmSc2_120,
            [SstvModeId.Sc2_60] = MmsstvModeCode.SmSc2_60,
            [SstvModeId.Pd50] = MmsstvModeCode.SmPd50,
            [SstvModeId.Pd90] = MmsstvModeCode.SmPd90,
            [SstvModeId.Pd120] = MmsstvModeCode.SmPd120,
            [SstvModeId.Pd160] = MmsstvModeCode.SmPd160,
            [SstvModeId.Pd180] = MmsstvModeCode.SmPd180,
            [SstvModeId.Pd240] = MmsstvModeCode.SmPd240,
            [SstvModeId.Pd290] = MmsstvModeCode.SmPd290,
            [SstvModeId.Pasokon3] = MmsstvModeCode.SmP3,
            [SstvModeId.Pasokon5] = MmsstvModeCode.SmP5,
            [SstvModeId.Pasokon7] = MmsstvModeCode.SmP7,
            [SstvModeId.WraseMr73] = MmsstvModeCode.SmMr73,
            [SstvModeId.WraseMr90] = MmsstvModeCode.SmMr90,
            [SstvModeId.WraseMr115] = MmsstvModeCode.SmMr115,
            [SstvModeId.WraseMr140] = MmsstvModeCode.SmMr140,
            [SstvModeId.WraseMr175] = MmsstvModeCode.SmMr175,
            [SstvModeId.WraseMp73] = MmsstvModeCode.SmMp73,
            [SstvModeId.WraseMp115] = MmsstvModeCode.SmMp115,
            [SstvModeId.WraseMp140] = MmsstvModeCode.SmMp140,
            [SstvModeId.WraseMp175] = MmsstvModeCode.SmMp175,
            [SstvModeId.WraseMl180] = MmsstvModeCode.SmMl180,
            [SstvModeId.WraseMl240] = MmsstvModeCode.SmMl240,
            [SstvModeId.WraseMl280] = MmsstvModeCode.SmMl280,
            [SstvModeId.WraseMl320] = MmsstvModeCode.SmMl320,
            [SstvModeId.Robot24] = MmsstvModeCode.SmR24,
            [SstvModeId.Bw8] = MmsstvModeCode.SmRm8,
            [SstvModeId.Bw12] = MmsstvModeCode.SmRm12,
            [SstvModeId.WraseMn73] = MmsstvModeCode.SmMn73,
            [SstvModeId.WraseMn110] = MmsstvModeCode.SmMn110,
            [SstvModeId.WraseMn140] = MmsstvModeCode.SmMn140,
            [SstvModeId.WraseMc110] = MmsstvModeCode.SmMc110,
            [SstvModeId.WraseMc140] = MmsstvModeCode.SmMc140,
            [SstvModeId.WraseMc180] = MmsstvModeCode.SmMc180,
        };

    private static readonly IReadOnlyDictionary<MmsstvModeCode, SstvModeId> ToModeId =
        ToSourceCode.ToDictionary(static pair => pair.Value, static pair => pair.Key);

    public static MmsstvModeCode ToMmsstvCode(SstvModeId modeId)
        => ToSourceCode[modeId];

    public static bool TryToModeId(MmsstvModeCode sourceCode, out SstvModeId modeId)
        => ToModeId.TryGetValue(sourceCode, out modeId);

    public static bool TryResolveSyncStartValue(int rawSyncStartValue, out SstvModeId modeId)
    {
        modeId = default;
        if (rawSyncStartValue <= 0)
        {
            return false;
        }

        var sourceCode = (MmsstvModeCode)(rawSyncStartValue - 1);
        return TryToModeId(sourceCode, out modeId);
    }
}
