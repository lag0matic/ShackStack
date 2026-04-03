using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IRadioService
{
    RadioState CurrentState { get; }
    Task ConnectAsync(RadioConnectionOptions options, CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
    Task RefreshStateAsync(CancellationToken ct);
    Task SetFrequencyAsync(long hz, CancellationToken ct);
    Task SetVfoBFrequencyAsync(long hz, CancellationToken ct);
    Task SetSplitAsync(bool enabled, CancellationToken ct);
    Task EqualizeVfosAsync(CancellationToken ct);
    Task ToggleActiveVfoAsync(CancellationToken ct);
    Task SetModeAsync(RadioMode mode, CancellationToken ct);
    Task SetFilterSlotAsync(int filterSlot, CancellationToken ct);
    Task SetPttAsync(bool enabled, CancellationToken ct);
    Task SetCwKeyAsync(bool enabled, CancellationToken ct);
    Task SendCwTextAsync(string text, CancellationToken ct);
    Task StopCwSendAsync(CancellationToken ct);
    Task SetCwPitchAsync(int pitchHz, CancellationToken ct);
    Task SetCwKeyerSpeedAsync(int wpm, CancellationToken ct);
    Task SetVoiceMicGainAsync(int percent, CancellationToken ct);
    Task SetVoiceCompressionAsync(int percent, CancellationToken ct);
    Task SetRfPowerAsync(int percent, CancellationToken ct);
    Task SetPreampAsync(int level, CancellationToken ct);
    Task SetAttenuatorAsync(bool enabled, int db, CancellationToken ct);
    Task SetTunerEnabledAsync(bool enabled, CancellationToken ct);
    Task SetNoiseBlankerAsync(bool enabled, CancellationToken ct);
    Task SetNoiseReductionAsync(bool enabled, int level, CancellationToken ct);
    Task SetAutoNotchAsync(bool enabled, CancellationToken ct);
    Task SetManualNotchAsync(bool enabled, int width, int position, CancellationToken ct);
    Task SetIpPlusAsync(bool enabled, CancellationToken ct);
    Task SetFilterShapeSoftAsync(bool enabled, CancellationToken ct);
    Task RetuneAsync(CancellationToken ct);
    IObservable<RadioState> StateStream { get; }
    IObservable<WaterfallRow> ScopeRowStream { get; }
}
