namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Source-shaped receive states harvested from MMSSTV's CSSTVDEM::m_SyncMode.
/// The numeric values intentionally match the original code so later ports can
/// move logic over without inventing a new state vocabulary.
/// </summary>
internal enum MmsstvDemodSyncMode
{
    WaitingForSyncTrigger = 0,
    Confirm1200Continuation = 1,
    DecodeVis = 2,
    ApplyNextMode = 3,
    AvtWaitFor1900 = 4,
    AvtAttackConfirm = 5,
    AvtExtendedVis = 6,
    AvtPeriodWait = 7,
    AvtRestart = 8,
    DecodeExtendedVis = 9,
    ForcedStart = 256,
    StopWaitPrime = 512,
    StopWaitCountdown = 513,
}
