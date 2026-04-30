using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShackStack.Core.Abstractions.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace ShackStack.UI.ViewModels;

public partial class MainWindowViewModel
{
    private bool _freedvPttRequested;

    [ObservableProperty]
    private int cwPitchHz = 700;

    [ObservableProperty]
    private int cwWpm = 20;

    [ObservableProperty]
    private int cwBandwidthHz = 220;

    [ObservableProperty]
    private bool cwMatchedFilterEnabled = true;

    [ObservableProperty]
    private bool cwTrackingEnabled = true;

    [ObservableProperty]
    private int cwTrackingRangeWpm = 8;

    [ObservableProperty]
    private int cwLowerWpmLimit = 5;

    [ObservableProperty]
    private int cwUpperWpmLimit = 60;

    [ObservableProperty]
    private IReadOnlyList<string> cwAttackOptions = ["Fast", "Normal", "Slow"];

    [ObservableProperty]
    private string cwSelectedAttack = "Normal";

    [ObservableProperty]
    private IReadOnlyList<string> cwDecayOptions = ["Fast", "Normal", "Slow"];

    [ObservableProperty]
    private string cwSelectedDecay = "Slow";

    [ObservableProperty]
    private IReadOnlyList<string> cwNoiseCharacterOptions = ["Suppress", "Asterisk", "Underscore", "Space"];

    [ObservableProperty]
    private string cwSelectedNoiseCharacter = "Suppress";

    [ObservableProperty]
    private bool cwAutoToneSearchEnabled = true;

    [ObservableProperty]
    private bool cwAfcEnabled = true;

    [ObservableProperty]
    private int cwToneSearchSpanHz = 250;

    [ObservableProperty]
    private IReadOnlyList<string> cwSquelchOptions = ["Off", "Low", "Medium", "High"];

    [ObservableProperty]
    private string cwSelectedSquelch = "Off";

    [ObservableProperty]
    private IReadOnlyList<string> cwSpacingOptions = ["Tight", "Normal", "Loose"];

    [ObservableProperty]
    private string cwSelectedSpacing = "Normal";

    [ObservableProperty]
    private string cwDecoderProfile = "Adaptive Python";

    [ObservableProperty]
    private string cwDecoderStatus = "Ready";

    [ObservableProperty]
    private string cwDecoderWorker = "None";

    [ObservableProperty]
    private string cwDecoderConfidenceDisplay = "0%";

    [ObservableProperty]
    private string cwEstimatedPitchDisplay = "---";

    [ObservableProperty]
    private string cwEstimatedWpmDisplay = "---";

    [ObservableProperty]
    private string cwDecodedText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> rttyProfileOptions =
    [
        "170 Hz / 45.45 baud",
        "170 Hz / 75 baud",
        "425 Hz / 45.45 baud"
    ];

    [ObservableProperty]
    private string rttySelectedProfile = "170 Hz / 45.45 baud";

    [ObservableProperty]
    private IReadOnlyList<string> rttyFrequencyOptions =
    [
        "14.080 MHz USB",
        "14.083 MHz USB",
        "7.080 MHz LSB",
        "3.580 MHz LSB"
    ];

    [ObservableProperty]
    private string rttySelectedFrequency = "14.080 MHz USB";

    [ObservableProperty]
    private bool rttyDecodeCurrentRadioFrequency = true;

    [ObservableProperty]
    private string rttyAudioCenterHz = "1700";

    [ObservableProperty]
    private string rttyTuneHelperSuggestion = "Start RX, then place an RTTY signal in the passband; helper will suggest Audio Hz.";

    [ObservableProperty]
    private double rttySuggestedAudioCenterHz;

    [ObservableProperty]
    private bool rttyReversePolarity;

    [ObservableProperty]
    private string rttyRxStatus = "RTTY receiver ready";

    [ObservableProperty]
    private string rttySessionNotes = "For IC-7300 audio RTTY, use USB-D/LSB-D rather than native RTTY mode. Tune the signal, select shift/baud, then start receive.";

    [ObservableProperty]
    private string rttyDecodedText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> keyboardModeOptions = ["BPSK31", "BPSK63"];

    [ObservableProperty]
    private string keyboardSelectedMode = "BPSK31";

    [ObservableProperty]
    private IReadOnlyList<string> keyboardFrequencyOptions =
    [
        "14.070 MHz USB-D",
        "7.070 MHz LSB-D",
        "21.070 MHz USB-D",
        "28.120 MHz USB-D"
    ];

    [ObservableProperty]
    private string keyboardSelectedFrequency = "14.070 MHz USB-D";

    [ObservableProperty]
    private bool keyboardUseCurrentRadioFrequency = true;

    [ObservableProperty]
    private string keyboardAudioCenterHz = "1000";

    [ObservableProperty]
    private bool keyboardReversePolarity;

    [ObservableProperty]
    private string keyboardTuneHelperSuggestion = "Start RX, tune the PSK trace into the passband, then apply the helper if AFC settles off-center.";

    [ObservableProperty]
    private double keyboardSuggestedAudioCenterHz;

    [ObservableProperty]
    private string keyboardRxStatus = "Keyboard modes ready";

    [ObservableProperty]
    private string keyboardSessionNotes = "PSK31/63 receive uses the fldigi-derived GPL sidecar. Use USB-D/LSB-D and place the trace near the selected audio center.";

    [ObservableProperty]
    private string keyboardDecodedText = string.Empty;

    [ObservableProperty]
    private string keyboardTransmitText = "CQ CQ DE W8STR W8STR K";

    [ObservableProperty]
    private string keyboardPreparedTransmitStatus = "No PSK TX audio prepared.";

    [ObservableProperty]
    private string keyboardPreparedTransmitPath = "No prepared PSK WAV.";

    public bool KeyboardHasPreparedTransmitClip => _keyboardPreparedTransmitClip is not null;

    [ObservableProperty]
    private IReadOnlyList<string> freedvModeOptions = ["RADEV1", "700D", "700E", "700C", "1600"];

    [ObservableProperty]
    private string freedvSelectedMode = "RADEV1";

    [ObservableProperty]
    private IReadOnlyList<string> freedvFrequencyOptions =
    [
        "14.236 MHz USB-D",
        "7.177 MHz LSB-D",
        "21.313 MHz USB-D",
        "28.330 MHz USB-D"
    ];

    [ObservableProperty]
    private string freedvSelectedFrequency = "14.236 MHz USB-D";

    [ObservableProperty]
    private bool freedvUseCurrentRadioFrequency;

    public string FreedvActiveFrequencyDisplay =>
        FreedvUseCurrentRadioFrequency
            ? $"{CurrentFrequencyHz / 1_000_000d:0.000000} MHz {ModeDisplay}"
            : FreedvSelectedFrequency;

    [ObservableProperty]
    private int freedvRxFrequencyOffsetHz;

    [ObservableProperty]
    private string freedvRxStatus = "FreeDV digital voice ready";

    [ObservableProperty]
    private string freedvSessionNotes = "Use USB-D/LSB-D with the official Codec2 FreeDV runtime. 700D is the practical first HF mode.";

    [ObservableProperty]
    private string freedvRuntimeStatus = "Codec2 runtime not loaded yet.";

    [ObservableProperty]
    private string freedvDecodedAudioStatus = "No decoded FreeDV speech yet.";

    [ObservableProperty]
    private string freedvSignalSummary = "Signal ---%  |  Sync ---%  |  SNR --.- dB";

    [ObservableProperty]
    private string freedvLastRadeCallsign = "None decoded";

    [ObservableProperty]
    private string freedvLastDecodedSpeechPath = "No decoded speech capture yet.";

    [ObservableProperty]
    private string freedvFieldNotes = "For live RADEV1: tune USB-D near the signal, Start RX, then adjust RX offset only if level is present but sync will not lock.";

    [ObservableProperty]
    private bool isFreedvTransmitting;

    [ObservableProperty]
    private string freedvPttButtonText = "PTT";

    public ObservableCollection<FreedvReporterStationItem> FreedvReporterStations { get; } = [];

    [ObservableProperty]
    private FreedvReporterStationItem? selectedFreedvReporterStation;

    [ObservableProperty]
    private string freedvReporterStatus = "Reporter disconnected";

    [ObservableProperty]
    private int freedvReporterStationCount;

    [ObservableProperty]
    private bool freedvReporterReportStation = true;

    [ObservableProperty]
    private bool freedvReporterReceiveOnly;

    [ObservableProperty]
    private string freedvReporterMessage = "Listening with ShackStack";






    [ObservableProperty]
    private string cwSendText = string.Empty;

    [ObservableProperty]
    private string cwTxStatus = "CW TX idle";

    [ObservableProperty]
    private bool isCwDecoderRunning;

    [ObservableProperty]
    private bool isCwSending;

    [RelayCommand]
    private async Task StartCwDecoderAsync()
    {
        if (_cwDecoderHost is null)
        {
            CwDecoderStatus = "CW decoder host unavailable";
            return;
        }

        var lowerLimit = Math.Clamp(CwLowerWpmLimit, 5, 60);
        var upperLimit = Math.Clamp(CwUpperWpmLimit, lowerLimit + 1, 80);
        var config = new CwDecoderConfiguration(
            Math.Clamp(CwPitchHz, 300, 1200),
            Math.Clamp(CwWpm, 5, 60),
            CwDecoderProfile,
            Math.Clamp(CwBandwidthHz, 40, 600),
            CwMatchedFilterEnabled,
            CwTrackingEnabled,
            Math.Clamp(CwTrackingRangeWpm, 1, 30),
            lowerLimit,
            upperLimit,
            CwSelectedAttack,
            CwSelectedDecay,
            CwSelectedNoiseCharacter,
            CwAutoToneSearchEnabled,
            CwAfcEnabled,
            Math.Clamp(CwToneSearchSpanHz, 50, 800),
            CwSelectedSquelch,
            CwSelectedSpacing);
        await _cwDecoderHost.ConfigureAsync(config, CancellationToken.None);
        await _cwDecoderHost.StartAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task StopCwDecoderAsync()
    {
        if (_cwDecoderHost is null)
        {
            return;
        }

        await _cwDecoderHost.StopAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task ResetCwDecoderAsync()
    {
        if (_cwDecoderHost is null)
        {
            return;
        }

        CwDecodedText = string.Empty;
        await _cwDecoderHost.ResetAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void ClearCwDecodedText()
    {
        CwDecodedText = string.Empty;
    }

    [RelayCommand]
    private void LoadCwMacro(string? label)
    {
        var text = ExpandCwMacro(label);
        if (string.IsNullOrWhiteSpace(text))
        {
            CwTxStatus = "No CW macro selected.";
            return;
        }

        CwSendText = text;
        CwTxStatus = $"Loaded CW macro: {text}";
    }

    [RelayCommand]
    private async Task SendCwMacroAsync(string? label)
    {
        LoadCwMacro(label);
        await QueueCwSendTextAsync();
    }

    [RelayCommand]
    private void StartRttyReceive()
    {
        if (_rttyDecoderHost is null)
        {
            RttyRxStatus = "RTTY decoder host unavailable";
            return;
        }

        _ = StartRttyReceiveCoreAsync();
    }

    [RelayCommand]
    private void StopRttyReceive()
    {
        if (_rttyDecoderHost is null)
        {
            return;
        }

        _ = _rttyDecoderHost.StopAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void ResetRttySession()
    {
        if (_rttyDecoderHost is not null)
        {
            _ = _rttyDecoderHost.ResetAsync(CancellationToken.None);
        }

        RttyRxStatus = "RTTY receiver ready";
        RttySessionNotes = "For IC-7300 audio RTTY, use USB-D/LSB-D rather than native RTTY mode. Tune the signal, select shift/baud, then start receive.";
        RttyTuneHelperSuggestion = "Start RX, then place an RTTY signal in the passband; helper will suggest Audio Hz.";
        RttySuggestedAudioCenterHz = 0;
        RttyDecodedText = string.Empty;
    }

    [RelayCommand]
    private void ClearRttyDecodedText() => RttyDecodedText = string.Empty;

    [RelayCommand]
    private void StartKeyboardReceive()
    {
        if (_keyboardModeDecoderHost is null)
        {
            KeyboardRxStatus = "Keyboard decoder host unavailable";
            return;
        }

        _ = StartKeyboardReceiveCoreAsync();
    }

    [RelayCommand]
    private void StopKeyboardReceive()
    {
        if (_keyboardModeDecoderHost is not null)
        {
            _ = _keyboardModeDecoderHost.StopAsync(CancellationToken.None);
        }
    }

    [RelayCommand]
    private void ResetKeyboardSession()
    {
        if (_keyboardModeDecoderHost is not null)
        {
            _ = _keyboardModeDecoderHost.ResetAsync(CancellationToken.None);
        }

        KeyboardRxStatus = "Keyboard modes ready";
        KeyboardSessionNotes = "PSK31/63 receive uses the fldigi-derived GPL sidecar. Use USB-D/LSB-D and place the trace near the selected audio center.";
        KeyboardTuneHelperSuggestion = "Start RX, tune the PSK trace into the passband, then apply the helper if AFC settles off-center.";
        KeyboardSuggestedAudioCenterHz = 0;
        KeyboardDecodedText = string.Empty;
    }

    [RelayCommand]
    private void ClearKeyboardDecodedText() => KeyboardDecodedText = string.Empty;

    [RelayCommand]
    private Task PrepareKeyboardTransmitAsync()
    {
        var text = KeyboardTransmitText.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _keyboardPreparedTransmitClip = null;
            KeyboardPreparedTransmitStatus = "Type PSK text before preparing TX audio.";
            KeyboardPreparedTransmitPath = "No prepared PSK WAV.";
            OnPropertyChanged(nameof(KeyboardHasPreparedTransmitClip));
            return Task.CompletedTask;
        }

        try
        {
            Directory.CreateDirectory(_keyboardTxDirectory);
            var mode = string.Equals(KeyboardSelectedMode, "BPSK63", StringComparison.OrdinalIgnoreCase)
                ? "BPSK63"
                : "BPSK31";
            var audioCenterHz = ParseKeyboardAudioCenterHz(KeyboardAudioCenterHz);
            var clip = PskBpskWaveformGenerator.Generate(mode, text, audioCenterHz);
            var timestamp = DateTime.Now;
            var safeMode = mode.ToLowerInvariant();
            var wavPath = Path.Combine(_keyboardTxDirectory, $"{timestamp:yyyyMMdd_HHmmss}_{safeMode}.wav");

            SstvReplyRenderer.WriteWaveFile(wavPath, clip);
            _keyboardPreparedTransmitClip = clip;
            _keyboardPreparedTransmitFingerprint = BuildKeyboardTransmitFingerprint();
            var durationSeconds = clip.PcmBytes.Length / (double)(clip.SampleRate * clip.Channels * 2);
            KeyboardPreparedTransmitStatus = $"Prepared {mode} TX WAV at {audioCenterHz:0} Hz audio center ({durationSeconds:0.0}s).";
            KeyboardPreparedTransmitPath = wavPath;
            OnPropertyChanged(nameof(KeyboardHasPreparedTransmitClip));
        }
        catch (Exception ex)
        {
            _keyboardPreparedTransmitClip = null;
            _keyboardPreparedTransmitFingerprint = null;
            KeyboardPreparedTransmitStatus = $"PSK TX prepare failed: {ex.Message}";
            KeyboardPreparedTransmitPath = "No prepared PSK WAV.";
            OnPropertyChanged(nameof(KeyboardHasPreparedTransmitClip));
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SendKeyboardTransmitAsync()
    {
        if (_keyboardTxSendInFlight)
        {
            return;
        }

        _keyboardTxSendInFlight = true;
        var pttRaised = false;
        var txAudioStarted = false;

        try
        {
            var interlockError = ValidateKeyboardLiveTransmitInterlock();
            if (interlockError is not null)
            {
                KeyboardPreparedTransmitStatus = interlockError;
                VoiceTxStatus = "TX audio idle";
                RadioStatusSummary = interlockError;
                return;
            }

            if (KeyboardUseCurrentRadioFrequency)
            {
                await SetRadioForKeyboardAudioDataModeAsync().ConfigureAwait(false);
            }
            else
            {
                await TuneRadioForKeyboardAsync(KeyboardSelectedFrequency).ConfigureAwait(false);
            }

            var route = BuildCurrentAudioRoute();
            var liveClip = WithLeadingSilence(_keyboardPreparedTransmitClip!, 250);
            var clipDurationMs = Math.Max(250, (int)Math.Ceiling(
                liveClip.PcmBytes.Length / (double)(liveClip.SampleRate * liveClip.Channels * 2) * 1000.0));
            var mode = string.Equals(KeyboardSelectedMode, "BPSK63", StringComparison.OrdinalIgnoreCase) ? "BPSK63" : "BPSK31";
            var text = KeyboardTransmitText.Trim();

            await _audioService!.StartTransmitPcmAsync(route, liveClip, CancellationToken.None).ConfigureAwait(false);
            txAudioStarted = true;
            await _radioService!.SetPttAsync(true, CancellationToken.None).ConfigureAwait(false);
            pttRaised = true;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                KeyboardPreparedTransmitStatus = $"Sending {mode} on-air: {text}";
                VoiceTxStatus = "PSK TX audio live";
                RadioStatusSummary = $"Keyboard TX live  |  {mode}  |  {KeyboardAudioCenterHz} Hz audio";
            });

            await Task.Delay(clipDurationMs + 150, CancellationToken.None).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                KeyboardPreparedTransmitStatus = $"PSK TX sent: {mode}. Prepared WAV retained.";
                VoiceTxStatus = "TX audio idle";
                RadioStatusSummary = "Keyboard TX complete";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                KeyboardPreparedTransmitStatus = $"PSK TX failed: {ex.Message}";
                VoiceTxStatus = "TX audio idle";
                RadioStatusSummary = $"Keyboard TX failed: {ex.Message}";
            });
        }
        finally
        {
            try
            {
                if (pttRaised && _radioService is not null)
                {
                    await _radioService.SetPttAsync(false, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
                // Leave shutdown paths to force PTT low if CI-V readback is in a bad mood.
            }

            try
            {
                if (txAudioStarted && _audioService is not null)
                {
                    await _audioService.StopTransmitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            _keyboardTxSendInFlight = false;
        }
    }

    [RelayCommand]
    private async Task ApplyKeyboardTuneHelperAsync()
    {
        if (KeyboardSuggestedAudioCenterHz <= 0)
        {
            KeyboardTuneHelperSuggestion = "No PSK tuning estimate is available yet. Start RX and wait for DCD/AFC to settle.";
            return;
        }

        KeyboardAudioCenterHz = $"{KeyboardSuggestedAudioCenterHz:0.0}";
        KeyboardTuneHelperSuggestion = $"Applied Audio Hz {KeyboardAudioCenterHz}.";
        if (_keyboardModeDecoderHost is not null)
        {
            await ConfigureKeyboardDecoderAsync();
            KeyboardTuneHelperSuggestion = $"Applied Audio Hz {KeyboardAudioCenterHz}; decoder reconfigured live.";
        }
    }

    [RelayCommand]
    private void StartFreedvReceive()
    {
        if (_freedvDigitalVoiceHost is null)
        {
            FreedvRxStatus = "FreeDV host unavailable";
            return;
        }

        _ = StartFreedvReceiveCoreAsync();
    }

    [RelayCommand]
    private void StopFreedvReceive()
    {
        _ = StopFreedvReceiveCoreAsync();
    }

    [RelayCommand]
    private void ResetFreedvSession()
    {
        if (_freedvDigitalVoiceHost is not null)
        {
            _ = _freedvDigitalVoiceHost.ResetAsync(CancellationToken.None);
        }

        FreedvRxStatus = "FreeDV digital voice ready";
        FreedvSessionNotes = "Use USB-D/LSB-D with the official Codec2 FreeDV runtime. 700D is the practical first HF mode.";
        FreedvRuntimeStatus = "Codec2 runtime not loaded yet.";
        FreedvDecodedAudioStatus = "No decoded FreeDV speech yet.";
        FreedvSignalSummary = "Signal ---%  |  Sync ---%  |  SNR --.- dB";
        FreedvLastRadeCallsign = "None decoded";
    }

    [RelayCommand]
    private async Task StartFreedvTransmitAsync()
    {
        if (_freedvDigitalVoiceHost is null)
        {
            FreedvRxStatus = "FreeDV host unavailable";
            return;
        }

        if (_radioService is null || CanConnect)
        {
            FreedvRxStatus = "FreeDV TX blocked: radio is not connected.";
            return;
        }

        if (_audioService is null || SelectedMicDevice is null || SelectedTxDevice is null)
        {
            FreedvRxStatus = "FreeDV TX blocked: mic/TX audio device is not configured.";
            return;
        }

        if (IsFreedvTransmitting)
        {
            return;
        }

        try
        {
            FreedvRxStatus = "Starting FreeDV TX...";
            if (FreedvUseCurrentRadioFrequency)
            {
                await SetRadioForFreedvAudioDataModeAsync().ConfigureAwait(false);
            }
            else
            {
                await TuneRadioForFreedvAsync(FreedvSelectedFrequency).ConfigureAwait(false);
            }

            var frequencyLabel = FreedvUseCurrentRadioFrequency
                ? "Current radio frequency"
                : FreedvSelectedFrequency;
            await _freedvDigitalVoiceHost.ConfigureAsync(
                new FreedvDigitalVoiceConfiguration(
                    FreedvSelectedMode,
                    frequencyLabel,
                    FreedvUseCurrentRadioFrequency,
                    FreedvRxFrequencyOffsetHz,
                    FormatCallsign(SettingsCallsign)),
                CancellationToken.None).ConfigureAwait(false);

            if (!_freedvPttRequested)
            {
                FreedvRxStatus = "FreeDV TX cancelled before keying.";
                return;
            }

            await _freedvDigitalVoiceHost.StartTransmitAsync(BuildCurrentAudioRoute(), CancellationToken.None).ConfigureAwait(false);
            if (_freedvReporterService is not null)
            {
                await UpdateFreedvReporterFrequencyAsync().ConfigureAwait(false);
                await _freedvReporterService.UpdateTransmitAsync(FreedvSelectedMode, true, CancellationToken.None).ConfigureAwait(false);
            }

            if (!_freedvPttRequested)
            {
                await StopFreedvTransmitSafelyAsync().ConfigureAwait(false);
                FreedvRxStatus = "FreeDV TX cancelled before keying.";
                return;
            }

            await Task.Delay(150).ConfigureAwait(false);
            if (!_freedvPttRequested)
            {
                await StopFreedvTransmitSafelyAsync().ConfigureAwait(false);
                FreedvRxStatus = "FreeDV TX cancelled before keying.";
                return;
            }

            await _radioService.SetPttAsync(true, CancellationToken.None).ConfigureAwait(false);
            IsFreedvTransmitting = true;
            FreedvRxStatus = $"FreeDV TX live ({FreedvSelectedMode})";
            FreedvSignalSummary = $"TX active  |  Mode {FreedvSelectedMode}  |  EOO call {FormatCallsign(SettingsCallsign)}";
            FreedvSessionNotes = string.Equals(FreedvSelectedMode, "RADEV1", StringComparison.OrdinalIgnoreCase)
                ? "Speak normally; RADEV1 TX will append your callsign in the end-of-over frame when you stop transmit."
                : "Speak normally; Codec2 FreeDV is encoding mic audio to the radio TX device.";
        }
        catch (Exception ex)
        {
            FreedvRxStatus = $"FreeDV TX failed: {ex.Message}";
            await StopFreedvTransmitSafelyAsync().ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task StopFreedvTransmitAsync()
    {
        await StopFreedvTransmitSafelyAsync().ConfigureAwait(false);
        FreedvRxStatus = "FreeDV TX stopped";
    }

    public async Task SetFreedvPttPressedAsync(bool isPressed)
    {
        _freedvPttRequested = isPressed;
        if (isPressed)
        {
            await StartFreedvTransmitAsync().ConfigureAwait(false);
        }
        else
        {
            await StopFreedvTransmitSafelyAsync().ConfigureAwait(false);
            FreedvRxStatus = "FreeDV TX stopped";
        }
    }

    [RelayCommand]
    private void OpenFreedvDebugFolder()
    {
        try
        {
            Directory.CreateDirectory(_freedvDebugDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _freedvDebugDirectory,
                UseShellExecute = true,
            });
            FreedvDecodedAudioStatus = $"Opened FreeDV captures: {_freedvDebugDirectory}";
        }
        catch (Exception ex)
        {
            FreedvDecodedAudioStatus = $"Could not open FreeDV captures: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ConnectFreedvReporterAsync()
    {
        if (_freedvReporterService is null)
        {
            FreedvReporterStatus = "FreeDV Reporter service unavailable";
            return;
        }

        var config = new FreedvReporterConfiguration(
            "qso.freedv.org",
            FormatCallsign(SettingsCallsign),
            SettingsGridSquare.Trim().ToUpperInvariant(),
            "ShackStack 1.0",
            FreedvReporterReportStation,
            FreedvReporterReceiveOnly);
        try
        {
            await _freedvReporterService.ConnectAsync(config, CancellationToken.None).ConfigureAwait(false);
            await UpdateFreedvReporterFrequencyAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FreedvReporterStatus = $"FreeDV Reporter connect failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectFreedvReporterAsync()
    {
        if (_freedvReporterService is not null)
        {
            await _freedvReporterService.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task SendFreedvReporterMessageAsync()
    {
        if (_freedvReporterService is not null)
        {
            await _freedvReporterService.UpdateMessageAsync(FreedvReporterMessage, CancellationToken.None).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task RefreshFreedvReporterFrequencyAsync()
    {
        if (_freedvReporterService is null)
        {
            FreedvReporterStatus = "FreeDV Reporter service unavailable";
            return;
        }

        if (FreedvUseCurrentRadioFrequency)
        {
            RefreshFromRadioSnapshot();
        }

        await UpdateFreedvReporterFrequencyAsync().ConfigureAwait(false);
        FreedvReporterStatus = $"Reporter frequency updated: {FreedvActiveFrequencyDisplay}";
    }

    [RelayCommand]
    private void OpenFreedvReporterWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://qso.freedv.org/",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            FreedvReporterStatus = $"Could not open FreeDV Reporter website: {ex.Message}";
        }
    }

    [RelayCommand]
    private void UseFreedvReporterStationForLog()
    {
        if (SelectedFreedvReporterStation is null)
        {
            LongwaveLogStatus = "Select a FreeDV Reporter station first.";
            return;
        }

        UseRigForModeLongwaveLog("FREEDV", "59", "59");
        LongwaveLogCallsign = SelectedFreedvReporterStation.Callsign;
        if (SelectedFreedvReporterStation.FrequencyHz is > 0)
        {
            LongwaveLogFrequencyKhz = $"{SelectedFreedvReporterStation.FrequencyHz.Value / 1000d:0.0}";
            LongwaveLogBand = DeriveBandFromFrequencyKhz(SelectedFreedvReporterStation.FrequencyHz.Value / 1000d);
        }

        LongwaveLogGridSquare = SelectedFreedvReporterStation.GridSquare;
        LongwaveLogStatus = $"Loaded FreeDV Reporter station {SelectedFreedvReporterStation.Callsign} for logging.";
    }

    [RelayCommand]
    private async Task WorkFreedvReporterSpotAsync()
    {
        if (SelectedFreedvReporterStation is null)
        {
            FreedvReporterStatus = "Select a FreeDV Reporter station first.";
            return;
        }

        if (SelectedFreedvReporterStation.FrequencyHz is not > 0)
        {
            FreedvReporterStatus = $"Reporter station {SelectedFreedvReporterStation.Callsign} has no usable frequency.";
            return;
        }

        var station = SelectedFreedvReporterStation;
        var modeLabel = NormalizeFreedvReporterMode(station.ModeText);
        if (!string.IsNullOrWhiteSpace(modeLabel))
        {
            FreedvSelectedMode = modeLabel;
        }

        FreedvUseCurrentRadioFrequency = true;
        var radioMode = GetFreedvDataModeForFrequency(station.FrequencyHz.Value);
        try
        {
            if (_radioService is null || CanConnect)
            {
                FreedvReporterStatus = $"Work spot loaded for {station.Callsign}; radio is not connected, tune manually to {FormatFrequencyMHz(station.FrequencyHz.Value)} MHz {FormatModeDisplay(radioMode)}.";
                FreedvSessionNotes = $"Selected reporter spot {station.Callsign} {FormatFrequencyMHz(station.FrequencyHz.Value)} MHz {FreedvSelectedMode}.";
                return;
            }

            await _radioService.SetModeAsync(radioMode, CancellationToken.None).ConfigureAwait(false);
            await _radioService.SetFrequencyAsync(station.FrequencyHz.Value, CancellationToken.None).ConfigureAwait(false);
            RadioStatusSummary = $"FreeDV reporter spot tuned: {station.FrequencyHz.Value:N0} Hz {FormatModeDisplay(radioMode)}";
            FreedvReporterStatus = $"Ready to work {station.Callsign} on {FormatFrequencyMHz(station.FrequencyHz.Value)} MHz {FreedvSelectedMode}.";
            FreedvSessionNotes = $"Reporter spot selected: {station.Callsign} {station.GridSquare}  |  {FormatFrequencyMHz(station.FrequencyHz.Value)} MHz {FormatModeDisplay(radioMode)}  |  {FreedvSelectedMode}.";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"FreeDV reporter tune failed: {ex.Message}";
            FreedvReporterStatus = $"Could not tune reporter spot {station.Callsign}: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyRttyTuneHelperAsync()
    {
        if (RttySuggestedAudioCenterHz <= 0)
        {
            RttyTuneHelperSuggestion = "No RTTY tone pair has been detected yet. Start RX and tune until the two tones are visible/audible.";
            return;
        }

        RttyAudioCenterHz = $"{RttySuggestedAudioCenterHz:0}";
        RttyTuneHelperSuggestion = $"Applied Audio Hz {RttyAudioCenterHz}.";

        if (_rttyDecoderHost is null)
        {
            return;
        }

        var (shiftHz, baudRate) = ParseRttyProfile(RttySelectedProfile);
        var frequencyLabel = RttyDecodeCurrentRadioFrequency
            ? "Current radio frequency"
            : RttySelectedFrequency;
        var config = new RttyDecoderConfiguration(
            RttySelectedProfile,
            shiftHz,
            baudRate,
            frequencyLabel,
            RttySuggestedAudioCenterHz,
            RttyReversePolarity);
        await _rttyDecoderHost.ConfigureAsync(config, CancellationToken.None);
        RttyTuneHelperSuggestion = $"Applied Audio Hz {RttyAudioCenterHz}; decoder reconfigured live.";
    }

    private async Task StartRttyReceiveCoreAsync()
    {
        if (_rttyDecoderHost is null)
        {
            return;
        }

        if (RttyDecodeCurrentRadioFrequency)
        {
            await SetRadioForRttyAudioDataModeAsync();
        }
        else
        {
            await TuneRadioForRttyAsync(RttySelectedFrequency);
        }

        var (shiftHz, baudRate) = ParseRttyProfile(RttySelectedProfile);
        var frequencyLabel = RttyDecodeCurrentRadioFrequency
            ? "Current radio frequency"
            : RttySelectedFrequency;
        var audioCenterHz = ParseRttyAudioCenterHz(RttyAudioCenterHz);
        var config = new RttyDecoderConfiguration(RttySelectedProfile, shiftHz, baudRate, frequencyLabel, audioCenterHz, RttyReversePolarity);
        await _rttyDecoderHost.ConfigureAsync(config, CancellationToken.None);
        await _rttyDecoderHost.StartAsync(CancellationToken.None);
        RttySessionNotes = "RTTY audio decoder running. IC-7300 should be in USB-D or LSB-D; native RTTY is for the rig's FSK/RTTY path.";
    }

    private async Task StartKeyboardReceiveCoreAsync()
    {
        if (_keyboardModeDecoderHost is null)
        {
            return;
        }

        if (KeyboardUseCurrentRadioFrequency)
        {
            await SetRadioForKeyboardAudioDataModeAsync();
        }
        else
        {
            await TuneRadioForKeyboardAsync(KeyboardSelectedFrequency);
        }

        await ConfigureKeyboardDecoderAsync();
        await _keyboardModeDecoderHost.StartAsync(CancellationToken.None);
        KeyboardSessionNotes = "PSK receiver running. Use USB-D/LSB-D and tune the waterfall trace near the selected audio center.";
    }

    private async Task ConfigureKeyboardDecoderAsync()
    {
        if (_keyboardModeDecoderHost is null)
        {
            return;
        }

        var frequencyLabel = KeyboardUseCurrentRadioFrequency
            ? "Current radio frequency"
            : KeyboardSelectedFrequency;
        var config = new KeyboardModeDecoderConfiguration(
            KeyboardSelectedMode,
            frequencyLabel,
            ParseKeyboardAudioCenterHz(KeyboardAudioCenterHz),
            KeyboardReversePolarity);
        await _keyboardModeDecoderHost.ConfigureAsync(config, CancellationToken.None);
    }

    private string? ValidateKeyboardLiveTransmitInterlock()
    {
        if (_radioService is null || CanConnect)
        {
            return "PSK TX blocked: radio is not connected.";
        }

        if (_audioService is null)
        {
            return "PSK TX blocked: audio service unavailable.";
        }

        if (SelectedTxDevice is null)
        {
            return "PSK TX blocked: no TX audio device configured.";
        }

        if (_keyboardPreparedTransmitClip is null)
        {
            return "PSK TX blocked: prepare TX WAV first.";
        }

        if (!string.Equals(_keyboardPreparedTransmitFingerprint, BuildKeyboardTransmitFingerprint(), StringComparison.Ordinal))
        {
            return "PSK TX blocked: prepared audio is stale. Press Prepare TX WAV again.";
        }

        return null;
    }

    private string BuildKeyboardTransmitFingerprint()
    {
        var mode = string.Equals(KeyboardSelectedMode, "BPSK63", StringComparison.OrdinalIgnoreCase)
            ? "BPSK63"
            : "BPSK31";
        return string.Join('|',
            mode,
            KeyboardTransmitText.Trim(),
            ParseKeyboardAudioCenterHz(KeyboardAudioCenterHz).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
    }

    partial void OnKeyboardTransmitTextChanged(string value) => MarkKeyboardPreparedTransmitStale();

    partial void OnKeyboardSelectedModeChanged(string value) => MarkKeyboardPreparedTransmitStale();

    partial void OnKeyboardAudioCenterHzChanged(string value) => MarkKeyboardPreparedTransmitStale();

    private void MarkKeyboardPreparedTransmitStale()
    {
        if (_keyboardPreparedTransmitClip is null)
        {
            return;
        }

        KeyboardPreparedTransmitStatus = "PSK TX text/mode/audio changed; prepare TX WAV again.";
        OnPropertyChanged(nameof(KeyboardHasPreparedTransmitClip));
    }

    private async Task StartFreedvReceiveCoreAsync()
    {
        if (_freedvDigitalVoiceHost is null)
        {
            return;
        }

        if (_audioService is null)
        {
            FreedvRxStatus = "FreeDV RX blocked: audio service unavailable.";
            return;
        }

        if (SelectedRxDevice is null)
        {
            FreedvRxStatus = "FreeDV RX blocked: no RX audio device configured.";
            return;
        }

        if (GetSelectedFreedvMonitorDevice() is null)
        {
            FreedvRxStatus = "FreeDV RX blocked: no FreeDV speech output configured.";
            FreedvDecodedAudioStatus = "Choose a FreeDV speech output, or configure PC RX Audio as the fallback, before starting FreeDV RX.";
            return;
        }

        if (FreedvUseCurrentRadioFrequency)
        {
            await SetRadioForFreedvAudioDataModeAsync();
        }
        else
        {
            await TuneRadioForFreedvAsync(FreedvSelectedFrequency);
        }

        var frequencyLabel = FreedvUseCurrentRadioFrequency
            ? "Current radio frequency"
            : FreedvSelectedFrequency;
        try
        {
            await _audioService.StartReceiveCaptureAsync(BuildCurrentAudioRoute(), CancellationToken.None);
            AudioMonitorState = CanStopAudio ? AudioMonitorState : "Decoder capture";
            CanStopAudio = true;
            await ApplyFreedvSpeechVolumeAsync(FreedvSpeechVolumePercent);
        }
        catch (Exception ex)
        {
            FreedvRxStatus = $"FreeDV RX blocked: decoder audio capture did not start ({ex.Message}).";
            AudioMonitorState = $"Error: {ex.Message}";
            CanStopAudio = false;
            return;
        }

            FreedvDecodedAudioStatus = $"FreeDV decoder capture is active; decoded speech will play on {GetSelectedFreedvMonitorDevice()?.FriendlyName} at {FreedvSpeechVolumePercent}%.";
        await _freedvDigitalVoiceHost.ConfigureAsync(
            new FreedvDigitalVoiceConfiguration(
                FreedvSelectedMode,
                frequencyLabel,
                FreedvUseCurrentRadioFrequency,
                FreedvRxFrequencyOffsetHz,
                FormatCallsign(SettingsCallsign)),
            CancellationToken.None);
        await _freedvDigitalVoiceHost.StartAsync(CancellationToken.None);
        await UpdateFreedvReporterFrequencyAsync().ConfigureAwait(false);
        FreedvSessionNotes = $"FreeDV receiver running on {frequencyLabel}. RX offset {FreedvRxFrequencyOffsetHz:+0;-0;0} Hz.";
    }

    private async Task StopFreedvReceiveCoreAsync()
    {
        try
        {
            if (_freedvDigitalVoiceHost is not null)
            {
                await _freedvDigitalVoiceHost.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FreedvRxStatus = "FreeDV receiver stopped";
                FreedvDecodedAudioStatus = "FreeDV decoder stopped; normal RX audio monitor is unchanged.";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FreedvRxStatus = $"FreeDV stop failed: {ex.Message}";
            });
        }
    }

    private async Task TuneRadioForFreedvAsync(string frequencyLabel)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        if (!TryParseUiFrequencyHz(frequencyLabel, out var hz))
        {
            return;
        }

        try
        {
            var mode = frequencyLabel.Contains("LSB", StringComparison.OrdinalIgnoreCase)
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            await _radioService.SetModeAsync(mode, CancellationToken.None);
            await _radioService.SetFrequencyAsync(hz, CancellationToken.None);
            RadioStatusSummary = $"FreeDV tuned: {hz:N0} Hz {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"FreeDV tune failed: {ex.Message}";
        }
    }

    private static RadioMode GetFreedvDataModeForFrequency(long hz) =>
        hz < 10_000_000 ? RadioMode.LsbData : RadioMode.UsbData;

    private string NormalizeFreedvReporterMode(string? modeText)
    {
        if (string.IsNullOrWhiteSpace(modeText) || string.Equals(modeText, "---", StringComparison.OrdinalIgnoreCase))
        {
            return "RADEV1";
        }

        var normalized = modeText.Trim().ToUpperInvariant();
        return FreedvModeOptions.FirstOrDefault(mode => string.Equals(mode, normalized, StringComparison.OrdinalIgnoreCase))
            ?? "RADEV1";
    }

    private static string FormatFrequencyMHz(long hz) =>
        (hz / 1_000_000d).ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);

    private async Task SetRadioForFreedvAudioDataModeAsync()
    {
        if (_radioService is null || CanConnect)
        {
            FreedvSessionNotes = "FreeDV RX using current radio frequency. Set the IC-7300 to USB-D/LSB-D.";
            return;
        }

        try
        {
            var currentMode = _radioService.CurrentState.Mode;
            var mode = currentMode == RadioMode.Lsb || currentMode == RadioMode.LsbData
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            if (currentMode != mode)
            {
                await _radioService.SetModeAsync(mode, CancellationToken.None);
            }

            RadioStatusSummary = $"FreeDV RX using current radio frequency in {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"FreeDV mode set failed: {ex.Message}";
            FreedvSessionNotes = "FreeDV RX will still start, but manually set the IC-7300 to USB-D/LSB-D.";
        }
    }

    private async Task StopFreedvTransmitSafelyAsync()
    {
        _freedvPttRequested = false;
        try
        {
            if (_radioService is not null)
            {
                await _radioService.SetPttAsync(false, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        try
        {
            if (_freedvDigitalVoiceHost is not null)
            {
                await _freedvDigitalVoiceHost.StopTransmitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        IsFreedvTransmitting = false;
        if (_freedvReporterService is not null)
        {
            await _freedvReporterService.UpdateTransmitAsync(FreedvSelectedMode, false, CancellationToken.None).ConfigureAwait(false);
        }
    }

    partial void OnIsFreedvTransmittingChanged(bool value)
    {
        FreedvPttButtonText = value ? "Release PTT" : "PTT";
    }

    private async Task TuneRadioForRttyAsync(string frequencyLabel)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        if (!TryParseUiFrequencyHz(frequencyLabel, out var hz))
        {
            return;
        }

        try
        {
            var mode = frequencyLabel.Contains("LSB", StringComparison.OrdinalIgnoreCase)
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            await _radioService.SetModeAsync(mode, CancellationToken.None);
            await _radioService.SetFrequencyAsync(hz, CancellationToken.None);
            RadioStatusSummary = $"RTTY tuned: {hz:N0} Hz {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"RTTY tune failed: {ex.Message}";
        }
    }

    private async Task SetRadioForRttyAudioDataModeAsync()
    {
        if (_radioService is null || CanConnect)
        {
            RttySessionNotes = "RTTY RX using current radio frequency. Set the IC-7300 to USB-D/LSB-D for audio RTTY.";
            return;
        }

        try
        {
            var currentMode = _radioService.CurrentState.Mode;
            var mode = currentMode == RadioMode.Lsb || currentMode == RadioMode.LsbData
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            if (currentMode != mode)
            {
                await _radioService.SetModeAsync(mode, CancellationToken.None);
            }

            RadioStatusSummary = $"RTTY RX using current radio frequency in {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"RTTY mode set failed: {ex.Message}";
            RttySessionNotes = "RTTY RX will still start, but manually set the IC-7300 to USB-D/LSB-D for audio RTTY.";
        }
    }

    private async Task TuneRadioForKeyboardAsync(string frequencyLabel)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        if (!TryParseUiFrequencyHz(frequencyLabel, out var hz))
        {
            return;
        }

        try
        {
            var mode = frequencyLabel.Contains("LSB", StringComparison.OrdinalIgnoreCase)
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            await _radioService.SetModeAsync(mode, CancellationToken.None);
            await _radioService.SetFrequencyAsync(hz, CancellationToken.None);
            RadioStatusSummary = $"Keyboard mode tuned: {hz:N0} Hz {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Keyboard mode tune failed: {ex.Message}";
        }
    }

    private async Task SetRadioForKeyboardAudioDataModeAsync()
    {
        if (_radioService is null || CanConnect)
        {
            KeyboardSessionNotes = "Keyboard RX using current radio frequency. Set the IC-7300 to USB-D/LSB-D for PSK.";
            return;
        }

        try
        {
            var currentMode = _radioService.CurrentState.Mode;
            var mode = currentMode == RadioMode.Lsb || currentMode == RadioMode.LsbData
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            if (currentMode != mode)
            {
                await _radioService.SetModeAsync(mode, CancellationToken.None);
            }

            RadioStatusSummary = $"Keyboard RX using current radio frequency in {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Keyboard mode set failed: {ex.Message}";
            KeyboardSessionNotes = "Keyboard RX will still start, but manually set the IC-7300 to USB-D/LSB-D.";
        }
    }

    [RelayCommand]
    private async Task QueueCwSendTextAsync()
    {
        var text = CwSendText.Trim();
        if (_radioService is null || CanConnect)
        {
            CwTxStatus = "Radio not connected";
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            CwTxStatus = "CW TX idle";
            return;
        }

        if (IsCwSending)
        {
            CwTxStatus = "CW send already in progress";
            return;
        }

        var normalized = text.ToUpperInvariant();
        CwSendText = normalized;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            CwTxStatus = "Nothing sendable in CW text";
            return;
        }

        _cwSendCts?.Cancel();
        _cwSendCts?.Dispose();
        _cwSendCts = new CancellationTokenSource();
        var token = _cwSendCts.Token;

        IsCwSending = true;
        CwTxStatus = $"Queueing {normalized.Length} chars to rig keyer...";
        _cwSendTask = RunCwSendAsync(normalized, token);
        await _cwSendTask;
    }

    [RelayCommand]
    private async Task StopCwSendAsync()
    {
        _cwSendCts?.Cancel();
        if (_radioService is not null)
        {
            try
            {
                await _radioService.StopCwSendAsync(CancellationToken.None);
            }
            catch
            {
            }
        }

        if (_cwSendTask is not null)
        {
            try
            {
                await _cwSendTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [RelayCommand]
    private async Task ApplyCwRigSettingsAsync()
    {
        if (_radioService is null || CanConnect)
        {
            CwTxStatus = "Radio not connected";
            return;
        }

        try
        {
            await _radioService.SetCwPitchAsync(CwPitchHz, CancellationToken.None);
            await _radioService.SetCwKeyerSpeedAsync(CwWpm, CancellationToken.None);
            _cwRigSettingsDirty = false;
            CwTxStatus = $"CW rig settings applied: {CwWpm} WPM, {CwPitchHz} Hz";
        }
        catch (Exception ex)
        {
            CwTxStatus = $"CW settings error: {ex.Message}";
        }
    }

    partial void OnCwPitchHzChanged(int value)
    {
        if (!_isUpdatingCwRigSettingsFromRadio)
        {
            _cwRigSettingsDirty = true;
        }
    }

    partial void OnCwWpmChanged(int value)
    {
        if (!_isUpdatingCwRigSettingsFromRadio)
        {
            _cwRigSettingsDirty = true;
        }
    }

    private async Task RunCwSendAsync(string text, CancellationToken ct)
    {
        var radioService = _radioService;
        if (radioService is null)
        {
            IsCwSending = false;
            CwTxStatus = "Radio not connected";
            return;
        }

        try
        {
            if (_audioService is not null)
            {
                try
                {
                    await _audioService.StopTransmitAsync(ct);
                }
                catch
                {
                }
            }

            if (SelectedMode != RadioMode.Cw)
            {
                await radioService.SetModeAsync(RadioMode.Cw, ct);
            }

            CwTxStatus = $"Sending: {text}";
            await radioService.SendCwTextAsync(text, ct);
            CwTxStatus = "CW send complete";
        }
        catch (OperationCanceledException)
        {
            if (_radioService is not null)
            {
                try
                {
                    await _radioService.StopCwSendAsync(CancellationToken.None);
                }
                catch
                {
                }
            }

            CwTxStatus = "CW send stopped";
        }
        catch (Exception ex)
        {
            if (_radioService is not null)
            {
                try
                {
                    await _radioService.StopCwSendAsync(CancellationToken.None);
                }
                catch
                {
                }
            }

            CwTxStatus = $"CW send error: {ex.Message}";
        }
        finally
        {
            IsCwSending = false;
        }
    }

    partial void OnSettingsShowExperimentalCwChanged(bool value)
    {
        ShowCwPanel = value;
        UpdateModePanelsVisibility();
    }

    partial void OnFreedvSpeechVolumePercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (clamped != value)
        {
            FreedvSpeechVolumePercent = clamped;
            return;
        }

        _ = ApplyFreedvSpeechVolumeAsync(clamped);
        ScheduleRuntimeUiStateSave();
    }

    partial void OnSelectedFreedvMonitorDeviceChanged(AudioDeviceInfo? value)
    {
        FreedvMonitorDeviceDisplay = DescribeFreedvMonitorDevice();
    }

    private async Task ApplyFreedvSpeechVolumeAsync(int volumePercent)
    {
        if (_audioService is null)
        {
            return;
        }

        try
        {
            await _audioService.SetDecodedMonitorVolumeAsync(volumePercent / 100f, CancellationToken.None);
        }
        catch
        {
        }
    }

    private AudioRoute BuildFreedvMonitorAudioRoute() => new(
        SelectedRxDevice?.DeviceId ?? string.Empty,
        SelectedTxDevice?.DeviceId ?? string.Empty,
        SelectedMicDevice?.DeviceId ?? string.Empty,
        GetSelectedFreedvMonitorDevice()?.DeviceId ?? string.Empty);

    private AudioDeviceInfo? GetSelectedFreedvMonitorDevice() =>
        SelectedFreedvMonitorDevice;

    private string DescribeFreedvMonitorDevice()
    {
        if (SelectedFreedvMonitorDevice is null)
        {
            return "Not configured";
        }

        if (SelectedMonitorDevice is not null
            && string.Equals(SelectedFreedvMonitorDevice.DeviceId, SelectedMonitorDevice.DeviceId, StringComparison.Ordinal))
        {
            return $"{SelectedFreedvMonitorDevice.FriendlyName} (same physical output as PC RX)";
        }

        return SelectedFreedvMonitorDevice.FriendlyName;
    }

    private async Task PlayFreedvSpeechFrameAsync(Pcm16AudioClip clip)
    {
        var duration = clip.PcmBytes.Length / (double)(clip.SampleRate * clip.Channels * 2);
            var debugPath = SaveFreedvDecodedSpeechFrame(clip);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FreedvLastDecodedSpeechPath = debugPath ?? "Decoded speech was not saved.";
                FreedvDecodedAudioStatus = string.IsNullOrWhiteSpace(debugPath)
                    ? $"Decoded speech frame: {duration:0.00}s @ {clip.SampleRate} Hz"
                    : $"Decoded speech frame: {duration:0.00}s @ {clip.SampleRate} Hz | saved {Path.GetFileName(debugPath)}";
        });

        if (_audioService is null)
        {
            return;
        }

        try
        {
            await _audioService.PlayDecodedMonitorPcmAsync(BuildFreedvMonitorAudioRoute(), clip, CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FreedvLastDecodedSpeechPath = debugPath ?? "Decoded speech was not saved.";
                FreedvDecodedAudioStatus = string.IsNullOrWhiteSpace(debugPath)
                    ? $"Decoded speech playing: {duration:0.00}s @ {clip.SampleRate} Hz"
                    : $"Decoded speech playing: {duration:0.00}s @ {clip.SampleRate} Hz | saved {debugPath}";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FreedvDecodedAudioStatus = $"FreeDV speech monitor failed: {ex.Message}";
            });
        }
    }

    private void RebuildFreedvReporterStations(IReadOnlyList<FreedvReporterStation> stations)
    {
        var selectedSid = SelectedFreedvReporterStation?.Sid;
        var nextItems = stations
            .Select(station => new FreedvReporterStationItem(
                station.Sid,
                station.Callsign,
                station.GridSquare,
                station.FrequencyHz is > 0 ? $"{station.FrequencyHz.Value / 1_000_000d:0.000000} MHz" : "---",
                string.IsNullOrWhiteSpace(station.Mode) ? "---" : station.Mode,
                station.IsTransmitting ? "TX" : (station.ReceiveOnly ? "RX only" : "Idle"),
                string.IsNullOrWhiteSpace(station.LastHeardCallsign)
                    ? "---"
                    : station.LastHeardSnrDb is double snr
                        ? $"{station.LastHeardCallsign} {snr:+0;-0;0} dB {station.LastHeardMode}"
                        : station.LastHeardCallsign,
                station.Message,
                FormatReporterUpdatedText(station.LastUpdatedUtc),
                station.FrequencyHz,
                station.IsTransmitting))
            .ToArray();

        for (var index = 0; index < nextItems.Length; index++)
        {
            var nextItem = nextItems[index];
            var existing = FreedvReporterStations.FirstOrDefault(item =>
                string.Equals(item.Sid, nextItem.Sid, StringComparison.Ordinal));
            if (existing is null)
            {
                FreedvReporterStations.Insert(Math.Min(index, FreedvReporterStations.Count), nextItem);
                continue;
            }

            existing.UpdateFrom(nextItem);
            var currentIndex = FreedvReporterStations.IndexOf(existing);
            if (currentIndex >= 0 && currentIndex != index)
            {
                FreedvReporterStations.Move(currentIndex, index);
            }
        }

        for (var index = FreedvReporterStations.Count - 1; index >= 0; index--)
        {
            var existing = FreedvReporterStations[index];
            if (!nextItems.Any(item => string.Equals(item.Sid, existing.Sid, StringComparison.Ordinal)))
            {
                FreedvReporterStations.RemoveAt(index);
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedSid))
        {
            SelectedFreedvReporterStation = FreedvReporterStations.FirstOrDefault(item =>
                string.Equals(item.Sid, selectedSid, StringComparison.Ordinal));
        }
    }

    private async Task UpdateFreedvReporterFrequencyAsync()
    {
        if (_freedvReporterService is null)
        {
            return;
        }

        var hz = CurrentFrequencyHz;
        if (!FreedvUseCurrentRadioFrequency && TryParseUiFrequencyHz(FreedvSelectedFrequency, out var selectedHz))
        {
            hz = selectedHz;
        }

        if (hz > 0)
        {
            await _freedvReporterService.UpdateFrequencyAsync(hz, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task ReportFreedvDecodedCallsignAsync(string callsign, string mode, double snrDb)
    {
        if (_freedvReporterService is null)
        {
            return;
        }

        var normalizedCallsign = NormalizeFreedvRadeCallsign(callsign);
        if (normalizedCallsign is null)
        {
            return;
        }

        await _freedvReporterService.ReportReceiveAsync(normalizedCallsign, mode, snrDb, CancellationToken.None).ConfigureAwait(false);
    }

    private static string? NormalizeFreedvRadeCallsign(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign))
        {
            return null;
        }

        var normalized = FormatCallsign(callsign);
        if (normalized.Length is < 3 or > 12)
        {
            return null;
        }

        var hasLetter = false;
        var hasDigit = false;
        var lastWasSlash = false;
        for (var i = 0; i < normalized.Length; i++)
        {
            var ch = normalized[i];
            var isLetter = ch is >= 'A' and <= 'Z';
            var isDigit = ch is >= '0' and <= '9';
            if (isLetter)
            {
                hasLetter = true;
            }
            else if (isDigit)
            {
                hasDigit = true;
            }
            else if (ch == '/')
            {
                if (i == 0 || i == normalized.Length - 1 || lastWasSlash)
                {
                    return null;
                }
            }
            else
            {
                return null;
            }

            lastWasSlash = ch == '/';
        }

        return hasLetter && hasDigit ? normalized : null;
    }

    private static string FormatReporterUpdatedText(DateTimeOffset? updatedUtc)
    {
        if (updatedUtc is null)
        {
            return "---";
        }

        var age = DateTimeOffset.UtcNow - updatedUtc.Value;
        if (age.TotalSeconds < 90)
        {
            return $"{Math.Max(0, (int)age.TotalSeconds)}s";
        }

        if (age.TotalMinutes < 90)
        {
            return $"{Math.Max(1, (int)age.TotalMinutes)}m";
        }

        return updatedUtc.Value.ToLocalTime().ToString("HH:mm");
    }

    partial void OnFreedvUseCurrentRadioFrequencyChanged(bool value)
    {
        OnPropertyChanged(nameof(FreedvActiveFrequencyDisplay));
        _ = UpdateFreedvReporterFrequencyAsync();
    }

    partial void OnFreedvSelectedFrequencyChanged(string value)
    {
        OnPropertyChanged(nameof(FreedvActiveFrequencyDisplay));
        _ = UpdateFreedvReporterFrequencyAsync();
    }

    partial void OnModeDisplayChanged(string value)
    {
        OnPropertyChanged(nameof(FreedvActiveFrequencyDisplay));
    }

    private string? SaveFreedvDecodedSpeechFrame(Pcm16AudioClip clip)
    {
        try
        {
            var mode = FreedvSelectedMode.ToLowerInvariant();
            var path = Path.Combine(_freedvDebugDirectory, $"last_{mode}_speech.wav");
            WritePcm16Wave(path, clip);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static void WritePcm16Wave(string path, Pcm16AudioClip clip)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataSize = clip.PcmBytes.Length;
        var blockAlign = (short)(clip.Channels * sizeof(short));
        var byteRate = clip.SampleRate * blockAlign;

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)clip.Channels);
        writer.Write(clip.SampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        writer.Write(clip.PcmBytes);
    }

    private static (int ShiftHz, double BaudRate) ParseRttyProfile(string profileLabel) => profileLabel switch
    {
        "170 Hz / 75 baud" => (170, 75.0),
        "425 Hz / 45.45 baud" => (425, 45.45),
        _ => (170, 45.45),
    };

    private static double ParseRttyAudioCenterHz(string value)
    {
        if (double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hz))
        {
            return Math.Clamp(hz, 300.0, 3200.0);
        }

        return 1700.0;
    }

    private static double ParseKeyboardAudioCenterHz(string value)
    {
        if (double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hz))
        {
            return Math.Clamp(hz, 300.0, 3200.0);
        }

        return 1000.0;
    }


    private string ExpandCwMacro(string? label)
    {
        var myCall = FormatCallsign(SettingsCallsign);
        if (string.IsNullOrWhiteSpace(myCall))
        {
            myCall = "<MYCALL>";
        }

        var toCall = FormatCallsign(LongwaveLogCallsign);
        if (string.IsNullOrWhiteSpace(toCall))
        {
            toCall = "<CALL>";
        }

        var park = string.IsNullOrWhiteSpace(LongwaveLogParkReference)
            ? SelectedVoiceLongwavePotaSpot?.ParkReference ?? SelectedLongwavePotaSpot?.ParkReference ?? string.Empty
            : LongwaveLogParkReference.Trim().ToUpperInvariant();

        var text = (label ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "CQ" => $"CQ CQ DE {myCall} {myCall} K",
            "DE" => $"{toCall} DE {myCall}",
            "RST" => $"{toCall} DE {myCall} 599 599",
            "TU" => $"{toCall} DE {myCall} TU",
            "73" => $"{toCall} DE {myCall} 73",
            "POTA" => string.IsNullOrWhiteSpace(park)
                ? $"CQ POTA DE {myCall} {myCall} K"
                : $"CQ POTA DE {myCall} {myCall} {park} K",
            _ => string.Empty,
        };

        return text
            .Replace("%MYCALL", myCall, StringComparison.OrdinalIgnoreCase)
            .Replace("%TOCALL", toCall, StringComparison.OrdinalIgnoreCase)
            .Replace("%RST", "599", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }
}
