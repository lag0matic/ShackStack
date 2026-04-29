using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShackStack.Core.Abstractions.Models;

namespace ShackStack.UI.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private RadioMode selectedMode = RadioMode.Usb;

    [ObservableProperty]
    private int selectedFilterSlot = 2;

    [ObservableProperty]
    private int preampLevel;

    [ObservableProperty]
    private IBrush preampOffBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private IBrush preamp1Brush = BuildToggleBrush(false);

    [ObservableProperty]
    private IBrush preamp2Brush = BuildToggleBrush(false);

    [ObservableProperty]
    private bool isAttenuatorEnabled;

    [ObservableProperty]
    private IBrush attenuatorButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private bool isTunerEnabled;

    [ObservableProperty]
    private IBrush tunerButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private bool isNoiseBlankerEnabled;

    [ObservableProperty]
    private IBrush noiseBlankerButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private bool isNoiseReductionEnabled;

    [ObservableProperty]
    private IBrush noiseReductionButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private int noiseReductionLevel;

    public IReadOnlyList<int> NoiseReductionLevelOptions { get; } = Enumerable.Range(0, 16).ToArray();

    [ObservableProperty]
    private bool isAutoNotchEnabled;

    [ObservableProperty]
    private IBrush autoNotchButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private bool isManualNotchEnabled;

    [ObservableProperty]
    private IBrush manualNotchButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private int manualNotchWidth = 1;

    public IReadOnlyList<int> ManualNotchWidthOptions { get; } = [0, 1, 2];

    public IReadOnlyList<string> ManualNotchWidthLabels { get; } = ["Wide", "Mid", "Nar"];

    [ObservableProperty]
    private string selectedManualNotchWidth = "Mid";

    [ObservableProperty]
    private int manualNotchPosition = 128;

    [ObservableProperty]
    private bool isIpPlusEnabled;

    [ObservableProperty]
    private IBrush ipPlusButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private bool isFilterShapeSoft;

    [ObservableProperty]
    private IBrush filterSharpBrush = BuildToggleBrush(true);

    [ObservableProperty]
    private IBrush filterSoftBrush = BuildToggleBrush(false);

    partial void OnNoiseReductionLevelChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 15);
        if (clamped != value)
        {
            NoiseReductionLevel = clamped;
            return;
        }

        if (!_isUpdatingNoiseReductionLevelFromRadio)
        {
            _noiseReductionInteractionUntilUtc = DateTimeOffset.UtcNow.AddSeconds(4);
        }
    }

    partial void OnManualNotchPositionChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 255);
        if (clamped != value)
        {
            ManualNotchPosition = clamped;
        }
    }

    [RelayCommand]
    private async Task SetSelectedModeAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _radioService.SetModeAsync(SelectedMode, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Mode change failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetModeDirectAsync(RadioMode mode)
    {
        SelectedMode = mode;
        AvailableModes = BuildModePresets(mode);
        await SetSelectedModeAsync();
    }

    [RelayCommand]
    private async Task AdjustVfoBByStepAsync(long deltaHz)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            var next = Math.Max(30_000, VfoBFrequencyHz + deltaHz);
            await _radioService.SetVfoBFrequencyAsync(next, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"VFO B tune failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExchangeVfosAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.ToggleActiveVfoAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"A/B failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task EqualizeVfosAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.EqualizeVfosAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"A=B failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleSplitAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.SetSplitAsync(!IsSplitEnabled, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Split failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetFilterWidthAsync(object? filterSlot)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        var slot = filterSlot switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => SelectedFilterSlot,
        };

        IsBusy = true;
        try
        {
            await _radioService.SetFilterSlotAsync(slot, CancellationToken.None);
            UpdateActiveFilterSlot(slot);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Filter change failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task JumpToBandAsync(long frequencyHz)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _radioService.SetFrequencyAsync(frequencyHz, CancellationToken.None);
            UpdateActiveBand(frequencyHz);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Band jump failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AdjustFrequencyByStepAsync(long deltaHz)
    {
        if (_radioService is null || CanConnect || deltaHz == 0)
        {
            return;
        }

        var next = Math.Clamp(CurrentFrequencyHz + deltaHz, 100_000L, 450_000_000L);
        if (next == CurrentFrequencyHz)
        {
            return;
        }

        CurrentFrequencyHz = next;
        FrequencyDisplay = $"{next:N0} Hz";
        UpdateActiveBand(next);
        IsBusy = true;

        try
        {
            await _radioService.SetFrequencyAsync(next, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Tune failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SetWaterfallZoom(object? zoom)
    {
        var value = zoom switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => WaterfallZoom,
        };

        WaterfallZoom = Math.Clamp(value, 1, 10);
        Zoom1Brush = BuildToggleBrush(WaterfallZoom == 1);
        Zoom2Brush = BuildToggleBrush(WaterfallZoom == 2);
        Zoom5Brush = BuildToggleBrush(WaterfallZoom == 5);
        Zoom10Brush = BuildToggleBrush(WaterfallZoom == 10);
        ApplyWaterfallDisplaySettings();
    }

    public async Task SetPttPressedAsync(bool isPressed)
    {
        if (_radioService is null || _audioService is null || CanConnect)
        {
            return;
        }

        var shouldEnableTx = isPressed;
        var isCurrentlyTx = string.Equals(PttState, "TX", StringComparison.OrdinalIgnoreCase);
        if (shouldEnableTx == isCurrentlyTx)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var route = BuildCurrentAudioRoute();
            if (shouldEnableTx)
            {
                await _audioService.StartTransmitAsync(route, CancellationToken.None);
                await _radioService.SetPttAsync(true, CancellationToken.None);
                VoiceTxStatus = "TX audio live";
            }
            else
            {
                await _radioService.SetPttAsync(false, CancellationToken.None);
                await _audioService.StopTransmitAsync(CancellationToken.None);
                VoiceTxStatus = "TX audio idle";
            }
        }
        catch (Exception ex)
        {
            if (shouldEnableTx)
            {
                try
                {
                    await _audioService.StopTransmitAsync(CancellationToken.None);
                }
                catch
                {
                }
            }
            RadioStatusSummary = $"PTT change failed: {ex.Message}";
            VoiceTxStatus = $"TX error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetPreampAsync(object? level)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        var parsedLevel = level switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => PreampLevel,
        };

        try
        {
            await _radioService.SetPreampAsync(parsedLevel, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Preamp change failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleAttenuatorAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.SetAttenuatorAsync(!IsAttenuatorEnabled, 20, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"ATT change failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleTunerAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.SetTunerEnabledAsync(!IsTunerEnabled, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Tuner change failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RetuneAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.RetuneAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Retune failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleNoiseBlankerAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.SetNoiseBlankerAsync(!IsNoiseBlankerEnabled, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"NB change failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleNoiseReductionAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            var enable = !IsNoiseReductionEnabled;
            var level = enable ? Math.Max(NoiseReductionLevel, 5) : 0;
            _noiseReductionInteractionUntilUtc = DateTimeOffset.UtcNow.AddSeconds(2);
            await _radioService.SetNoiseReductionAsync(enable, level, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"NR change failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetNoiseReductionLevelAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            var level = Math.Clamp(NoiseReductionLevel, 0, 15);
            _noiseReductionInteractionUntilUtc = DateTimeOffset.UtcNow.AddSeconds(2);
            await _radioService.SetNoiseReductionAsync(level > 0 || IsNoiseReductionEnabled, level, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"NR level failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleAutoNotchAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.SetAutoNotchAsync(!IsAutoNotchEnabled, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"AN failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleManualNotchAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            var width = ParseManualNotchWidth(SelectedManualNotchWidth);
            var position = ParseManualNotchPosition();
            await _radioService.SetManualNotchAsync(!IsManualNotchEnabled, width, position, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"MN failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetManualNotchAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            var width = ParseManualNotchWidth(SelectedManualNotchWidth);
            var position = ParseManualNotchPosition();
            await _radioService.SetManualNotchAsync(IsManualNotchEnabled, width, position, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"MN set failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleIpPlusAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.SetIpPlusAsync(!IsIpPlusEnabled, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"IP+ change failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetFilterShapeAsync(string shape)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            var soft = string.Equals(shape, "soft", StringComparison.OrdinalIgnoreCase);
            await _radioService.SetFilterShapeSoftAsync(soft, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Filter shape failed: {ex.Message}";
        }
    }

    public void RefreshFromRadioSnapshot()
    {
        if (_radioService is null)
        {
            return;
        }

        var snapshot = _radioService.CurrentState;
        ApplyRadioState(snapshot, forceRigBackedControls: true);
    }

    private void ApplyRadioState(RadioState state, bool forceRigBackedControls = false)
    {
        ConnectionState = state.IsConnected ? "Connected" : "Disconnected";
        if (state.FrequencyHz > 0)
        {
            CurrentFrequencyHz = state.FrequencyHz;
        }
        if (state.VfoAFrequencyHz > 0)
        {
            VfoAFrequencyHz = state.VfoAFrequencyHz;
        }
        if (state.VfoBFrequencyHz > 0)
        {
            VfoBFrequencyHz = state.VfoBFrequencyHz;
        }
        IsVfoBActive = state.IsVfoBActive;
        OnPropertyChanged(nameof(IsVfoAActive));
        IsSplitEnabled = state.IsSplitEnabled;

        FrequencyDisplay = state.FrequencyHz > 0 ? $"{state.FrequencyHz:N0} Hz" : "---";
        ModeDisplay = FormatModeDisplay(state.Mode);
        if (SelectedMode != state.Mode)
        {
            SelectedMode = state.Mode;
            AvailableModes = BuildModePresets(state.Mode);
        }

        PttState = state.IsPttActive ? "TX" : "RX";
        FilterWidthDisplay = $"F{state.FilterSlot}  |  {state.FilterWidthHz} Hz";
        SelectedFilterSlot = state.FilterSlot;
        UpdateActiveFilterSlot(state.FilterSlot);
        PreampLevel = state.PreampLevel;
        IsAttenuatorEnabled = state.AttenuatorDb > 0;
        IsTunerEnabled = state.IsTunerEnabled;
        IsNoiseBlankerEnabled = state.IsNoiseBlankerEnabled;
        IsNoiseReductionEnabled = state.IsNoiseReductionEnabled;
        if (DateTimeOffset.UtcNow >= _noiseReductionInteractionUntilUtc || NoiseReductionLevel == state.NoiseReductionLevel)
        {
            _isUpdatingNoiseReductionLevelFromRadio = true;
            NoiseReductionLevel = state.NoiseReductionLevel;
            _isUpdatingNoiseReductionLevelFromRadio = false;
        }
        IsAutoNotchEnabled = state.IsAutoNotchEnabled;
        IsManualNotchEnabled = state.IsManualNotchEnabled;
        ManualNotchWidth = state.ManualNotchWidth;
        SelectedManualNotchWidth = state.ManualNotchWidth switch
        {
            0 => "Wide",
            2 => "Nar",
            _ => "Mid",
        };
        ManualNotchPosition = Math.Clamp(state.ManualNotchPosition, 0, 255);
        IsIpPlusEnabled = state.IsIpPlusEnabled;
        IsFilterShapeSoft = state.IsFilterShapeSoft;
        UpdateActiveBand(state.FrequencyHz);
        UpdateRadioControlBrushes();
        ApplySmeterLevel(state.Smeter);

        if (!state.IsConnected)
        {
            _hasReceivedVoiceRigStateFromRadio = false;
        }

        if (state.IsConnected && (forceRigBackedControls || !_voiceRigSettingsDirty || !_hasReceivedVoiceRigStateFromRadio))
        {
            _isUpdatingVoiceRigSettingsFromRadio = true;
            VoiceMicGainPercent = state.VoiceMicGainPercent;
            VoiceCompressionPercent = state.VoiceCompressionPercent;
            VoiceRfPowerPercent = state.VoiceRfPowerPercent;
            _isUpdatingVoiceRigSettingsFromRadio = false;
            _hasReceivedVoiceRigStateFromRadio = true;
        }

        if (forceRigBackedControls || !_cwRigSettingsDirty)
        {
            _isUpdatingCwRigSettingsFromRadio = true;
            CwPitchHz = state.CwPitchHz;
            CwWpm = state.CwKeyerWpm;
            _isUpdatingCwRigSettingsFromRadio = false;
        }

        RadioStatusSummary = state.IsConnected
            ? $"{ModeDisplay}  |  {FrequencyDisplay}  |  {FilterWidthDisplay}"
            : "Radio idle";
        CanDisconnect = state.IsConnected;
        CanConnect = !state.IsConnected
            && _radioService is not null
            && string.Equals(_settings.Radio.ControlBackend, "direct", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(_settings.Radio.CivPort, "auto", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_settings.Radio.CivPort);
        IsBusy = false;
    }

    private static bool TryParseCivAddress(string value, out int address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out address);
        }

        if (trimmed.EndsWith("h", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(trimmed[..^1], System.Globalization.NumberStyles.HexNumber, null, out address);
        }

        return int.TryParse(trimmed, out address);
    }

    private void UpdateActiveBand(long frequencyHz)
    {
        var activeLabel = GetBandLabelForFrequency(frequencyHz);
        if (string.Equals(activeLabel, _activeBandLabel, StringComparison.Ordinal))
        {
            return;
        }

        _activeBandLabel = activeLabel;
        AvailableBands = BuildBandPresets(activeLabel);
    }

    private void UpdateRadioControlBrushes()
    {
        PreampOffBrush = BuildToggleBrush(PreampLevel == 0);
        Preamp1Brush = BuildToggleBrush(PreampLevel == 1);
        Preamp2Brush = BuildToggleBrush(PreampLevel == 2);
        SplitButtonBrush = BuildToggleBrush(IsSplitEnabled);
        AttenuatorButtonBrush = BuildToggleBrush(IsAttenuatorEnabled);
        TunerButtonBrush = BuildToggleBrush(IsTunerEnabled);
        NoiseBlankerButtonBrush = BuildToggleBrush(IsNoiseBlankerEnabled);
        NoiseReductionButtonBrush = BuildToggleBrush(IsNoiseReductionEnabled);
        AutoNotchButtonBrush = BuildToggleBrush(IsAutoNotchEnabled);
        ManualNotchButtonBrush = BuildToggleBrush(IsManualNotchEnabled);
        IpPlusButtonBrush = BuildToggleBrush(IsIpPlusEnabled);
        FilterSharpBrush = BuildToggleBrush(!IsFilterShapeSoft);
        FilterSoftBrush = BuildToggleBrush(IsFilterShapeSoft);
    }

    private int ParseManualNotchWidth(string value) => value switch
    {
        "Wide" => 0,
        "Nar" => 2,
        _ => 1,
    };

    private int ParseManualNotchPosition()
    {
        return Math.Clamp(ManualNotchPosition, 0, 255);
    }

    private void UpdateActiveFilterSlot(int filterSlot)
    {
        var normalized = Math.Clamp(filterSlot, 1, 3);
        if (normalized == _activeFilterSlot)
        {
            return;
        }

        _activeFilterSlot = normalized;
        AvailableFilterWidths = BuildFilterPresets(normalized);
    }

    private static IReadOnlyList<FilterPreset> BuildFilterPresets(int selectedSlot)
    {
        return
        [
            new FilterPreset("F1", 1, BuildToggleBrush(selectedSlot == 1)),
            new FilterPreset("F2", 2, BuildToggleBrush(selectedSlot == 2)),
            new FilterPreset("F3", 3, BuildToggleBrush(selectedSlot == 3)),
        ];
    }

    private static IReadOnlyList<ModePreset> BuildModePresets(RadioMode selectedMode)
    {
        return
        [
            new ModePreset("LSB", RadioMode.Lsb, BuildToggleBrush(selectedMode == RadioMode.Lsb)),
            new ModePreset("LSB-D", RadioMode.LsbData, BuildToggleBrush(selectedMode == RadioMode.LsbData)),
            new ModePreset("USB", RadioMode.Usb, BuildToggleBrush(selectedMode == RadioMode.Usb)),
            new ModePreset("USB-D", RadioMode.UsbData, BuildToggleBrush(selectedMode == RadioMode.UsbData)),
            new ModePreset("CW", RadioMode.Cw, BuildToggleBrush(selectedMode == RadioMode.Cw)),
            new ModePreset("AM", RadioMode.Am, BuildToggleBrush(selectedMode == RadioMode.Am)),
            new ModePreset("FM", RadioMode.Fm, BuildToggleBrush(selectedMode == RadioMode.Fm)),
            new ModePreset("RTTY", RadioMode.Rtty, BuildToggleBrush(selectedMode == RadioMode.Rtty)),
        ];
    }

    private static IReadOnlyList<BandPreset> BuildBandPresets(string activeLabel)
    {
        return
        [
            new BandPreset("160m", 1_900_000, BuildToggleBrush(activeLabel == "160m")),
            new BandPreset("80m", 3_750_000, BuildToggleBrush(activeLabel == "80m")),
            new BandPreset("40m", 7_150_000, BuildToggleBrush(activeLabel == "40m")),
            new BandPreset("30m", 10_125_000, BuildToggleBrush(activeLabel == "30m")),
            new BandPreset("20m", 14_175_000, BuildToggleBrush(activeLabel == "20m")),
            new BandPreset("17m", 18_118_000, BuildToggleBrush(activeLabel == "17m")),
            new BandPreset("15m", 21_225_000, BuildToggleBrush(activeLabel == "15m")),
            new BandPreset("12m", 24_940_000, BuildToggleBrush(activeLabel == "12m")),
            new BandPreset("10m", 28_400_000, BuildToggleBrush(activeLabel == "10m")),
            new BandPreset("6m", 50_250_000, BuildToggleBrush(activeLabel == "6m")),
        ];
    }


    private static string GetBandLabelForFrequency(long frequencyHz) => frequencyHz switch
    {
        >= 1_800_000 and <= 2_000_000 => "160m",
        >= 3_500_000 and <= 4_000_000 => "80m",
        >= 7_000_000 and <= 7_300_000 => "40m",
        >= 10_100_000 and <= 10_150_000 => "30m",
        >= 14_000_000 and <= 14_350_000 => "20m",
        >= 18_068_000 and <= 18_168_000 => "17m",
        >= 21_000_000 and <= 21_450_000 => "15m",
        >= 24_890_000 and <= 24_990_000 => "12m",
        >= 28_000_000 and <= 29_700_000 => "10m",
        >= 50_000_000 and <= 54_000_000 => "6m",
        _ => string.Empty,
    };

    private static IBrush BuildToggleBrush(bool active) =>
        new SolidColorBrush(Color.Parse(active ? "#1A1D2E" : "#0C1017"));

    private static IBrush BuildConditionBrush(string condition) => new SolidColorBrush(Color.Parse(condition switch
    {
        "Good" => "#12301F",
        "Fair" => "#33290F",
        "Poor" => "#351314",
        "Band Closed" => "#16181D",
        _ => "#16181D",
    }));

    private static IReadOnlyList<int> GetFilterOptions(RadioMode mode) => mode switch
    {
        RadioMode.Cw => [250, 500, 1200],
        RadioMode.Rtty => [250, 500, 1200],
        RadioMode.LsbData => [1800, 2400, 3000],
        RadioMode.UsbData => [1800, 2400, 3000],
        _ => [1800, 2400, 3000],
    };

    private static string FormatModeDisplay(RadioMode mode) => mode switch
    {
        RadioMode.LsbData => "LSB-D",
        RadioMode.UsbData => "USB-D",
        _ => mode.ToString().ToUpperInvariant(),
    };

    private void ApplySmeterLevel(int targetLevel)
    {
        _targetSmeterLevel = Math.Clamp(targetLevel / 25.5, 0.0, 10.0);
        if (_targetSmeterLevel > _displayedSmeterLevel)
        {
            _displayedSmeterLevel = _targetSmeterLevel;
            RenderSmeter();
        }
    }

    private void AnimateSmeter()
    {
        var delta = _targetSmeterLevel - _displayedSmeterLevel;
        if (Math.Abs(delta) < 0.02)
        {
            if (Math.Abs(delta) > 0)
            {
                _displayedSmeterLevel = _targetSmeterLevel;
                RenderSmeter();
            }

            return;
        }

        var step = delta > 0
            ? Math.Max(0.15, delta * 0.45)
            : Math.Min(-0.08, delta * 0.20);

        _displayedSmeterLevel = Math.Clamp(_displayedSmeterLevel + step, 0.0, 10.0);
        RenderSmeter();
    }

    private void RenderSmeter()
    {
        var roundedDisplay = (int)Math.Round(_displayedSmeterLevel, MidpointRounding.AwayFromZero);
        SmeterDisplay = roundedDisplay >= 10 ? "S9+" : $"S{roundedDisplay}";
        SmeterSegmentBrushes = BuildSmeterBrushes(_displayedSmeterLevel);
    }

    private static IReadOnlyList<IBrush> BuildSmeterBrushes(double level)
    {
        var brushes = new IBrush[11];
        var clamped = Math.Clamp(level, 0.0, 10.0);
        for (var i = 0; i < brushes.Length; i++)
        {
            var segmentFill = Math.Clamp(clamped - i, 0.0, 1.0);
            var active = segmentFill > 0.001;
            if (i < 7)
            {
                brushes[i] = BuildMeterSegmentBrush(active ? "#1D9E75" : "#0A1A10", segmentFill);
            }
            else
            {
                brushes[i] = BuildMeterSegmentBrush(active ? "#EF9F27" : "#1A1200", segmentFill);
            }
        }

        return brushes;
    }

    private static IBrush BuildMeterSegmentBrush(string activeColorHex, double fill)
    {
        var active = Color.Parse(activeColorHex);
        var off = Color.Parse(activeColorHex == "#EF9F27" ? "#1A1200" : "#0A1A10");
        var clamped = Math.Clamp(fill, 0.0, 1.0);
        byte Lerp(byte from, byte to) => (byte)Math.Round(from + ((to - from) * clamped));
        return new SolidColorBrush(Color.FromRgb(
            Lerp(off.R, active.R),
            Lerp(off.G, active.G),
            Lerp(off.B, active.B)));
    }
}