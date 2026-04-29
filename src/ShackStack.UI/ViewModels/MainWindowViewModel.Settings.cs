using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShackStack.Core.Abstractions.Models;

namespace ShackStack.UI.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private string settingsCallsign = string.Empty;

    [ObservableProperty]
    private string settingsGridSquare = string.Empty;

    [ObservableProperty]
    private string settingsRadioBackend = "direct";

    [ObservableProperty]
    private string settingsCivPort = "auto";

    [ObservableProperty]
    private string settingsCivBaud = "115200";

    [ObservableProperty]
    private string settingsCivAddress = "94h";

    [ObservableProperty]
    private string settingsTheme = "dark";

    [ObservableProperty]
    private bool settingsBandConditionsEnabled = true;

    [ObservableProperty]
    private bool settingsShowExperimentalCw;

    [ObservableProperty]
    private bool settingsFlrigEnabled;

    [ObservableProperty]
    private string settingsFlrigHost = "127.0.0.1";

    [ObservableProperty]
    private string settingsFlrigPort = "12345";

    [ObservableProperty]
    private string settingsStatusMessage = string.Empty;

    [ObservableProperty]
    private bool settingsLongwaveEnabled;

    [ObservableProperty]
    private string settingsLongwaveBaseUrl = string.Empty;

    [ObservableProperty]
    private string settingsLongwaveClientApiToken = string.Empty;

    [ObservableProperty]
    private string settingsLongwaveDefaultLogbookName = "ShackStack Home";

    [ObservableProperty]
    private string settingsLongwaveDefaultLogbookNotes = "LONGWAVE_KIND=standard;POTA_MODE=hunting";

    public string SettingsPath => _settingsPath;

    [RelayCommand]
    private void ShowOperating() => CurrentWorkspace = WorkspaceKind.Operating;

    [RelayCommand]
    private void ShowSettings() => CurrentWorkspace = WorkspaceKind.Settings;

    [RelayCommand]
    private void ShowDiagnostics() => CurrentWorkspace = WorkspaceKind.Diagnostics;

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (_settingsStore is null)
        {
            SettingsStatusMessage = "Settings store unavailable";
            return;
        }

        if (!int.TryParse(SettingsCivBaud, out var civBaud)
            || !TryParseCivAddress(SettingsCivAddress, out var civAddress)
            || !int.TryParse(SettingsFlrigPort, out var flrigPort))
        {
            SettingsStatusMessage = "Enter valid values for baud, address (94h / 0x94 / 148), and FLRig port";
            return;
        }

        var updated = _settings with
        {
            Station = _settings.Station with
            {
                Callsign = SettingsCallsign.Trim().ToUpperInvariant(),
                GridSquare = SettingsGridSquare.Trim().ToUpperInvariant(),
            },
            Radio = _settings.Radio with
            {
                ControlBackend = SettingsRadioBackend.Trim(),
                CivPort = SettingsCivPort.Trim(),
                CivBaud = civBaud,
                CivAddress = civAddress,
            },
            Audio = _settings.Audio with
            {
                RxDeviceId = SelectedRxDevice?.DeviceId ?? string.Empty,
                TxDeviceId = SelectedTxDevice?.DeviceId ?? string.Empty,
                MicDeviceId = SelectedMicDevice?.DeviceId ?? string.Empty,
                MonitorDeviceId = SelectedMonitorDevice?.DeviceId ?? string.Empty,
                FreedvMonitorDeviceId = SelectedFreedvMonitorDevice?.DeviceId ?? string.Empty,
                MonitorVolumePercent = MonitorVolumePercent,
                FreedvMonitorVolumePercent = FreedvSpeechVolumePercent,
                MicGainPercent = VoiceMicGainPercent,
                VoiceCompressionPercent = VoiceCompressionPercent,
            },
            Interop = _settings.Interop with
            {
                FlrigEnabled = SettingsFlrigEnabled,
                FlrigHost = SettingsFlrigHost.Trim(),
                FlrigPort = flrigPort,
            },
            Longwave = _settings.Longwave with
            {
                Enabled = SettingsLongwaveEnabled,
                BaseUrl = SettingsLongwaveBaseUrl.Trim(),
                ClientApiToken = SettingsLongwaveClientApiToken.Trim(),
                DefaultLogbookName = SettingsLongwaveDefaultLogbookName.Trim(),
                DefaultLogbookNotes = SettingsLongwaveDefaultLogbookNotes.Trim(),
            },
            Ui = _settings.Ui with
            {
                Theme = SettingsTheme.Trim(),
                BandConditionsEnabled = SettingsBandConditionsEnabled,
                ShowExperimentalCw = SettingsShowExperimentalCw,
                WaterfallFloorPercent = WaterfallFloorPercent,
                WaterfallCeilingPercent = WaterfallCeilingPercent,
            }
        };

        await _settingsStore.SaveAsync(updated, CancellationToken.None);
        ApplySettings(updated);
        SettingsStatusMessage = "Saved. Radio changes apply on next connect.";
    }

    [RelayCommand]
    private async Task ReloadSettingsAsync()
    {
        if (_settingsStore is null)
        {
            SettingsStatusMessage = "Settings store unavailable";
            return;
        }

        var reloaded = await _settingsStore.LoadAsync(CancellationToken.None);
        ApplySettings(reloaded);
        SettingsStatusMessage = "Reloaded from disk";
    }

    partial void OnSettingsCallsignChanged(string value)
    {
        var normalized = FormatCallsign(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            SettingsCallsign = normalized;
            return;
        }

        WsjtxOperatorCallsign = normalized;
        LongwaveLogOperatorCallsign = normalized;
        RebuildWsjtxSuggestedMessages();
        RebuildJs8SuggestedMessages();
        UpdateHeaderCallsign();
    }

    partial void OnSettingsGridSquareChanged(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            SettingsGridSquare = normalized;
            return;
        }

        WsjtxOperatorGridSquare = normalized;
        RebuildWsjtxSuggestedMessages();
        RebuildJs8SuggestedMessages();
    }

    partial void OnConnectionStateChanged(string value) => UpdateHeaderCallsign();

    partial void OnCurrentWorkspaceChanged(WorkspaceKind value)
    {
        IsOperatingWorkspace = value == WorkspaceKind.Operating;
        IsSettingsWorkspace = value == WorkspaceKind.Settings;
        IsDiagnosticsWorkspace = value == WorkspaceKind.Diagnostics;
        UpdateBandConditionsVisibility();
    }

    partial void OnSettingsBandConditionsEnabledChanged(bool value)
    {
        IsBandConditionsEnabled = value;
        UpdateBandConditionsVisibility();
    }

    partial void OnIsBandConditionsEnabledChanged(bool value) => UpdateBandConditionsVisibility();

    private void ApplySettings(AppSettings settings)
    {
        _suppressRuntimeUiStatePersistence = true;
        try
        {
        _settings = settings;
        Theme = settings.Ui.Theme;
        WindowWidth = settings.Ui.WindowWidth;
        WindowHeight = settings.Ui.WindowHeight;
        RadioBackend = settings.Radio.ControlBackend;
        RadioPort = settings.Radio.CivPort;
        FlrigEndpoint = $"{settings.Interop.FlrigHost}:{settings.Interop.FlrigPort}";
        DiagnosticsRadioPort = settings.Radio.CivPort;
        DiagnosticsRadioBaud = settings.Radio.CivBaud.ToString();
        DiagnosticsRadioAddress = $"0x{settings.Radio.CivAddress:X2} ({settings.Radio.CivAddress})";
        DiagnosticsInterOpEndpoint = $"{settings.Interop.FlrigHost}:{settings.Interop.FlrigPort}";
        DiagnosticsBandConditionsState = settings.Ui.BandConditionsEnabled ? DiagnosticsBandConditionsState : "Disabled";

        SettingsCallsign = FormatCallsign(settings.Station.Callsign);
        SettingsGridSquare = settings.Station.GridSquare;
        SettingsRadioBackend = settings.Radio.ControlBackend;
        SettingsCivPort = settings.Radio.CivPort;
        SettingsCivBaud = settings.Radio.CivBaud.ToString();
        SettingsCivAddress = FormatCivAddress(settings.Radio.CivAddress);
        SettingsTheme = settings.Ui.Theme;
        SettingsBandConditionsEnabled = settings.Ui.BandConditionsEnabled;
        SettingsShowExperimentalCw = settings.Ui.ShowExperimentalCw;
        SettingsLongwaveEnabled = settings.Longwave.Enabled;
        SettingsLongwaveBaseUrl = settings.Longwave.BaseUrl;
        SettingsLongwaveClientApiToken = settings.Longwave.ClientApiToken;
        SettingsLongwaveDefaultLogbookName = settings.Longwave.DefaultLogbookName;
        SettingsLongwaveDefaultLogbookNotes = settings.Longwave.DefaultLogbookNotes;
        IsBandConditionsEnabled = settings.Ui.BandConditionsEnabled;
        ShowCwPanel = settings.Ui.ShowExperimentalCw;
        SettingsFlrigEnabled = settings.Interop.FlrigEnabled;
        SettingsFlrigHost = settings.Interop.FlrigHost;
        SettingsFlrigPort = settings.Interop.FlrigPort.ToString();
        MonitorVolumePercent = Math.Clamp(settings.Audio.MonitorVolumePercent, 0, 100);
        FreedvSpeechVolumePercent = Math.Clamp(settings.Audio.FreedvMonitorVolumePercent, 0, 100);
        WaterfallFloorPercent = Math.Clamp(settings.Ui.WaterfallFloorPercent, 0, 95);
        WaterfallCeilingPercent = Math.Clamp(settings.Ui.WaterfallCeilingPercent, WaterfallFloorPercent + 1, 100);
        _voiceRigSettingsDirty = false;
        LongwaveLogOperatorCallsign = FormatCallsign(settings.Station.Callsign);
        ApplyLongwaveSettingsState(settings);
        CanConnect = _radioService is not null
            && !CanDisconnect
            && string.Equals(_settings.Radio.ControlBackend, "direct", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(_settings.Radio.CivPort, "auto", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_settings.Radio.CivPort);

        UpdateAudioSelectionsFromSettings();
        UpdateModePanelsVisibility();
        }
        finally
        {
            _suppressRuntimeUiStatePersistence = false;
        }
    }

    public async Task PersistRuntimeUiStateAsync()
    {
        if (_settingsStore is null)
        {
            return;
        }

        var updated = _settings with
        {
            Audio = _settings.Audio with
            {
                MonitorVolumePercent = MonitorVolumePercent,
            },
            Ui = _settings.Ui with
            {
                WaterfallFloorPercent = WaterfallFloorPercent,
                WaterfallCeilingPercent = WaterfallCeilingPercent,
            }
        };

        await _settingsStore.SaveAsync(updated, CancellationToken.None);
        _settings = updated;
    }

    private void ScheduleRuntimeUiStateSave()
    {
        if (_suppressRuntimeUiStatePersistence || _settingsStore is null)
        {
            return;
        }

        _runtimeUiStateSaveCts?.Cancel();
        _runtimeUiStateSaveCts?.Dispose();
        _runtimeUiStateSaveCts = new CancellationTokenSource();
        var ct = _runtimeUiStateSaveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested)
                {
                    await PersistRuntimeUiStateAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, ct);
    }

    private void UpdateBandConditionsVisibility()
    {
        ShowBandConditionsPanel = IsOperatingWorkspace && IsBandConditionsEnabled;
    }

    private void UpdateModePanelsVisibility()
    {
        if (!ShowCwPanel && SelectedModePanelTabIndex == 1)
        {
            SelectedModePanelTabIndex = 0;
        }
    }

    private static string FormatCivAddress(int address) => $"{address:X2}h";
}
