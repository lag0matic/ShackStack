using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;
using ShackStack.Infrastructure.Radio.Civ;
using ShackStack.Infrastructure.Radio.Icom;

namespace ShackStack.Infrastructure.Radio;

public sealed class RadioService : IRadioService, IDisposable
{
    private const int MaxCwChunkLength = 30;
    private const int CwRetryLimit = 20;
    private readonly CivDispatcher _dispatcher = new();
    private readonly RadioStateStore _stateStore = new();
    private readonly SimpleSubject<WaterfallRow> _scopeRows = new();
    private readonly CivSession _session;
    private readonly IcomCivCommands _icomCommands;
    private readonly IDisposable _unsolicitedSubscription;
    private readonly IDisposable _streamSubscription;
    private readonly IcomScopeAssembler _scopeAssembler = new();
    private RadioConnectionOptions? _lastOptions;
    private CancellationTokenSource? _smeterCts;
    private Task? _smeterTask;

    public RadioService()
    {
        var connection = new CivConnection(_dispatcher);
        _session = new CivSession(_dispatcher, connection, _stateStore);
        _icomCommands = new IcomCivCommands(_session);
        _unsolicitedSubscription = _dispatcher.UnsolicitedFrames.Subscribe(new Observer<CivFrame>(HandleUnsolicitedFrame));
        _streamSubscription = _dispatcher.StreamFrames.Subscribe(new Observer<CivFrame>(HandleStreamFrame));
    }

    public IObservable<RadioState> StateStream => _session.StateStream;
    public IObservable<WaterfallRow> ScopeRowStream => _scopeRows;
    public RadioState CurrentState => _stateStore.Current;

    public async Task ConnectAsync(RadioConnectionOptions options, CancellationToken ct)
    {
        _lastOptions = options;
        await _session.ConnectAsync(options, ct).ConfigureAwait(false);

        try
        {
            await _icomCommands.EnableScopeOutputAsync((byte)options.RadioAddress, ct).ConfigureAwait(false);
            await SyncScopeStateAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Scope output is optional during the first pass. Do not block core CI-V control
            // if the radio rejects or delays the scope-enable commands.
        }

        await SyncStateAsync(ct).ConfigureAwait(false);
        _session.StartReconciliationLoop(SyncCurrentStateAsync, ct, TimeSpan.FromSeconds(2));
        StartSmeterLoop(ct);
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        await StopSmeterLoopAsync().ConfigureAwait(false);
        await _session.DisconnectAsync().ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { IsConnected = false, IsPttActive = false, Smeter = 0 });
    }

    public Task RefreshStateAsync(CancellationToken ct) => SyncStateAsync(ct);

    public async Task SetFrequencyAsync(long hz, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var current = _stateStore.Current;
        var radioAddress = (byte)options.RadioAddress;
        if (current.IsVfoBActive)
        {
            await _icomCommands.SelectVfoBAsync(radioAddress, ct).ConfigureAwait(false);
            await _icomCommands.SetFrequencyAsync(radioAddress, hz, ct).ConfigureAwait(false);
            _stateStore.Update(current with { FrequencyHz = hz, VfoBFrequencyHz = hz });
        }
        else
        {
            await _icomCommands.SelectVfoAAsync(radioAddress, ct).ConfigureAwait(false);
            await _icomCommands.SetFrequencyAsync(radioAddress, hz, ct).ConfigureAwait(false);
            _stateStore.Update(current with { FrequencyHz = hz, VfoAFrequencyHz = hz });
        }
    }

    public async Task SetVfoBFrequencyAsync(long hz, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var radioAddress = (byte)options.RadioAddress;
        await _icomCommands.SelectVfoBAsync(radioAddress, ct).ConfigureAwait(false);
        await _icomCommands.SetFrequencyAsync(radioAddress, hz, ct).ConfigureAwait(false);
        await _icomCommands.SelectVfoAAsync(radioAddress, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { VfoBFrequencyHz = hz });
    }

    public async Task SetSplitAsync(bool enabled, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        await _icomCommands.SetSplitAsync((byte)options.RadioAddress, enabled, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { IsSplitEnabled = enabled });
    }

    public async Task EqualizeVfosAsync(CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        await _icomCommands.EqualizeVfosAsync((byte)options.RadioAddress, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { VfoBFrequencyHz = current.VfoAFrequencyHz });
    }

    public async Task ToggleActiveVfoAsync(CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var radioAddress = (byte)options.RadioAddress;
        var current = _stateStore.Current;
        if (current.IsVfoBActive)
        {
            await _icomCommands.SelectVfoAAsync(radioAddress, ct).ConfigureAwait(false);
        }
        else
        {
            await _icomCommands.SelectVfoBAsync(radioAddress, ct).ConfigureAwait(false);
        }

        _stateStore.Update(current with
        {
            IsVfoBActive = !current.IsVfoBActive,
            FrequencyHz = !current.IsVfoBActive ? current.VfoBFrequencyHz : current.VfoAFrequencyHz,
        });
    }

    public async Task SetModeAsync(RadioMode mode, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var current = _stateStore.Current;
        var filterSlot = (byte)Math.Clamp(current.FilterSlot is >= 1 and <= 3 ? current.FilterSlot : 2, 1, 3);
        await _icomCommands.SetModeAsync((byte)options.RadioAddress, mode, filterSlot, ct).ConfigureAwait(false);
        _stateStore.Update(current with
        {
            Mode = mode,
            FilterSlot = filterSlot,
            FilterWidthHz = IcomModeCodec.DecodeFilterWidth(mode, filterSlot),
        });
    }

    public async Task SetFilterSlotAsync(int filterSlot, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var current = _stateStore.Current;
        var slot = (byte)Math.Clamp(filterSlot, 1, 3);
        await _icomCommands.SetModeAsync((byte)options.RadioAddress, current.Mode, slot, ct).ConfigureAwait(false);
        _stateStore.Update(current with
        {
            FilterSlot = slot,
            FilterWidthHz = IcomModeCodec.DecodeFilterWidth(current.Mode, slot),
        });
    }

    public async Task SetPttAsync(bool enabled, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        await _icomCommands.SetPttAsync((byte)options.RadioAddress, enabled, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { IsPttActive = enabled });
    }

    public Task SetCwKeyAsync(bool enabled, CancellationToken ct) =>
        _session.SetDtrAsync(enabled, ct);

    public async Task SendCwTextAsync(string text, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var normalized = IcomCivCommands.NormalizeCwText(text);
        if (normalized.Length == 0)
        {
            return;
        }

        var radioAddress = (byte)options.RadioAddress;
        CwTraceLog.Write($"SEND begin text=\"{normalized}\"");

        for (var index = 0; index < normalized.Length; index += MaxCwChunkLength)
        {
            ct.ThrowIfCancellationRequested();
            var length = Math.Min(MaxCwChunkLength, normalized.Length - index);
            var chunk = normalized.Substring(index, length);
            await SendCwChunkReliableAsync(radioAddress, chunk, ct).ConfigureAwait(false);
        }

        CwTraceLog.Write("SEND queued all chunks");
    }

    public async Task StopCwSendAsync(CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        CwTraceLog.Write("SEND stop requested");
        var response = await _icomCommands.StopCwSendAsync((byte)options.RadioAddress, ct).ConfigureAwait(false);
        CwTraceLog.Write($"SEND stop response={(response?.Kind.ToString() ?? "null")} cmd=0x{response?.Command:X2}");
    }

    public async Task SetCwPitchAsync(int pitchHz, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        await _icomCommands.SetCwPitchAsync((byte)options.RadioAddress, pitchHz, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { CwPitchHz = Math.Clamp(pitchHz, 300, 900) });
    }

    public async Task SetCwKeyerSpeedAsync(int wpm, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        await _icomCommands.SetCwKeySpeedAsync((byte)options.RadioAddress, wpm, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { CwKeyerWpm = Math.Clamp(wpm, 6, 48) });
    }

    public async Task SetVoiceMicGainAsync(int percent, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var radioAddress = (byte)options.RadioAddress;
        await _icomCommands.SetMicGainPercentAsync(radioAddress, percent, ct).ConfigureAwait(false);
        var actual = await _icomCommands.GetMicGainPercentAsync(radioAddress, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { VoiceMicGainPercent = actual });
    }

    public async Task SetVoiceCompressionAsync(int percent, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var radioAddress = (byte)options.RadioAddress;
        var clamped = Math.Clamp(percent, 0, 10);
        await _icomCommands.SetCompressionLevelAsync(radioAddress, clamped, ct).ConfigureAwait(false);
        await _icomCommands.SetCompressionEnabledAsync(radioAddress, clamped > 0, ct).ConfigureAwait(false);
        var actualLevel = await _icomCommands.GetCompressionLevelAsync(radioAddress, ct).ConfigureAwait(false);
        var enabled = await _icomCommands.GetCompressionEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { VoiceCompressionPercent = enabled ? actualLevel : 0 });
    }

    public async Task SetRfPowerAsync(int percent, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var radioAddress = (byte)options.RadioAddress;
        await _icomCommands.SetRfPowerPercentAsync(radioAddress, percent, ct).ConfigureAwait(false);
        var actual = await _icomCommands.GetRfPowerPercentAsync(radioAddress, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { VoiceRfPowerPercent = actual });
    }

    public async Task SetPreampAsync(int level, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        await _icomCommands.SetPreampAsync((byte)options.RadioAddress, level, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { PreampLevel = Math.Clamp(level, 0, 2) });
    }

    public async Task SetAttenuatorAsync(bool enabled, int db, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        await _icomCommands.SetAttenuatorAsync((byte)options.RadioAddress, enabled, db, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { AttenuatorDb = enabled ? db : 0 });
    }

    public async Task SetTunerEnabledAsync(bool enabled, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        await _icomCommands.SetTunerEnabledAsync((byte)options.RadioAddress, enabled, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { IsTunerEnabled = enabled });
    }

    public async Task SetNoiseBlankerAsync(bool enabled, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        await _icomCommands.SetNoiseBlankerEnabledAsync((byte)options.RadioAddress, enabled, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { IsNoiseBlankerEnabled = enabled });
    }

    public async Task SetNoiseReductionAsync(bool enabled, int level, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var radioAddress = (byte)options.RadioAddress;
        await _icomCommands.SetNoiseReductionLevelAsync(radioAddress, level, ct).ConfigureAwait(false);
        await _icomCommands.SetNoiseReductionEnabledAsync(radioAddress, enabled && level > 0, ct).ConfigureAwait(false);
        var actualLevel = await _icomCommands.GetNoiseReductionLevelAsync(radioAddress, ct).ConfigureAwait(false);
        var actualEnabled = await _icomCommands.GetNoiseReductionEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with
        {
            IsNoiseReductionEnabled = actualEnabled,
            NoiseReductionLevel = actualEnabled ? actualLevel : 0,
        });
    }

    public async Task SetAutoNotchAsync(bool enabled, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        await _icomCommands.SetAutoNotchEnabledAsync((byte)options.RadioAddress, enabled, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { IsAutoNotchEnabled = enabled });
    }

    public async Task SetManualNotchAsync(bool enabled, int width, int position, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var radioAddress = (byte)options.RadioAddress;
        var clampedWidth = Math.Clamp(width, 0, 2);
        var clampedPosition = Math.Clamp(position, 0, 255);
        await _icomCommands.SetManualNotchWidthAsync(radioAddress, clampedWidth, ct).ConfigureAwait(false);
        await _icomCommands.SetManualNotchPositionAsync(radioAddress, clampedPosition, ct).ConfigureAwait(false);
        await _icomCommands.SetManualNotchEnabledAsync(radioAddress, enabled, ct).ConfigureAwait(false);
        var actualWidth = await _icomCommands.GetManualNotchWidthAsync(radioAddress, ct).ConfigureAwait(false);
        var actualPosition = await _icomCommands.GetManualNotchPositionAsync(radioAddress, ct).ConfigureAwait(false);
        var actualEnabled = await _icomCommands.GetManualNotchEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with
        {
            IsManualNotchEnabled = actualEnabled,
            ManualNotchWidth = actualWidth,
            ManualNotchPosition = actualPosition,
        });
    }

    public async Task SetIpPlusAsync(bool enabled, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        await _icomCommands.SetIpPlusEnabledAsync((byte)options.RadioAddress, enabled, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { IsIpPlusEnabled = enabled });
    }

    public async Task SetFilterShapeSoftAsync(bool enabled, CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        await _icomCommands.SetFilterShapeSoftAsync((byte)options.RadioAddress, enabled, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { IsFilterShapeSoft = enabled });
    }

    public async Task RetuneAsync(CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        await _icomCommands.RetuneAsync((byte)options.RadioAddress, ct).ConfigureAwait(false);
    }

    private async Task SyncStateAsync(CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var radioAddress = (byte)options.RadioAddress;
        var activeFrequency = await _icomCommands.GetFrequencyAsync(radioAddress, ct).ConfigureAwait(false);
        await _icomCommands.SelectVfoAAsync(radioAddress, ct).ConfigureAwait(false);
        var vfoA = await _icomCommands.GetFrequencyAsync(radioAddress, ct).ConfigureAwait(false);
        await _icomCommands.SelectVfoBAsync(radioAddress, ct).ConfigureAwait(false);
        var vfoB = await _icomCommands.GetFrequencyAsync(radioAddress, ct).ConfigureAwait(false);
        var restoreVfoB = activeFrequency == vfoB
            ? true
            : activeFrequency == vfoA
                ? false
                : _stateStore.Current.IsVfoBActive;

        if (restoreVfoB)
        {
            await _icomCommands.SelectVfoBAsync(radioAddress, ct).ConfigureAwait(false);
        }
        else
        {
            await _icomCommands.SelectVfoAAsync(radioAddress, ct).ConfigureAwait(false);
        }
        var (mode, filterWidth, filterSlot) = await _icomCommands.GetModeAsync(radioAddress, ct).ConfigureAwait(false);
        var split = await _icomCommands.GetSplitAsync(radioAddress, ct).ConfigureAwait(false);
        var ptt = await _icomCommands.GetPttAsync(radioAddress, ct).ConfigureAwait(false);
        var smeter = await _icomCommands.GetSmeterAsync(radioAddress, ct).ConfigureAwait(false);
        var voiceMicGain = await _icomCommands.GetMicGainPercentAsync(radioAddress, ct).ConfigureAwait(false);
        var voiceCompression = await _icomCommands.GetCompressionLevelAsync(radioAddress, ct).ConfigureAwait(false);
        var compressionEnabled = await _icomCommands.GetCompressionEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var rfPower = await _icomCommands.GetRfPowerPercentAsync(radioAddress, ct).ConfigureAwait(false);
        var cwPitch = await _icomCommands.GetCwPitchAsync(radioAddress, ct).ConfigureAwait(false);
        var cwWpm = await _icomCommands.GetCwKeySpeedAsync(radioAddress, ct).ConfigureAwait(false);
        var preamp = await _icomCommands.GetPreampAsync(radioAddress, ct).ConfigureAwait(false);
        var attDb = await _icomCommands.GetAttenuatorDbAsync(radioAddress, ct).ConfigureAwait(false);
        var tuner = await _icomCommands.GetTunerEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var noiseBlanker = await _icomCommands.GetNoiseBlankerEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var noiseReductionEnabled = await _icomCommands.GetNoiseReductionEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var noiseReductionLevel = await _icomCommands.GetNoiseReductionLevelAsync(radioAddress, ct).ConfigureAwait(false);
        var autoNotchEnabled = await _icomCommands.GetAutoNotchEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var manualNotchEnabled = await _icomCommands.GetManualNotchEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var manualNotchWidth = await _icomCommands.GetManualNotchWidthAsync(radioAddress, ct).ConfigureAwait(false);
        var manualNotchPosition = await _icomCommands.GetManualNotchPositionAsync(radioAddress, ct).ConfigureAwait(false);
        var ipPlus = await _icomCommands.GetIpPlusEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var filterShapeSoft = await _icomCommands.GetFilterShapeSoftAsync(radioAddress, ct).ConfigureAwait(false);

        var current = _stateStore.Current;
        _stateStore.Update(current with
        {
            IsConnected = true,
            FrequencyHz = restoreVfoB ? vfoB : vfoA,
            VfoAFrequencyHz = vfoA,
            VfoBFrequencyHz = vfoB,
            Mode = mode,
            IsVfoBActive = restoreVfoB,
            IsSplitEnabled = split,
            FilterSlot = filterSlot,
            FilterWidthHz = filterWidth,
            IsPttActive = ptt,
            Smeter = smeter,
            PreampLevel = preamp,
            AttenuatorDb = attDb,
            IsTunerEnabled = tuner,
            IsNoiseBlankerEnabled = noiseBlanker,
            IsNoiseReductionEnabled = noiseReductionEnabled,
            NoiseReductionLevel = noiseReductionEnabled ? noiseReductionLevel : 0,
            IsAutoNotchEnabled = autoNotchEnabled,
            IsManualNotchEnabled = manualNotchEnabled,
            ManualNotchWidth = manualNotchWidth,
            ManualNotchPosition = manualNotchPosition,
            IsIpPlusEnabled = ipPlus,
            IsFilterShapeSoft = filterShapeSoft,
            VoiceMicGainPercent = voiceMicGain,
            VoiceCompressionPercent = compressionEnabled ? voiceCompression : 0,
            VoiceRfPowerPercent = rfPower,
            CwPitchHz = cwPitch,
            CwKeyerWpm = cwWpm,
        });
    }

    private async Task SyncCurrentStateAsync(CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var radioAddress = (byte)options.RadioAddress;
        var frequency = await _icomCommands.GetFrequencyAsync(radioAddress, ct).ConfigureAwait(false);
        var (mode, filterWidth, filterSlot) = await _icomCommands.GetModeAsync(radioAddress, ct).ConfigureAwait(false);
        var split = await _icomCommands.GetSplitAsync(radioAddress, ct).ConfigureAwait(false);
        var ptt = await _icomCommands.GetPttAsync(radioAddress, ct).ConfigureAwait(false);
        var smeter = await _icomCommands.GetSmeterAsync(radioAddress, ct).ConfigureAwait(false);
        var voiceMicGain = await _icomCommands.GetMicGainPercentAsync(radioAddress, ct).ConfigureAwait(false);
        var voiceCompression = await _icomCommands.GetCompressionLevelAsync(radioAddress, ct).ConfigureAwait(false);
        var compressionEnabled = await _icomCommands.GetCompressionEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var rfPower = await _icomCommands.GetRfPowerPercentAsync(radioAddress, ct).ConfigureAwait(false);
        var cwPitch = await _icomCommands.GetCwPitchAsync(radioAddress, ct).ConfigureAwait(false);
        var cwWpm = await _icomCommands.GetCwKeySpeedAsync(radioAddress, ct).ConfigureAwait(false);
        var preamp = await _icomCommands.GetPreampAsync(radioAddress, ct).ConfigureAwait(false);
        var attDb = await _icomCommands.GetAttenuatorDbAsync(radioAddress, ct).ConfigureAwait(false);
        var tuner = await _icomCommands.GetTunerEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var noiseBlanker = await _icomCommands.GetNoiseBlankerEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var noiseReductionEnabled = await _icomCommands.GetNoiseReductionEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var noiseReductionLevel = await _icomCommands.GetNoiseReductionLevelAsync(radioAddress, ct).ConfigureAwait(false);
        var autoNotchEnabled = await _icomCommands.GetAutoNotchEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var manualNotchEnabled = await _icomCommands.GetManualNotchEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var manualNotchWidth = await _icomCommands.GetManualNotchWidthAsync(radioAddress, ct).ConfigureAwait(false);
        var manualNotchPosition = await _icomCommands.GetManualNotchPositionAsync(radioAddress, ct).ConfigureAwait(false);
        var ipPlus = await _icomCommands.GetIpPlusEnabledAsync(radioAddress, ct).ConfigureAwait(false);
        var filterShapeSoft = await _icomCommands.GetFilterShapeSoftAsync(radioAddress, ct).ConfigureAwait(false);

        var current = _stateStore.Current;
        var inferredIsVfoBActive = current.IsVfoBActive;
        var activeVfoKnown = false;
        if (current.VfoAFrequencyHz > 0 || current.VfoBFrequencyHz > 0)
        {
            if (frequency == current.VfoAFrequencyHz && frequency != current.VfoBFrequencyHz)
            {
                inferredIsVfoBActive = false;
                activeVfoKnown = true;
            }
            else if (frequency == current.VfoBFrequencyHz && frequency != current.VfoAFrequencyHz)
            {
                inferredIsVfoBActive = true;
                activeVfoKnown = true;
            }
        }
        else
        {
            activeVfoKnown = true;
        }

        var nextVfoA = current.VfoAFrequencyHz;
        var nextVfoB = current.VfoBFrequencyHz;
        if (activeVfoKnown)
        {
            if (inferredIsVfoBActive)
            {
                nextVfoB = frequency;
                if (nextVfoA == 0)
                {
                    nextVfoA = frequency;
                }
            }
            else
            {
                nextVfoA = frequency;
                if (nextVfoB == 0)
                {
                    nextVfoB = frequency;
                }
            }
        }

        _stateStore.Update(current with
        {
            IsConnected = true,
            FrequencyHz = frequency,
            VfoAFrequencyHz = nextVfoA,
            VfoBFrequencyHz = nextVfoB,
            IsVfoBActive = inferredIsVfoBActive,
            Mode = mode,
            IsSplitEnabled = split,
            FilterSlot = filterSlot,
            FilterWidthHz = filterWidth,
            IsPttActive = ptt,
            Smeter = smeter,
            PreampLevel = preamp,
            AttenuatorDb = attDb,
            IsTunerEnabled = tuner,
            IsNoiseBlankerEnabled = noiseBlanker,
            IsNoiseReductionEnabled = noiseReductionEnabled,
            NoiseReductionLevel = noiseReductionEnabled ? noiseReductionLevel : 0,
            IsAutoNotchEnabled = autoNotchEnabled,
            IsManualNotchEnabled = manualNotchEnabled,
            ManualNotchWidth = manualNotchWidth,
            ManualNotchPosition = manualNotchPosition,
            IsIpPlusEnabled = ipPlus,
            IsFilterShapeSoft = filterShapeSoft,
            VoiceMicGainPercent = voiceMicGain,
            VoiceCompressionPercent = compressionEnabled ? voiceCompression : 0,
            VoiceRfPowerPercent = rfPower,
            CwPitchHz = cwPitch,
            CwKeyerWpm = cwWpm,
        });
    }

    private async Task SyncScopeStateAsync(CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var radioAddress = (byte)options.RadioAddress;

        try
        {
            // wfview maintains scope state as an active subsystem. The first low-risk
            // thing we can mirror is clearing hold and polling the core scope state
            // family so the radio stays in a coherent streaming mode.
            await _icomCommands.SetScopeHoldAsync(radioAddress, enabled: false, ct).ConfigureAwait(false);
        }
        catch
        {
            // Best effort only.
        }

        await TryQueryScopeStateAsync(() => _icomCommands.GetScopeModeAsync(radioAddress, ct)).ConfigureAwait(false);
        await TryQueryScopeStateAsync(() => _icomCommands.GetScopeSpanAsync(radioAddress, ct)).ConfigureAwait(false);
        await TryQueryScopeStateAsync(() => _icomCommands.GetScopeHoldAsync(radioAddress, ct)).ConfigureAwait(false);
        await TryQueryScopeStateAsync(() => _icomCommands.GetScopeRefAsync(radioAddress, ct)).ConfigureAwait(false);
        await TryQueryScopeStateAsync(() => _icomCommands.GetScopeSpeedAsync(radioAddress, ct)).ConfigureAwait(false);
        await TryQueryScopeStateAsync(() => _icomCommands.GetScopeEdgeAsync(radioAddress, ct)).ConfigureAwait(false);
    }

    private static async Task TryQueryScopeStateAsync(Func<Task<CivFrame?>> query)
    {
        try
        {
            await query().ConfigureAwait(false);
        }
        catch
        {
            // Best effort only.
        }
    }

    private async Task SendCwChunkReliableAsync(byte radioAddress, string chunk, CancellationToken ct)
    {
        if (chunk.Length <= 1)
        {
            await SendSingleCwCharacterReliableAsync(radioAddress, chunk, ct).ConfigureAwait(false);
            return;
        }

        CwTraceLog.Write($"SEND chunk len={chunk.Length} text=\"{chunk}\"");
        var response = await _icomCommands.SendCwTextChunkAsync(radioAddress, chunk, ct).ConfigureAwait(false);
        CwTraceLog.Write($"SEND chunk response={(response?.Kind.ToString() ?? "null")} cmd=0x{response?.Command:X2}");

        if (response?.Kind == CivFrameKind.Acknowledge)
        {
            return;
        }

        CwTraceLog.Write("SEND chunk rejected; falling back to per-character queue");
        foreach (var ch in chunk)
        {
            ct.ThrowIfCancellationRequested();
            await SendSingleCwCharacterReliableAsync(radioAddress, ch.ToString(), ct).ConfigureAwait(false);
        }
    }

    private async Task SendSingleCwCharacterReliableAsync(byte radioAddress, string character, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= CwRetryLimit; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            CwTraceLog.Write($"SEND char attempt={attempt} text=\"{character}\"");

            var response = await _icomCommands.SendCwTextChunkAsync(radioAddress, character, ct).ConfigureAwait(false);
            CwTraceLog.Write($"SEND char response={(response?.Kind.ToString() ?? "null")} cmd=0x{response?.Command:X2}");

            if (response?.Kind == CivFrameKind.Acknowledge)
            {
                return;
            }

            await Task.Delay(10, ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"CW send rejected by radio for character '{character}'.");
    }

    private void StartSmeterLoop(CancellationToken ct)
    {
        _smeterCts?.Cancel();
        _smeterCts?.Dispose();
        _smeterCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _smeterTask = Task.Run(async () =>
        {
            try
            {
                while (!_smeterCts.IsCancellationRequested)
                {
                    await Task.Delay(100, _smeterCts.Token).ConfigureAwait(false);
                    await PollSmeterAsync(_smeterCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, _smeterCts.Token);
    }

    private async Task PollSmeterAsync(CancellationToken ct)
    {
        var options = _lastOptions;
        if (options is null)
        {
            return;
        }

        var smeter = await _icomCommands.GetSmeterAsync((byte)options.RadioAddress, ct).ConfigureAwait(false);
        var current = _stateStore.Current;
        _stateStore.Update(current with { IsConnected = true, Smeter = smeter });
    }

    private async Task StopSmeterLoopAsync()
    {
        if (_smeterCts is null)
        {
            return;
        }

        _smeterCts.Cancel();
        if (_smeterTask is not null)
        {
            try
            {
                await _smeterTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _smeterTask = null;
        _smeterCts.Dispose();
        _smeterCts = null;
    }

    private void HandleUnsolicitedFrame(CivFrame frame)
    {
        if (_lastOptions is null || frame.Source != (byte)_lastOptions.RadioAddress)
        {
            return;
        }

        if (frame.Command == IcomCivConstants.GetFrequency)
        {
            var hz = IcomFrequencyCodec.Decode(frame.Payload);
            var current = _stateStore.Current;
            var inferredIsVfoBActive = current.IsVfoBActive;
            var activeVfoKnown = false;
            if (current.VfoAFrequencyHz > 0 || current.VfoBFrequencyHz > 0)
            {
                if (hz == current.VfoAFrequencyHz && hz != current.VfoBFrequencyHz)
                {
                    inferredIsVfoBActive = false;
                    activeVfoKnown = true;
                }
                else if (hz == current.VfoBFrequencyHz && hz != current.VfoAFrequencyHz)
                {
                    inferredIsVfoBActive = true;
                    activeVfoKnown = true;
                }
            }
            else
            {
                activeVfoKnown = true;
            }

            var nextVfoA = current.VfoAFrequencyHz;
            var nextVfoB = current.VfoBFrequencyHz;
            if (activeVfoKnown)
            {
                if (inferredIsVfoBActive)
                {
                    nextVfoB = hz;
                    if (nextVfoA == 0)
                    {
                        nextVfoA = hz;
                    }
                }
                else
                {
                    nextVfoA = hz;
                    if (nextVfoB == 0)
                    {
                        nextVfoB = hz;
                    }
                }
            }

            _stateStore.Update(current with
            {
                IsConnected = true,
                FrequencyHz = hz,
                VfoAFrequencyHz = nextVfoA,
                VfoBFrequencyHz = nextVfoB,
                IsVfoBActive = inferredIsVfoBActive,
            });
            return;
        }

        if (frame.Command == IcomCivConstants.VfoCommand && frame.Payload.Length > 0)
        {
            var current = _stateStore.Current;
            var isVfoBActive = frame.Payload[0] == IcomCivConstants.VfoBSubcommand;
            if (frame.Payload[0] is IcomCivConstants.VfoASubcommand or IcomCivConstants.VfoBSubcommand)
            {
                _stateStore.Update(current with
                {
                    IsConnected = true,
                    IsVfoBActive = isVfoBActive,
                    FrequencyHz = isVfoBActive ? current.VfoBFrequencyHz : current.VfoAFrequencyHz,
                });
            }
            return;
        }

        if (frame.Command == IcomCivConstants.GetMode && frame.Payload.Length > 0)
        {
            var mode = IcomModeCodec.Decode(frame.Payload[0]);
            var filterSlot = frame.Payload.Length > 1 ? frame.Payload[1] : (byte)0x02;
            var current = _stateStore.Current;
            _stateStore.Update(current with
            {
                IsConnected = true,
                Mode = mode,
                FilterSlot = filterSlot,
                FilterWidthHz = IcomModeCodec.DecodeFilterWidth(mode, filterSlot),
            });
            return;
        }

        if (frame.Command == IcomCivConstants.SetControlState && frame.Payload.Length >= 2 && frame.Payload[0] == IcomCivConstants.PttSubcommand)
        {
            var current = _stateStore.Current;
            _stateStore.Update(current with { IsConnected = true, IsPttActive = frame.Payload[1] != 0x00 });
        }
    }

    private void HandleStreamFrame(CivFrame frame)
    {
        if (_lastOptions is null || frame.Source != (byte)_lastOptions.RadioAddress)
        {
            return;
        }

        if (frame.Command != IcomCivConstants.ScopeCommand)
        {
            return;
        }

        var row = _scopeAssembler.TryProcess(frame.Payload);
        if (row is not null)
        {
            _scopeRows.OnNext(row);
        }
    }

    public void Dispose()
    {
        _smeterCts?.Cancel();
        _unsolicitedSubscription.Dispose();
        _streamSubscription.Dispose();
    }
}
