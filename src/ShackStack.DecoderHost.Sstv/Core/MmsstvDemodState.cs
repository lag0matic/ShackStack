namespace ShackStack.DecoderHost.Sstv.Core;

/// <summary>
/// Source-faithful landing zone for the mutable receive state embedded in
/// MMSSTV's CSSTVDEM. This is intentionally a state bag with MMSSTV naming so
/// later ports can translate logic directly instead of re-inventing a new
/// receive model.
/// </summary>
internal sealed class MmsstvDemodState
{
    public enum EarlySyncEvent
    {
        None,
        StartCatalogMode,
        LeadInTriggered,
        LeadInEnteredVis,
        LeadInLost,
        StopWaitComplete,
        DecodeVisPending,
        VisLostToneSeparation,
        VisEnterExtended,
        VisResolvedMode,
        VisResetToAutoStart,
        ApplyEnterForcedStart,
        ApplyEnterAvtWait,
        ApplyLostFollowup,
        ForcedStartReady,
        AvtEnterForcedStart,
        AvtEnterAttackConfirm,
        AvtEnterExtendedVis,
        AvtEnterPeriodWait,
        AvtRevertToWait
    }

    public enum ApplyNextModeOutcome
    {
        Pending,
        LostFollowup,
        EnterForcedStart,
        EnterAvtWait
    }

    public enum VisDecodeOutcome
    {
        Pending,
        LostToneSeparation,
        EnterExtendedVis,
        ResolvedMode,
        ResetToAutoStart
    }

    public enum AvtOutcome
    {
        Pending,
        EnterForcedStart,
        EnterAttackConfirm,
        EnterExtendedVis,
        EnterPeriodWait,
        RevertToAvtWait
    }

    public enum StopWaitOutcome
    {
        Pending,
        ResetToAutoStart
    }

    public readonly record struct EarlySyncResult(EarlySyncEvent Event, SstvModeId ModeId = default);

    public const int DemodBufferMax = 24;

    public int Skip { get; set; }
    public int Sync { get; set; }
    public int SyncRestart { get; set; }
    public MmsstvDemodSyncMode SyncMode { get; set; } = MmsstvDemodSyncMode.WaitingForSyncTrigger;
    public int SyncTime { get; set; }
    public int SyncAttackTime { get; set; }
    public int VisData { get; set; }
    public int VisCount { get; set; }
    public int VisTrigger { get; set; }
    public int SyncError { get; set; }
    public int NextMode { get; set; }
    public int SyncAvt { get; set; }

    public int WriteBase { get; set; }
    public int WritePage { get; set; }
    public int ReadPage { get; set; }
    public int WriteCount { get; set; }
    public int WriteLine { get; set; }
    public int WriteBegin { get; set; }
    public int ReadBase { get; set; }

    public int RequestSave { get; set; }
    public int LoopBack { get; set; }
    public int StageWritePage { get; set; }
    public int StageWriteLine { get; set; }
    public int Lost { get; set; }

    public int SenseLevel { get; set; }
    public int ScopeFlag { get; set; }
    public int Tick { get; set; }
    public int TickFrequency { get; set; }
    public int ManualSync { get; set; }
    public int AfcEnabled { get; set; } = 1;
    public int TuneFrequency { get; set; }
    public int Tune { get; set; }
    public int SyncSenseLevel { get; private set; }
    public int SyncSenseThreshold { get; private set; }
    public int SyncSenseThresholdHalf { get; private set; }
    public int SyncSenseThresholdNarrow { get; private set; }

    public MmsstvSyncInterval SyncInterval1 { get; }
    public MmsstvSyncInterval SyncInterval2 { get; }
    public MmsstvSyncInterval SyncInterval3 { get; }
    public MmsstvAfcTracker AfcTracker { get; } = new();
    public MmsstvSyncToneBank SyncToneBank { get; } = new();
    public MmsstvFskState Fsk { get; } = new();
    public MmsstvRepeaterState Repeater { get; } = new();

    public MmsstvDemodState(int sampleRate)
    {
        SyncInterval1 = new MmsstvSyncInterval(sampleRate);
        SyncInterval2 = new MmsstvSyncInterval(sampleRate);
        SyncInterval3 = new MmsstvSyncInterval(sampleRate);
        Reset();
    }

    public void Reset()
    {
        Skip = 0;
        Sync = 0;
        SyncRestart = 0;
        SyncMode = MmsstvDemodSyncMode.WaitingForSyncTrigger;
        SyncTime = 0;
        SyncAttackTime = 0;
        VisData = 0;
        VisCount = 0;
        VisTrigger = 0;
        SyncError = 0;
        NextMode = 0;
        SyncAvt = 0;
        WriteBase = 0;
        WritePage = 0;
        ReadPage = 0;
        WriteCount = 0;
        WriteLine = 0;
        WriteBegin = 0;
        ReadBase = 0;
        RequestSave = 0;
        LoopBack = 0;
        StageWritePage = 0;
        StageWriteLine = 0;
        Lost = 0;
        SenseLevel = 0;
        ScopeFlag = 0;
        Tick = 0;
        TickFrequency = 0;
        ManualSync = 0;
        AfcEnabled = 1;
        TuneFrequency = 0;
        Tune = 0;
        SetSenseLevel(1);

        SyncInterval1.Reset();
        SyncInterval2.Reset();
        SyncInterval3.Reset();
        AfcTracker.Reset();
        SyncToneBank.InitTone(0);
        Fsk.Reset();
        Repeater.Reset();
    }

    public void SetSenseLevel(int senseLevel)
    {
        SyncSenseLevel = senseLevel;
        switch (senseLevel)
        {
            case 1:
                SyncSenseThreshold = 3500;
                SyncSenseThresholdHalf = SyncSenseThreshold / 2;
                SyncSenseThresholdNarrow = 5700;
                break;
            case 2:
                SyncSenseThreshold = 4800;
                SyncSenseThresholdHalf = SyncSenseThreshold / 2;
                SyncSenseThresholdNarrow = 6800;
                break;
            case 3:
                SyncSenseThreshold = 6000;
                SyncSenseThresholdHalf = SyncSenseThreshold / 2;
                SyncSenseThresholdNarrow = 8000;
                break;
            default:
                SyncSenseThreshold = 2400;
                SyncSenseThresholdHalf = SyncSenseThreshold / 2;
                SyncSenseThresholdNarrow = 5000;
                break;
        }
    }

    public void ResetForStart()
    {
        SyncInterval1.Reset();
        SyncInterval2.Reset();
        SyncInterval3.Reset();
        WriteBegin = 0;
        ReadBase = 0;
        SyncMode = MmsstvDemodSyncMode.WaitingForSyncTrigger;
        Sync = 0;
        Skip = 0;
        WritePage = 0;
        ReadPage = 0;
        WriteBase = 0;
        WriteLine = 0;
        WriteCount = 0;
        SyncRestart = 0;
        LoopBack = 0;
        Lost = 0;
    }

    public void Trigger1200LeadIn(int sampleRate)
    {
        SyncMode = MmsstvDemodSyncMode.Confirm1200Continuation;
        SyncTime = 15 * sampleRate / 1000;
    }

    public void Continue1200LeadIn(int sampleRate)
    {
        SyncTime--;
        if (SyncTime > 0)
        {
            return;
        }

        SyncMode = MmsstvDemodSyncMode.DecodeVis;
        SyncTime = 30 * sampleRate / 1000;
        VisData = 0;
        VisCount = 8;
    }

    public void BeginVisDecode(int sampleRate)
    {
        SyncMode = MmsstvDemodSyncMode.DecodeVis;
        SyncTime = 30 * sampleRate / 1000;
        VisData = 0;
        VisCount = 8;
    }

    public void BeginExtendedVisDecode(int sampleRate)
    {
        SyncMode = MmsstvDemodSyncMode.DecodeExtendedVis;
        SyncTime = 30 * sampleRate / 1000;
        VisData = 0;
        VisCount = 8;
    }

    public void ResetToAutoStart()
    {
        SyncMode = MmsstvDemodSyncMode.WaitingForSyncTrigger;
        SyncTime = 0;
        VisData = 0;
        VisCount = 0;
        NextMode = 0;
    }

    public void PrepareForModeStart(bool startImmediately)
    {
        SyncInterval1.Reset();
        SyncInterval2.Reset();
        SyncInterval3.Reset();
        WriteBegin = 0;
        ReadBase = 0;
        SyncMode = startImmediately
            ? MmsstvDemodSyncMode.WaitingForSyncTrigger
            : (MmsstvDemodSyncMode)(-1);
        Sync = 0;
    }

    public void EnterStartedMode()
    {
        SyncMode = (MmsstvDemodSyncMode)(-1);
        Sync = 0;
        Skip = 0;
        WritePage = 0;
        ReadPage = 0;
        WriteBase = 0;
        WriteLine = 0;
        WriteCount = 0;
        ReadBase = 0;
        WriteBegin = 2;
        Lost = 0;
        Sync = 1;
        SyncMode = MmsstvDemodSyncMode.WaitingForSyncTrigger;
    }

    public bool AdvanceVisBit(bool markBitHigh, int sampleRate)
    {
        SyncTime = 30 * sampleRate / 1000;
        VisData >>= 1;
        if (markBitHigh)
        {
            VisData |= 0x80;
        }

        VisCount--;
        return VisCount <= 0;
    }

    public VisDecodeOutcome AdvanceVisDecodeWindow(
        double tone1080,
        double tone1300,
        double tone1900,
        int syncThresholdHalf,
        int sampleRate,
        int consumedSamples,
        out SstvModeId resolvedMode,
        out bool completedBit)
    {
        resolvedMode = default;
        completedBit = false;

        SyncTime -= consumedSamples;
        if (SyncTime > 0)
        {
            return VisDecodeOutcome.Pending;
        }

        if (((tone1080 < tone1900) && (tone1300 < tone1900)) ||
            (Math.Abs(tone1080 - tone1300) < syncThresholdHalf))
        {
            ResetToAutoStart();
            return VisDecodeOutcome.LostToneSeparation;
        }

        completedBit = AdvanceVisBit(tone1080 > tone1300, sampleRate);
        if (!completedBit)
        {
            return VisDecodeOutcome.Pending;
        }

        if (ResolveCompletedVis(out resolvedMode, out var enterExtendedVis))
        {
            return VisDecodeOutcome.ResolvedMode;
        }

        return enterExtendedVis
            ? VisDecodeOutcome.EnterExtendedVis
            : VisDecodeOutcome.ResetToAutoStart;
    }

    public bool ResolveCompletedVis(out SstvModeId modeId, out bool enterExtendedVis)
    {
        enterExtendedVis = false;

        if (SyncMode == MmsstvDemodSyncMode.DecodeVis)
        {
            if (VisData == MmsstvVisResolver.ExtendedVisMarker)
            {
                SyncMode = MmsstvDemodSyncMode.DecodeExtendedVis;
                VisData = 0;
                VisCount = 8;
                modeId = default;
                enterExtendedVis = true;
                return false;
            }

            if (MmsstvVisResolver.TryResolve(VisData, extended: false, out modeId))
            {
                NextMode = (int)modeId;
                SyncMode = MmsstvDemodSyncMode.ApplyNextMode;
                return true;
            }
        }
        else if (SyncMode == MmsstvDemodSyncMode.DecodeExtendedVis &&
                 MmsstvVisResolver.TryResolve(VisData, extended: true, out modeId))
        {
            NextMode = (int)modeId;
            SyncMode = MmsstvDemodSyncMode.ApplyNextMode;
            return true;
        }

        modeId = default;
        ResetToAutoStart();
        return false;
    }

    public bool TryResolveSyncIntervalAutoStart(int rawSyncStartValue, out SstvModeId modeId)
    {
        if (MmsstvAutoStartResolver.TryResolveSyncIntervalCandidate(rawSyncStartValue, out modeId))
        {
            NextMode = (int)modeId;
            return true;
        }

        modeId = default;
        return false;
    }

    public bool TryResolveWideIntervalStart(out SstvModeId modeId)
    {
        var rawSyncStartValue = SyncInterval1.SyncStart();
        if (rawSyncStartValue > 0 && TryResolveSyncIntervalAutoStart(rawSyncStartValue - 1, out modeId))
        {
            return true;
        }

        modeId = default;
        return false;
    }

    public bool TryResolveLimitedIntervalStart(out SstvModeId modeId)
    {
        var rawSyncStartValue = SyncInterval2.SyncStart();
        if (rawSyncStartValue > 0 && TryResolveSyncIntervalAutoStart(rawSyncStartValue - 1, out modeId))
        {
            return true;
        }

        modeId = default;
        return false;
    }

    public bool TryResolveNarrowIntervalStart(out SstvModeId modeId)
    {
        var rawSyncStartValue = SyncInterval3.SyncStart();
        if (rawSyncStartValue > 0 && TryResolveSyncIntervalAutoStart(rawSyncStartValue - 1, out modeId))
        {
            return true;
        }

        modeId = default;
        return false;
    }

    public bool AdvanceNarrow1900PhaseChain(
        double tone1200,
        double tone1300,
        double tone1900,
        out SstvModeId narrowMode)
    {
        narrowMode = default;
        var tone1900Dominant =
            tone1900 > tone1200 &&
            tone1900 > tone1300 &&
            tone1900 > SyncSenseThresholdNarrow &&
            (tone1900 - tone1200) >= SyncSenseThresholdNarrow;

        if (tone1900Dominant)
        {
            if (SyncInterval3.SyncPhase > 0)
            {
                SyncInterval3.SyncMax((int)Math.Round(tone1900));
            }
            else
            {
                SyncInterval3.SyncTrig((int)Math.Round(tone1900));
                SyncInterval3.BeginSyncPhase();
            }

            return false;
        }

        if (SyncInterval3.SyncPhase <= 0)
        {
            return false;
        }

        SyncInterval3.ClearSyncPhase();
        return TryResolveNarrowIntervalStart(out narrowMode);
    }

    public bool AdvancePreVis1200Chain(
        double tone1200,
        double tone1900,
        int sampleRate,
        out SstvModeId autoStartMode,
        out bool triggeredLeadIn,
        out bool enteredVisDecode,
        out bool lostLeadIn)
    {
        autoStartMode = default;
        triggeredLeadIn = false;
        enteredVisDecode = false;
        lostLeadIn = false;

        var tone1200Dominant =
            tone1200 > tone1900 &&
            tone1200 > SyncSenseThreshold &&
            (tone1200 - tone1900) >= SyncSenseThreshold;
        var tone1200LeadCandidate =
            tone1200 > tone1900 &&
            tone1200 > SyncSenseThresholdHalf &&
            (tone1200 - tone1900) >= SyncSenseThresholdHalf;

        switch (SyncMode)
        {
            case MmsstvDemodSyncMode.WaitingForSyncTrigger:
                if (TryResolveWideIntervalStart(out autoStartMode))
                {
                    return true;
                }

                if (tone1200LeadCandidate)
                {
                    SyncInterval2.SyncMax((int)Math.Round(tone1200));
                }
                else if (TryResolveLimitedIntervalStart(out autoStartMode))
                {
                    return true;
                }

                if (tone1200Dominant)
                {
                    Trigger1200LeadIn(sampleRate);
                    SyncInterval1.SyncTrig((int)Math.Round(tone1200));
                    triggeredLeadIn = true;
                }

                return true;

            case MmsstvDemodSyncMode.Confirm1200Continuation:
                if (tone1200LeadCandidate)
                {
                    SyncInterval2.SyncMax((int)Math.Round(tone1200));
                }

                if (tone1200Dominant)
                {
                    SyncInterval1.SyncMax((int)Math.Round(tone1200));
                    Continue1200LeadIn(sampleRate);
                    enteredVisDecode = SyncMode == MmsstvDemodSyncMode.DecodeVis;
                }
                else
                {
                    ResetToAutoStart();
                    lostLeadIn = true;
                }

                return true;

            default:
                return false;
        }
    }

    public void ResolveApplyNextMode(bool has1200Sync, bool shouldRequestSave, int sampleRate)
    {
        if (!has1200Sync)
        {
            ResetToAutoStart();
            return;
        }

        if (shouldRequestSave)
        {
            RequestSave = 1;
        }

        SyncMode = MmsstvDemodSyncMode.ForcedStart;
        if ((SstvModeId)NextMode == SstvModeId.Avt90)
        {
            SyncTime = (int)Math.Round((9 + 910 + 910 + 5311.9424 + 0.30514375) * sampleRate / 1000.0);
            SyncMode = MmsstvDemodSyncMode.AvtWaitFor1900;
            SyncAvt = 1;
            Sync = 0;
        }
    }

    public void BeginApplyNextMode(int sampleRate)
    {
        SyncMode = MmsstvDemodSyncMode.ApplyNextMode;
        SyncTime = 30 * sampleRate / 1000;
    }

    public bool AdvanceApplyNextMode(bool has1200Sync, bool shouldRequestSave, int sampleRate, int consumedSamples)
    {
        if (SyncMode != MmsstvDemodSyncMode.ApplyNextMode)
        {
            return false;
        }

        SyncTime -= consumedSamples;
        if (SyncTime > 0)
        {
            return false;
        }

        ResolveApplyNextMode(has1200Sync, shouldRequestSave, sampleRate);
        return true;
    }

    public ApplyNextModeOutcome AdvanceApplyNextModeStep(
        bool has1200Sync,
        bool shouldRequestSave,
        int sampleRate,
        int consumedSamples)
    {
        var completed = AdvanceApplyNextMode(has1200Sync, shouldRequestSave, sampleRate, consumedSamples);
        if (!completed)
        {
            return ApplyNextModeOutcome.Pending;
        }

        return SyncMode switch
        {
            MmsstvDemodSyncMode.ForcedStart => ApplyNextModeOutcome.EnterForcedStart,
            MmsstvDemodSyncMode.AvtWaitFor1900 => ApplyNextModeOutcome.EnterAvtWait,
            _ => ApplyNextModeOutcome.LostFollowup
        };
    }

    public AvtOutcome AdvanceAvtWaitFor1900(double rawPllValue, int sampleRate, int consumedSamples)
    {
        SyncTime -= consumedSamples;
        if (SyncTime <= 0)
        {
            SyncMode = MmsstvDemodSyncMode.ForcedStart;
            return AvtOutcome.EnterForcedStart;
        }

        if (rawPllValue >= -1000.0 && rawPllValue <= 1000.0)
        {
            SyncMode = MmsstvDemodSyncMode.AvtAttackConfirm;
            SyncAttackTime = (int)Math.Round(9.7646 * 0.5 * sampleRate / 1000.0);
            return AvtOutcome.EnterAttackConfirm;
        }

        return AvtOutcome.Pending;
    }

    public AvtOutcome AdvanceAvtAttackConfirm(double rawPllValue, int sampleRate, int consumedSamples)
    {
        SyncTime -= consumedSamples;
        if (SyncTime <= 0)
        {
            SyncMode = MmsstvDemodSyncMode.ForcedStart;
            return AvtOutcome.EnterForcedStart;
        }

        if (rawPllValue >= -800.0 && rawPllValue <= 800.0)
        {
            SyncAttackTime -= consumedSamples;
            if (SyncAttackTime <= 0)
            {
                SyncMode = MmsstvDemodSyncMode.AvtExtendedVis;
                SyncAttackTime = (int)Math.Round(9.7646 * sampleRate / 1000.0);
                VisData = 0;
                VisCount = 16;
                return AvtOutcome.EnterExtendedVis;
            }

            return AvtOutcome.Pending;
        }

        SyncMode = MmsstvDemodSyncMode.AvtWaitFor1900;
        return AvtOutcome.RevertToAvtWait;
    }

    public AvtOutcome AdvanceAvtExtendedVis(double rawPllValue, int sampleRate, int consumedSamples)
    {
        SyncTime -= consumedSamples;
        if (SyncTime <= 0)
        {
            SyncMode = MmsstvDemodSyncMode.ForcedStart;
            return AvtOutcome.EnterForcedStart;
        }

        SyncAttackTime -= consumedSamples;
        if (SyncAttackTime > 0)
        {
            return AvtOutcome.Pending;
        }

        if (rawPllValue >= 8000.0 || rawPllValue <= -8000.0)
        {
            SyncAttackTime = (int)Math.Round(9.7646 * sampleRate / 1000.0);
            VisData <<= 1;
            if (rawPllValue > 0)
            {
                VisData |= 0x0001;
            }

            VisCount--;
            if (VisCount > 0)
            {
                return AvtOutcome.Pending;
            }

            var l = VisData & 0x00ff;
            var h = (VisData >> 8) & 0x00ff;
            if ((l + h) == 0x00ff && l >= 0xa0 && l <= 0xbf && h >= 0x40 && h <= 0x5f)
            {
                if (h != 0x40)
                {
                    SyncAttackTime = (int)Math.Round(9.7646 * 0.7 * sampleRate / 1000.0);
                    SyncTime = (int)Math.Round((((double)(h - 0x40) * 165.9982) - 0.8) * sampleRate / 1000.0);
                    SyncMode = MmsstvDemodSyncMode.AvtPeriodWait;
                    return AvtOutcome.EnterPeriodWait;
                }

                if (SyncTime <= 0 || SyncTime >= (int)Math.Round(9.7646 * sampleRate / 1000.0))
                {
                    SyncTime = (int)Math.Round(((9.7646 * 0.5) - 0.8) * sampleRate / 1000.0);
                }

                SyncMode = MmsstvDemodSyncMode.AvtRestart;
                return AvtOutcome.EnterPeriodWait;
            }
        }

        SyncMode = MmsstvDemodSyncMode.AvtWaitFor1900;
        return AvtOutcome.RevertToAvtWait;
    }

    public AvtOutcome AdvanceAvtPeriodWait(double rawPllValue, int sampleRate, int consumedSamples)
    {
        if (SyncMode == MmsstvDemodSyncMode.AvtPeriodWait)
        {
            if (rawPllValue >= -1000.0 && rawPllValue <= 1000.0)
            {
                SyncMode = MmsstvDemodSyncMode.AvtAttackConfirm;
                SyncAttackTime = (int)Math.Round(9.7646 * 0.5 * sampleRate / 1000.0);
                return AvtOutcome.EnterAttackConfirm;
            }

            SyncAttackTime -= consumedSamples;
            if (SyncAttackTime <= 0)
            {
                SyncMode = MmsstvDemodSyncMode.AvtWaitFor1900;
                return AvtOutcome.RevertToAvtWait;
            }

            return AvtOutcome.Pending;
        }

        SyncMode = (MmsstvDemodSyncMode)((int)SyncMode - 1);
        if ((int)SyncMode <= 0)
        {
            SyncMode = MmsstvDemodSyncMode.ForcedStart;
            return AvtOutcome.EnterForcedStart;
        }

        return AvtOutcome.Pending;
    }

    public void ResetForStop()
    {
        SyncInterval1.Reset();
        SyncInterval2.Reset();
        SyncInterval3.Reset();
        WriteBegin = 0;
        SyncMode = MmsstvDemodSyncMode.StopWaitPrime;
        Sync = 0;
        SyncAvt = 0;
        Skip = 0;
    }

    public bool ShouldAdvanceSyncIntervals()
        => Sync == 0 || SyncRestart != 0 || SyncAvt != 0;

    public void PrimeStopWait(int sampleRate)
    {
        SyncTime = sampleRate / 2;
        SyncMode = MmsstvDemodSyncMode.StopWaitCountdown;
    }

    public StopWaitOutcome AdvanceStopWaitChain(int sampleRate, int consumedSamples)
    {
        switch (SyncMode)
        {
            case MmsstvDemodSyncMode.StopWaitPrime:
                PrimeStopWait(sampleRate);
                return StopWaitOutcome.Pending;

            case MmsstvDemodSyncMode.StopWaitCountdown:
                SyncTime -= consumedSamples;
                if (SyncTime <= 0)
                {
                    ResetToAutoStart();
                    return StopWaitOutcome.ResetToAutoStart;
                }

                return StopWaitOutcome.Pending;

            default:
                return StopWaitOutcome.Pending;
        }
    }

    public void IncrementWritePointer(int width)
    {
        WriteCount++;
        if (WriteCount < width)
        {
            return;
        }

        WriteCount = 0;
        WritePage++;
        WriteLine++;
        WriteBase += width;
        if (WritePage >= DemodBufferMax)
        {
            WritePage = 0;
            WriteBase = 0;
        }
    }

    public void CommitDecodedRow(int width)
    {
        for (var sample = 0; sample < width; sample++)
        {
            IncrementWritePointer(width);
        }
    }

    public void CommitDecodedRows(int width, int rowCount)
    {
        for (var row = 0; row < rowCount; row++)
        {
            CommitDecodedRow(width);
        }
    }

    public void AdvanceReadPointer(int width)
    {
        ReadPage++;
        ReadBase += width;
        if (ReadPage >= DemodBufferMax)
        {
            ReadPage = 0;
            ReadBase = 0;
        }
    }

    public void MarkStagedRow()
    {
        StageWritePage = WritePage;
        StageWriteLine++;
    }

    public EarlySyncResult AdvanceEarlySyncState(
        double tone1080,
        double tone1200,
        double tone1300,
        double tone1900,
        double rawPllValue,
        int sampleRate,
        int consumedSamples)
    {
        if (ShouldAdvanceSyncIntervals())
        {
            SyncInterval1.SyncInc(consumedSamples);
            SyncInterval2.SyncInc(consumedSamples);
            SyncInterval3.SyncInc(consumedSamples);
        }

        switch (SyncMode)
        {
            case MmsstvDemodSyncMode.WaitingForSyncTrigger:
                if (AdvanceNarrow1900PhaseChain(tone1200, tone1300, tone1900, out var narrowMode))
                {
                    return new(EarlySyncEvent.StartCatalogMode, narrowMode);
                }

                AdvancePreVis1200Chain(
                    tone1200,
                    tone1900,
                    sampleRate,
                    out var waitingAutoStartMode,
                    out var triggeredLeadIn,
                    out _,
                    out _);

                if (waitingAutoStartMode != default)
                {
                    return new(EarlySyncEvent.StartCatalogMode, waitingAutoStartMode);
                }

                if (triggeredLeadIn)
                {
                    return new(EarlySyncEvent.LeadInTriggered);
                }

                return new(EarlySyncEvent.None);

            case MmsstvDemodSyncMode.Confirm1200Continuation:
                AdvancePreVis1200Chain(
                    tone1200,
                    tone1900,
                    sampleRate,
                    out _,
                    out _,
                    out var enteredVisDecode,
                    out var lostLeadIn);

                if (enteredVisDecode)
                {
                    return new(EarlySyncEvent.LeadInEnteredVis);
                }

                if (lostLeadIn)
                {
                    return new(EarlySyncEvent.LeadInLost);
                }

                return new(EarlySyncEvent.None);

            case MmsstvDemodSyncMode.StopWaitPrime:
            case MmsstvDemodSyncMode.StopWaitCountdown:
                var stopWaitOutcome = AdvanceStopWaitChain(sampleRate, consumedSamples);
                return stopWaitOutcome == StopWaitOutcome.ResetToAutoStart
                    ? new(EarlySyncEvent.StopWaitComplete)
                    : new(EarlySyncEvent.None);

            case MmsstvDemodSyncMode.DecodeVis:
            case MmsstvDemodSyncMode.DecodeExtendedVis:
                var visOutcome = AdvanceVisDecodeWindow(
                    tone1080,
                    tone1300,
                    tone1900,
                    SyncSenseThresholdHalf,
                    sampleRate,
                    consumedSamples,
                    out var modeId,
                    out _);

                return visOutcome switch
                {
                    VisDecodeOutcome.Pending => new(EarlySyncEvent.DecodeVisPending),
                    VisDecodeOutcome.LostToneSeparation => new(EarlySyncEvent.VisLostToneSeparation),
                    VisDecodeOutcome.EnterExtendedVis => new(EarlySyncEvent.VisEnterExtended),
                    VisDecodeOutcome.ResolvedMode => new(EarlySyncEvent.VisResolvedMode, modeId),
                    _ => new(EarlySyncEvent.VisResetToAutoStart)
                };

            case MmsstvDemodSyncMode.ApplyNextMode:
                var applyOutcome = AdvanceApplyNextModeStep(
                    tone1200 > tone1900 && tone1200 > SyncSenseThreshold,
                    shouldRequestSave: false,
                    sampleRate,
                    consumedSamples);

                return applyOutcome switch
                {
                    ApplyNextModeOutcome.EnterForcedStart => new(EarlySyncEvent.ApplyEnterForcedStart),
                    ApplyNextModeOutcome.EnterAvtWait => new(EarlySyncEvent.ApplyEnterAvtWait),
                    ApplyNextModeOutcome.LostFollowup => new(EarlySyncEvent.ApplyLostFollowup),
                    _ => new(EarlySyncEvent.None)
                };

            case MmsstvDemodSyncMode.ForcedStart:
                return new(EarlySyncEvent.ForcedStartReady);

            case MmsstvDemodSyncMode.AvtWaitFor1900:
            case MmsstvDemodSyncMode.AvtAttackConfirm:
            case MmsstvDemodSyncMode.AvtExtendedVis:
            case MmsstvDemodSyncMode.AvtPeriodWait:
            case MmsstvDemodSyncMode.AvtRestart:
                var avtOutcome = SyncMode switch
                {
                    MmsstvDemodSyncMode.AvtWaitFor1900 => AdvanceAvtWaitFor1900(rawPllValue, sampleRate, consumedSamples),
                    MmsstvDemodSyncMode.AvtAttackConfirm => AdvanceAvtAttackConfirm(rawPllValue, sampleRate, consumedSamples),
                    MmsstvDemodSyncMode.AvtExtendedVis => AdvanceAvtExtendedVis(rawPllValue, sampleRate, consumedSamples),
                    _ => AdvanceAvtPeriodWait(rawPllValue, sampleRate, consumedSamples)
                };

                return avtOutcome switch
                {
                    AvtOutcome.EnterForcedStart => new(EarlySyncEvent.AvtEnterForcedStart),
                    AvtOutcome.EnterAttackConfirm => new(EarlySyncEvent.AvtEnterAttackConfirm),
                    AvtOutcome.EnterExtendedVis => new(EarlySyncEvent.AvtEnterExtendedVis),
                    AvtOutcome.EnterPeriodWait => new(EarlySyncEvent.AvtEnterPeriodWait),
                    AvtOutcome.RevertToAvtWait => new(EarlySyncEvent.AvtRevertToWait),
                    _ => new(EarlySyncEvent.None)
                };

            default:
                return new(EarlySyncEvent.None);
        }
    }
}
