namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Harvested mode-start constraints from the early CSSTVDEM auto-start states.
/// MMSSTV does not allow every detected candidate to force-start from the same
/// path, so we keep those rules explicit instead of flattening everything into
/// a generic "start whatever matched" policy.
/// </summary>
internal static class MmsstvAutoStartResolver
{
    public static bool CanStartFromSyncInterval(SstvModeId modeId)
        => modeId is
            SstvModeId.Scottie1 or
            SstvModeId.Martin1 or
            SstvModeId.Martin2 or
            SstvModeId.Sc2_180;

    public static bool TryResolveSyncIntervalCandidate(int rawSyncStartValue, out SstvModeId modeId)
    {
        modeId = default;
        if (rawSyncStartValue <= 0)
        {
            return false;
        }

        var candidate = (SstvModeId)(rawSyncStartValue - 1);
        if (!Enum.IsDefined(candidate) || !CanStartFromSyncInterval(candidate))
        {
            return false;
        }

        modeId = candidate;
        return true;
    }
}
