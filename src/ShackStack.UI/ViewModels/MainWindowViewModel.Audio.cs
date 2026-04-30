using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShackStack.Core.Abstractions.Models;
using System.Runtime.InteropServices;

namespace ShackStack.UI.ViewModels;

public partial class MainWindowViewModel
{
    private float _rxMeterLevel;
    private float _txMeterLevel;
    private float _micMeterLevel;

    [ObservableProperty]
    private string audioMonitorState = "Stopped";

    [ObservableProperty]
    private int rxLevelPercent;

    [ObservableProperty]
    private int monitorVolumePercent = 75;

    [ObservableProperty]
    private int freedvSpeechVolumePercent = 75;

    [ObservableProperty]
    private string spectrumPathData = string.Empty;

    [ObservableProperty]
    private Bitmap? waterfallBitmap;

    [ObservableProperty]
    private bool canStopAudio;

    [ObservableProperty]
    private int txLevelPercent;

    [ObservableProperty]
    private int micLevelPercent;

    [ObservableProperty]
    private int waterfallFloorPercent = 8;

    [ObservableProperty]
    private int waterfallCeilingPercent = 92;

    private void SetAudioLevelPercentages(AudioLevels levels)
    {
        _rxMeterLevel = SmoothMeterLevel(_rxMeterLevel, levels.RxLevel);
        _txMeterLevel = SmoothMeterLevel(_txMeterLevel, levels.TxLevel);
        _micMeterLevel = SmoothMeterLevel(_micMeterLevel, levels.MicLevel);

        RxLevelPercent = ToMeterPercent(_rxMeterLevel);
        TxLevelPercent = ToMeterPercent(_txMeterLevel);
        MicLevelPercent = ToMeterPercent(_micMeterLevel);
    }

    private static float SmoothMeterLevel(float current, float next)
    {
        next = Math.Clamp(next, 0f, 1f);
        if (next <= 0.001f)
        {
            return 0f;
        }

        if (next >= current)
        {
            return next;
        }

        return (current * 0.72f) + (next * 0.28f);
    }

    private static int ToMeterPercent(float value) =>
        Math.Clamp((int)Math.Round(value * 100f), 0, 100);

    [ObservableProperty]
    private int waterfallZoom = 1;

    [ObservableProperty]
    private IReadOnlyList<AudioDeviceInfo> rxDeviceOptions = Array.Empty<AudioDeviceInfo>();

    [ObservableProperty]
    private IReadOnlyList<AudioDeviceInfo> outputDeviceOptions = Array.Empty<AudioDeviceInfo>();

    [ObservableProperty]
    private AudioDeviceInfo? selectedRxDevice;

    [ObservableProperty]
    private AudioDeviceInfo? selectedTxDevice;

    [ObservableProperty]
    private AudioDeviceInfo? selectedMicDevice;

    [ObservableProperty]
    private AudioDeviceInfo? selectedMonitorDevice;

    [ObservableProperty]
    private AudioDeviceInfo? selectedFreedvMonitorDevice;

    [ObservableProperty]
    private string voiceRxDeviceDisplay = "Not configured";

    [ObservableProperty]
    private string voiceTxDeviceDisplay = "Not configured";

    [ObservableProperty]
    private string voiceMicDeviceDisplay = "Not configured";

    [ObservableProperty]
    private string voiceMonitorDeviceDisplay = "Not configured";

    [ObservableProperty]
    private string freedvMonitorDeviceDisplay = "Not configured";

    [ObservableProperty]
    private string voiceTxStatus = "TX audio idle";

    [ObservableProperty]
    private int voiceMicGainPercent = 100;

    [ObservableProperty]
    private int voiceCompressionPercent;

    [ObservableProperty]
    private int voiceRfPowerPercent = 100;

    [RelayCommand]
    private async Task ApplyVoiceRigSettingsAsync()
    {
        if (_radioService is null || CanConnect)
        {
            VoiceTxStatus = "Radio not connected";
            return;
        }

        try
        {
            await ApplyVoiceRigSettingsCoreAsync();
            VoiceTxStatus = $"Rig voice settings applied: mic {VoiceMicGainPercent}% / comp {VoiceCompressionPercent} / rf {VoiceRfPowerPercent}%";
        }
        catch (Exception ex)
        {
            VoiceTxStatus = $"Voice settings error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshAudioDevicesAsync()
    {
        await LoadAudioDevicesAsync();
        SettingsStatusMessage = "Audio devices refreshed";
    }

    private async Task StartAudioMonitorAsync()
    {
        if (_audioService is null)
        {
            return;
        }

        var route = new AudioRoute(
            SelectedRxDevice?.DeviceId ?? string.Empty,
            SelectedTxDevice?.DeviceId ?? string.Empty,
            SelectedMicDevice?.DeviceId ?? string.Empty,
            SelectedMonitorDevice?.DeviceId ?? string.Empty);

        IsBusy = true;
        AudioMonitorState = "Starting...";

        try
        {
            await _audioService.StartReceiveAsync(route, CancellationToken.None);
            await ApplyMonitorVolumeAsync(MonitorVolumePercent);
            AudioMonitorState = "Running";
            CanStopAudio = true;
            SettingsStatusMessage = "Audio monitor started";
        }
        catch (Exception ex)
        {
            AudioMonitorState = $"Error: {ex.Message}";
            CanStopAudio = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopAudio))]
    private async Task StopAudioMonitorAsync()
    {
        if (_audioService is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _audioService.StopReceiveAsync(CancellationToken.None);
            AudioMonitorState = "Stopped";
            RxLevelPercent = 0;
            CanStopAudio = false;
            SettingsStatusMessage = "Audio monitor stopped";
        }
        catch (Exception ex)
        {
            AudioMonitorState = $"Stop failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]

    partial void OnCanStopAudioChanged(bool value) => StopAudioMonitorCommand.NotifyCanExecuteChanged();

    partial void OnMonitorVolumePercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (clamped != value)
        {
            MonitorVolumePercent = clamped;
            return;
        }

        _ = ApplyMonitorVolumeAsync(clamped);
        ScheduleRuntimeUiStateSave();
    }

    partial void OnSelectedMonitorDeviceChanged(AudioDeviceInfo? value)
    {
        VoiceMonitorDeviceDisplay = value?.FriendlyName ?? "Not configured";
        FreedvMonitorDeviceDisplay = DescribeFreedvMonitorDevice();
    }

    partial void OnAudioMonitorStateChanged(string value) => DiagnosticsAudioMonitorState = value;

    partial void OnVoiceTxStatusChanged(string value) => DiagnosticsVoiceTxState = value;

    partial void OnVoiceMicGainPercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (clamped != value)
        {
            VoiceMicGainPercent = clamped;
            return;
        }

        if (!_isUpdatingVoiceRigSettingsFromRadio)
        {
            _voiceRigSettingsDirty = true;
        }
    }

    partial void OnVoiceCompressionPercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 10);
        if (clamped != value)
        {
            VoiceCompressionPercent = clamped;
            return;
        }

        if (!_isUpdatingVoiceRigSettingsFromRadio)
        {
            _voiceRigSettingsDirty = true;
        }
    }

    partial void OnVoiceRfPowerPercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (clamped != value)
        {
            VoiceRfPowerPercent = clamped;
            return;
        }

        if (!_isUpdatingVoiceRigSettingsFromRadio)
        {
            _voiceRigSettingsDirty = true;
        }
    }

    partial void OnWaterfallFloorPercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 95);
        if (clamped != value)
        {
            WaterfallFloorPercent = clamped;
            return;
        }

        if (WaterfallFloorPercent >= WaterfallCeilingPercent)
        {
            WaterfallCeilingPercent = Math.Min(100, WaterfallFloorPercent + 1);
        }

        ApplyWaterfallDisplaySettings();
        ScheduleRuntimeUiStateSave();
    }

    partial void OnWaterfallCeilingPercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 1, 100);
        if (clamped != value)
        {
            WaterfallCeilingPercent = clamped;
            return;
        }

        if (WaterfallCeilingPercent <= WaterfallFloorPercent)
        {
            WaterfallFloorPercent = Math.Max(0, WaterfallCeilingPercent - 1);
        }

        ApplyWaterfallDisplaySettings();
        ScheduleRuntimeUiStateSave();
    }

    private async Task InitializeAudioAsync()
    {
        await LoadAudioDevicesAsync();
        await EnsureAudioMonitorStartedAsync();
    }

    private async Task LoadAudioDevicesAsync()
    {
        if (_audioService is null)
        {
            return;
        }

        try
        {
            var devices = await _audioService.GetDevicesAsync(CancellationToken.None);
            var inputs = devices.Where(d => d.IsInput).ToArray();
            var outputs = devices.Where(d => d.IsOutput).ToArray();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RxDeviceOptions = inputs;
                OutputDeviceOptions = outputs;
                UpdateAudioSelectionsFromSettings();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SettingsStatusMessage = $"Audio enumeration failed: {ex.Message}";
            });
        }
    }

    private async Task EnsureAudioMonitorStartedAsync()
    {
        if (_monitorAutostartAttempted || _audioService is null || CanStopAudio)
        {
            return;
        }

        _monitorAutostartAttempted = true;

        if (SelectedRxDevice is null || SelectedMonitorDevice is null)
        {
            AudioMonitorState = "Not configured";
            return;
        }

        await StartAudioMonitorAsync();
    }

    private async Task ApplyMonitorVolumeAsync(int volumePercent)
    {
        if (_audioService is null)
        {
            return;
        }

        try
        {
            await _audioService.SetMonitorVolumeAsync(volumePercent / 100f, CancellationToken.None);
        }
        catch
        {
        }
    }

    private async Task ApplyVoiceRigSettingsCoreAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.SetVoiceMicGainAsync(VoiceMicGainPercent, CancellationToken.None);
            await _radioService.SetVoiceCompressionAsync(VoiceCompressionPercent, CancellationToken.None);
            await _radioService.SetRfPowerAsync(VoiceRfPowerPercent, CancellationToken.None);
            _voiceRigSettingsDirty = false;
        }
        catch
        {
        }
    }

    private AudioRoute BuildCurrentAudioRoute() => new(
        SelectedRxDevice?.DeviceId ?? string.Empty,
        SelectedTxDevice?.DeviceId ?? string.Empty,
        SelectedMicDevice?.DeviceId ?? string.Empty,
        SelectedMonitorDevice?.DeviceId ?? string.Empty);

    private void ApplyWaterfallDisplaySettings()
    {
        _waterfallService?.UpdateDisplaySettings(
            WaterfallFloorPercent / 100f,
            WaterfallCeilingPercent / 100f,
            WaterfallZoom);
        UpdateWaterfallBitmap();
    }

    private void UpdateAudioSelectionsFromSettings()
    {
        SelectedRxDevice = FindDevice(RxDeviceOptions, _settings.Audio.RxDeviceId);
        SelectedMicDevice = FindDevice(RxDeviceOptions, _settings.Audio.MicDeviceId);
        SelectedTxDevice = FindDevice(OutputDeviceOptions, _settings.Audio.TxDeviceId);
        SelectedMonitorDevice = FindDevice(OutputDeviceOptions, _settings.Audio.MonitorDeviceId);
        SelectedFreedvMonitorDevice = FindDevice(OutputDeviceOptions, _settings.Audio.FreedvMonitorDeviceId);
        VoiceRxDeviceDisplay = SelectedRxDevice?.FriendlyName ?? "Not configured";
        VoiceTxDeviceDisplay = SelectedTxDevice?.FriendlyName ?? "Not configured";
        VoiceMicDeviceDisplay = SelectedMicDevice?.FriendlyName ?? "Not configured";
        VoiceMonitorDeviceDisplay = SelectedMonitorDevice?.FriendlyName ?? "Not configured";
        FreedvMonitorDeviceDisplay = DescribeFreedvMonitorDevice();
        DiagnosticsAudioRadioRx = VoiceRxDeviceDisplay;
        DiagnosticsAudioRadioTx = VoiceTxDeviceDisplay;
        DiagnosticsAudioPcTx = VoiceMicDeviceDisplay;
        DiagnosticsAudioPcRx = VoiceMonitorDeviceDisplay;
    }

    private static AudioDeviceInfo? FindDevice(IReadOnlyList<AudioDeviceInfo> devices, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return devices.FirstOrDefault(d => d.IsDefault);
        }

        return devices.FirstOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault(d => d.IsDefault);
    }

    private void UpdateWaterfallBitmap()
    {
        var frame = _waterfallRenderSource?.GetLatestFrame();
        if (frame is null || frame.Width <= 0 || frame.Height <= 0 || frame.WaterfallPixels.Length == 0)
        {
            return;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using (var locked = bitmap.Lock())
        {
            Marshal.Copy(frame.WaterfallPixels, 0, locked.Address, frame.WaterfallPixels.Length);
        }

        WaterfallBitmap = bitmap;
        FrequencyMarkers = BuildFrequencyMarkers(frame.CenterFrequencyHz, frame.SpanHz);
    }

    private static string BuildSpectrumPath(IReadOnlyList<float> bins, double width, double height)
    {
        if (bins.Count == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < bins.Count; i++)
        {
            var x = bins.Count == 1 ? 0 : width * i / (bins.Count - 1);
            var y = height - (Math.Clamp(bins[i], 0f, 1f) * height);
            sb.Append(i == 0 ? "M " : " L ");
            sb.Append(x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(' ');
            sb.Append(y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> BuildFrequencyMarkers(long centerFrequencyHz, int spanHz)
    {
        if (centerFrequencyHz <= 0 || spanHz <= 0)
        {
            return ["---", "---", "---", "---", "---"];
        }

        var half = spanHz / 2.0;
        return
        [
            FormatMarker(centerFrequencyHz - (long)half),
            FormatMarker(centerFrequencyHz - (long)(half / 2.0)),
            FormatMarker(centerFrequencyHz),
            FormatMarker(centerFrequencyHz + (long)(half / 2.0)),
            FormatMarker(centerFrequencyHz + (long)half),
        ];
    }

    private static string FormatMarker(long hz) => $"{hz / 1_000_000.0:0.000}";
}
