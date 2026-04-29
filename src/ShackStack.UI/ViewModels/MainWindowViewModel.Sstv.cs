using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShackStack.Core.Abstractions.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;

namespace ShackStack.UI.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private IReadOnlyList<string> sstvModeOptions =
    [
        "Auto Detect",
        "Lock Martin M1",
        "Lock Martin M2",
        "Lock Scottie 1",
        "Lock Scottie 2",
        "Lock Robot 36",
        "Lock PD 120"
    ];

    [ObservableProperty]
    private string sstvSelectedMode = "Auto Detect";

    [ObservableProperty]
    private IReadOnlyList<string> sstvFrequencyOptions =
    [
        "14.230 MHz USB-D",
        "14.233 MHz USB-D",
        "7.171 MHz LSB-D",
        "3.845 MHz LSB-D"
    ];

    [ObservableProperty]
    private string sstvSelectedFrequency = "14.230 MHz USB-D";

    [ObservableProperty]
    private string sstvRxStatus = "SSTV receiver ready";

    [ObservableProperty]
    private string sstvImageStatus = "No image captured yet";

    [ObservableProperty]
    private string sstvSessionNotes = SstvIdleSessionNotes;

    [ObservableProperty]
    private string sstvDecodedFskIdCallsign = string.Empty;

    [ObservableProperty]
    private Bitmap? sstvPreviewBitmap;

    [ObservableProperty]
    private bool sstvHasPreview;

    public bool SstvShowPlaceholder => !SstvHasPreview;

    [ObservableProperty]
    private ObservableCollection<SstvImageItem> sstvReceivedImages = [];

    [ObservableProperty]
    private SstvImageItem? selectedSstvReceivedImage;

    [ObservableProperty]
    private ObservableCollection<SstvImageItem> sstvReplyImages = [];

    [ObservableProperty]
    private SstvImageItem? selectedSstvReplyBaseImage;

    [ObservableProperty]
    private ObservableCollection<SstvOverlayItemViewModel> sstvReplyOverlayItems = [];

    [ObservableProperty]
    private SstvOverlayItemViewModel? selectedSstvReplyOverlayItem;

    [ObservableProperty]
    private ObservableCollection<SstvImageOverlayItemViewModel> sstvReplyImageOverlayItems = [];

    [ObservableProperty]
    private SstvImageOverlayItemViewModel? selectedSstvReplyImageOverlayItem;

    [ObservableProperty]
    private ObservableCollection<SstvTemplateItem> sstvReplyLayoutTemplates = [];

    [ObservableProperty]
    private SstvTemplateItem? selectedSstvReplyLayoutTemplate;

    [ObservableProperty]
    private string sstvReplyTemplateName = "Default Reply";

    [ObservableProperty]
    private string sstvReplyTemplateStatus = "Save a layout template to reuse overlay positioning later.";

    [ObservableProperty]
    private string? sstvReplyPresetKind;

    [ObservableProperty]
    private IReadOnlyList<SstvReplyLayoutPreset> sstvReplyLayoutPresets =
    [
        new("CQ Card", "cq"),
        new("QSL + RX", "qsl-rx"),
        new("Signal Report", "report"),
        new("TNX / 73", "73"),
        new("Station ID", "id"),
    ];

    [ObservableProperty]
    private IReadOnlyList<string> sstvReplyTemplates =
    [
        "CQ SSTV DE %m",
        "%m 599 %tocall",
        "QSL SSTV - TNX DE %m",
        "SSTV REPORT: RSV 595",
        "TNX QSO - 73 DE %m",
        "%m %g",
    ];

    [ObservableProperty]
    private IReadOnlyList<string> sstvTxModeHints =
    [
        "Martin/Scottie: common live QSOs and repeaters.",
        "Robot: short, robust exchanges when time matters.",
        "PD: higher-detail picture modes; slower but clean.",
    ];

    [ObservableProperty]
    private IReadOnlyList<string> sstvTxModeOptions =
    [
        "Martin 1",
        "Martin 2",
        "Scottie 1",
        "Scottie 2",
        "Scottie DX",
        "Robot 24",
        "Robot 36",
        "Robot 72",
        "PD 50",
        "PD 90",
        "PD 120",
        "PD 160",
        "PD 180",
        "PD 240",
        "PD 290",
    ];

    [ObservableProperty]
    private string sstvSelectedTxMode = "Martin 1";

    [ObservableProperty]
    private bool sstvTxCwIdEnabled;

    [ObservableProperty]
    private bool sstvTxFskIdEnabled = true;

    [ObservableProperty]
    private string sstvTxCwIdText = "DE %m";

    [ObservableProperty]
    private int sstvTxCwIdFrequencyHz = 1000;

    [ObservableProperty]
    private int sstvTxCwIdWpm = 28;

    [ObservableProperty]
    private string sstvTransmitStatus = "Prepare a reply image to stage SSTV TX.";

    [ObservableProperty]
    private string sstvPreparedTransmitPath = "No prepared SSTV TX artifact.";

    [ObservableProperty]
    private Bitmap? sstvPreparedTransmitBitmap;

    [ObservableProperty]
    private string sstvPreparedTransmitImagePath = "No prepared TX image.";

    [ObservableProperty]
    private string sstvPreparedTransmitSummary = "No prepared SSTV TX image/audio.";

    [ObservableProperty]
    private bool sstvTxIsSending;

    public bool SstvHasPreparedTransmitPreview => SstvPreparedTransmitBitmap is not null;

    public bool SstvHasPreparedTransmitClip => _sstvPreparedTransmitClip is not null;

    public Bitmap? SstvSelectedReceivedBitmap => SelectedSstvReceivedImage?.Bitmap;

    public string SstvSelectedReceivedPath => SelectedSstvReceivedImage?.Path ?? "No saved image selected";

    public Bitmap? SstvReplyPreviewBitmap => SelectedSstvReplyBaseImage?.Bitmap;

    public double SstvReplyCanvasWidth => SelectedSstvReplyBaseImage?.Bitmap.PixelSize.Width ?? 320;

    public double SstvReplyCanvasHeight => SelectedSstvReplyBaseImage?.Bitmap.PixelSize.Height ?? 256;

    public bool SstvReplyHasBaseImage => SelectedSstvReplyBaseImage is not null;

    public bool SstvReplyShowPlaceholder => !SstvReplyHasBaseImage;

    public string SstvReceivedFolderPath => _sstvReceivedDirectory;

    public string SstvReplyFolderPath => _sstvReplyDirectory;

    public string SstvTemplateFolderPath => _sstvTemplateDirectory;

    public IReadOnlyList<string> SstvReplyFontOptions => ["Segoe UI", "Arial", "Consolas", "Georgia", "Tahoma", "Verdana"];

    [RelayCommand]
    private void StartSstvReceive()
    {
        if (_sstvDecoderHost is null)
        {
            SstvRxStatus = "SSTV decoder host unavailable";
            return;
        }

        _ = StartSstvReceiveCoreAsync();
    }

    [RelayCommand]
    private void StopSstvReceive()
    {
        if (_sstvDecoderHost is null)
        {
            return;
        }

        _ = _sstvDecoderHost.StopAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void ForceStartSstvReceive()
    {
        if (_sstvDecoderHost is null)
        {
            SstvRxStatus = "SSTV decoder host unavailable";
            return;
        }

        _ = ForceStartSstvReceiveCoreAsync();
    }

    [RelayCommand]
    private void ResetSstvSession()
    {
        if (_sstvDecoderHost is not null)
        {
            _ = _sstvDecoderHost.ResetAsync(CancellationToken.None);
        }

        SstvRxStatus = "SSTV receiver ready";
        SstvImageStatus = "No image captured yet";
        SstvSessionNotes = SstvIdleSessionNotes;
        SstvDecodedFskIdCallsign = string.Empty;
        UpdateSstvPreview(null);
    }

    [RelayCommand]
    private void RefreshSstvArchive()
    {
        LoadSstvArchiveImages();
    }


    [RelayCommand]
    private void ApplySstvReplyTemplate(string template)
    {
        if (!string.IsNullOrWhiteSpace(template) && SelectedSstvReplyOverlayItem is not null)
        {
            SstvReplyPresetKind = null;
            SelectedSstvReplyOverlayItem.Text = ExpandSstvReplyMacro(template);
            SstvTransmitStatus = "Reply text changed; prepare TX when ready.";
        }
    }

    [RelayCommand]
    [SupportedOSPlatform("windows")]
    private async Task PrepareSstvReplyTransmitAsync()
    {
        if (_sstvTransmitService is null)
        {
            SstvTransmitStatus = "Native SSTV TX builder unavailable";
            return;
        }

        if (SelectedSstvReplyBaseImage is null)
        {
            SstvTransmitStatus = "Choose a reply base image first";
            return;
        }

        try
        {
            DeletePreparedSstvTransmitArtifacts();
            var timestamp = DateTime.Now;
            Directory.CreateDirectory(_sstvTxDirectory);
            var stem = $"{timestamp:yyyyMMdd_HHmmss}_{SstvSelectedTxMode.ToLowerInvariant().Replace(' ', '_')}";
            var pngPath = Path.Combine(_sstvTxDirectory, $"{stem}.png");
            var wavPath = Path.Combine(_sstvTxDirectory, $"{stem}.wav");
            var preparedFingerprint = BuildSstvTransmitFingerprint();
            var preparedMode = SstvSelectedTxMode;
            var transmitOptions = BuildSstvTransmitOptions();
            var preparedTextOverlays = SstvReplyOverlayItems
                .Select(item => new SstvOverlayItemViewModel
                {
                    Text = ExpandSstvReplyMacro(item.Text),
                    X = item.X,
                    Y = item.Y,
                    FontSize = item.FontSize,
                    FontFamilyName = item.FontFamilyName,
                    Red = item.Red,
                    Green = item.Green,
                    Blue = item.Blue
                })
                .ToArray();
            var rgb24 = SstvReplyRenderer.RenderRgb24(
                SelectedSstvReplyBaseImage.Path,
                preparedTextOverlays,
                SstvReplyImageOverlayItems,
                pngPath,
                out var width,
                out var height);

            _sstvPreparedTransmitClip = await _sstvTransmitService
                .BuildTransmitClipAsync(
                    preparedMode,
                    rgb24,
                    width,
                    height,
                    transmitOptions,
                    CancellationToken.None)
                .ConfigureAwait(false);

            SstvReplyRenderer.WriteWaveFile(wavPath, _sstvPreparedTransmitClip);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _sstvPreparedTransmitFingerprint = preparedFingerprint;
                _sstvPreparedTransmitMode = preparedMode;
                _sstvPreparedTransmitImageFile = pngPath;
                _sstvPreparedTransmitWaveFile = wavPath;
                var idParts = new List<string>();
                if (transmitOptions.FskIdEnabled)
                {
                    idParts.Add($"FSKID {transmitOptions.FskIdCallsign}");
                }

                if (transmitOptions.CwIdEnabled)
                {
                    idParts.Add($"CW ID {transmitOptions.CwIdText} @ {transmitOptions.CwIdFrequencyHz} Hz/{transmitOptions.CwIdWpm} WPM");
                }

                _sstvPreparedTransmitCwIdSummary = idParts.Count == 0 ? null : string.Join(" + ", idParts);
                SstvPreparedTransmitPath = $"{pngPath}  |  {wavPath}";
                SstvPreparedTransmitImagePath = pngPath;
                SstvPreparedTransmitBitmap = new Bitmap(pngPath);
                OnPropertyChanged(nameof(SstvHasPreparedTransmitPreview));
                OnPropertyChanged(nameof(SstvHasPreparedTransmitClip));
                _sstvPreparedTransmitDurationSeconds = _sstvPreparedTransmitClip.PcmBytes.Length / (double)(_sstvPreparedTransmitClip.SampleRate * _sstvPreparedTransmitClip.Channels * 2);
                var idSummary = string.Join(
                    string.Empty,
                    transmitOptions.FskIdEnabled ? " + FSKID" : string.Empty,
                    transmitOptions.CwIdEnabled ? " + CWID" : string.Empty);
                SstvTransmitStatus = $"Prepared {preparedMode}{idSummary} TX ({_sstvPreparedTransmitDurationSeconds:0.0}s)";
                RefreshPreparedSstvTransmitSummary();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ClearPreparedSstvTransmit($"SSTV TX prepare failed: {ex.Message}");
            });
        }
    }

    private SstvTransmitOptions BuildSstvTransmitOptions()
    {
        var stationCallsign = string.IsNullOrWhiteSpace(SettingsCallsign)
            ? "CALL"
            : SettingsCallsign.Trim().ToUpperInvariant();
        var cwidText = (SstvTxCwIdText ?? string.Empty)
            .Replace("%m", stationCallsign, StringComparison.OrdinalIgnoreCase)
            .Replace("%tocall", SstvDecodedFskIdCallsign, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return new SstvTransmitOptions(
            SstvTxCwIdEnabled,
            cwidText,
            SstvTxCwIdFrequencyHz,
            SstvTxCwIdWpm,
            SstvTxFskIdEnabled,
            stationCallsign);
    }

    private string BuildSstvTransmitFingerprint()
    {
        var overlays = string.Join(
            "|",
            SstvReplyOverlayItems.Select(static item =>
                $"{item.Text}\u001f{item.X:0.###}\u001f{item.Y:0.###}\u001f{item.FontSize:0.###}\u001f{item.FontFamilyName}\u001f{item.ColorHex}"));
        var imageOverlays = string.Join(
            "|",
            SstvReplyImageOverlayItems.Select(static item =>
                $"{item.Path}\u001f{item.X:0.###}\u001f{item.Y:0.###}\u001f{item.Width:0.###}\u001f{item.Height:0.###}"));

        return string.Join(
            "\u001e",
            SelectedSstvReplyBaseImage?.Path ?? string.Empty,
            SstvSelectedTxMode,
            SstvTxCwIdEnabled,
            SstvTxCwIdText ?? string.Empty,
            SstvTxCwIdFrequencyHz,
            SstvTxCwIdWpm,
            SstvTxFskIdEnabled,
            SstvDecodedFskIdCallsign,
            overlays,
            imageOverlays);
    }

    private void ClearPreparedSstvTransmit(string status)
    {
        DeletePreparedSstvTransmitArtifacts();
        _sstvPreparedTransmitClip = null;
        _sstvPreparedTransmitFingerprint = null;
        _sstvPreparedTransmitMode = null;
        _sstvPreparedTransmitCwIdSummary = null;
        _sstvPreparedTransmitDurationSeconds = 0;
        SstvPreparedTransmitPath = "No prepared SSTV TX artifact.";
        SstvPreparedTransmitImagePath = "No prepared TX image.";
        SstvPreparedTransmitSummary = "No prepared SSTV TX image/audio.";
        SstvPreparedTransmitBitmap = null;
        OnPropertyChanged(nameof(SstvHasPreparedTransmitPreview));
        OnPropertyChanged(nameof(SstvHasPreparedTransmitClip));
        SstvTransmitStatus = status;
    }

    private void DeletePreparedSstvTransmitArtifacts()
    {
        SstvPreparedTransmitBitmap = null;
        foreach (var path in new[] { _sstvPreparedTransmitImageFile, _sstvPreparedTransmitWaveFile })
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best effort only: a preview or audio device may still be releasing the file.
            }
        }

        _sstvPreparedTransmitImageFile = null;
        _sstvPreparedTransmitWaveFile = null;
    }

    private void RefreshPreparedSstvTransmitSummary()
    {
        if (_sstvPreparedTransmitClip is null)
        {
            SstvPreparedTransmitSummary = "No prepared SSTV TX image/audio.";
            return;
        }

        var stale = string.Equals(_sstvPreparedTransmitFingerprint, BuildSstvTransmitFingerprint(), StringComparison.Ordinal)
            ? "ready"
            : "stale - prepare again before sending";
        var cwid = string.IsNullOrWhiteSpace(_sstvPreparedTransmitCwIdSummary)
            ? "CW ID off"
            : _sstvPreparedTransmitCwIdSummary;
        var route = SelectedTxDevice is null ? "No TX audio device selected" : $"TX route: {SelectedTxDevice.FriendlyName}";
        SstvPreparedTransmitSummary = $"{stale}: {_sstvPreparedTransmitMode ?? SstvSelectedTxMode}  |  {_sstvPreparedTransmitDurationSeconds:0.0}s  |  {cwid}  |  {route}";
    }

    private void AttachSstvReplyLayoutChangeTracking(ObservableCollection<SstvOverlayItemViewModel> items)
    {
        items.CollectionChanged += OnSstvReplyLayoutCollectionChanged;
        foreach (var item in items)
        {
            item.PropertyChanged += OnSstvReplyLayoutItemChanged;
        }
    }

    private void AttachSstvReplyLayoutChangeTracking(ObservableCollection<SstvImageOverlayItemViewModel> items)
    {
        items.CollectionChanged += OnSstvReplyLayoutCollectionChanged;
        foreach (var item in items)
        {
            item.PropertyChanged += OnSstvReplyLayoutItemChanged;
        }
    }

    private void OnSstvReplyLayoutCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is SstvOverlayItemViewModel textItem)
                {
                    textItem.PropertyChanged += OnSstvReplyLayoutItemChanged;
                }
                else if (item is SstvImageOverlayItemViewModel imageItem)
                {
                    imageItem.PropertyChanged += OnSstvReplyLayoutItemChanged;
                }
            }
        }

        MarkSstvReplyLayoutDirty();
    }

    private void OnSstvReplyLayoutItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkSstvReplyLayoutDirty();
    }

    private void MarkSstvReplyLayoutDirty()
    {
        RefreshPreparedSstvTransmitSummary();
        if (!SstvTxIsSending)
        {
            SstvTransmitStatus = "Reply layout changed; prepare TX when ready.";
        }
    }

    [RelayCommand]
    [SupportedOSPlatform("windows")]
    private async Task SendSstvReplyTransmitAsync()
    {
        if (_sstvTxSendInFlight)
        {
            return;
        }

        _sstvTxSendInFlight = true;
        var txAudioStarted = false;
        var pttRaised = false;
        _sstvTxCts?.Cancel();
        _sstvTxCts?.Dispose();
        _sstvTxCts = new CancellationTokenSource();
        var token = _sstvTxCts.Token;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SstvTxIsSending = true;
                SstvTransmitStatus = $"Checking prepared {SstvSelectedTxMode} TX...";
            });

            if (_sstvPreparedTransmitClip is null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SstvTransmitStatus = "SSTV TX blocked: prepare TX first.";
                });
                return;
            }

            if (!string.Equals(_sstvPreparedTransmitFingerprint, BuildSstvTransmitFingerprint(), StringComparison.Ordinal))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RefreshPreparedSstvTransmitSummary();
                    SstvTransmitStatus = "SSTV TX blocked: prepared image/audio is stale. Press Prepare TX again.";
                });
                return;
            }

            var interlockError = ValidateSstvLiveTransmitInterlock();
            if (interlockError is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SstvTransmitStatus = interlockError;
                    VoiceTxStatus = "TX audio idle";
                    RadioStatusSummary = interlockError;
                });
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RefreshPreparedSstvTransmitSummary();
                SstvTransmitStatus = $"Keying radio for {_sstvPreparedTransmitMode ?? SstvSelectedTxMode}...";
            });

            await TuneRadioForSstvAsync(SstvSelectedFrequency, strict: true).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            var route = BuildCurrentAudioRoute();
            var clip = WithLeadingSilence(_sstvPreparedTransmitClip, 250);
            var clipDurationMs = Math.Max(500, (int)Math.Ceiling(
                clip.PcmBytes.Length / (double)(clip.SampleRate * clip.Channels * 2) * 1000.0));

            await _audioService!.StartTransmitPcmAsync(route, clip, token).ConfigureAwait(false);
            txAudioStarted = true;
            await _radioService!.SetPttAsync(true, token).ConfigureAwait(false);
            pttRaised = true;
            await VerifySstvPttRaisedAsync(token).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                VoiceTxStatus = "SSTV TX audio live";
                SstvTransmitStatus = $"Sending {_sstvPreparedTransmitMode ?? SstvSelectedTxMode} on-air";
                RadioStatusSummary = $"SSTV TX live  |  {_sstvPreparedTransmitMode ?? SstvSelectedTxMode}  |  {SstvSelectedFrequency}";
            });

            await Task.Delay(clipDurationMs + 150, token).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ClearPreparedSstvTransmit($"SSTV TX sent: {_sstvPreparedTransmitMode ?? SstvSelectedTxMode}. Prepared artifacts discarded.");
            });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ClearPreparedSstvTransmit("SSTV TX stopped. Prepared artifacts discarded.");
                VoiceTxStatus = "TX audio idle";
                RadioStatusSummary = "SSTV TX stopped.";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SstvTransmitStatus = $"SSTV TX failed: {ex.Message}";
                VoiceTxStatus = "TX audio idle";
                RadioStatusSummary = $"SSTV TX failed: {ex.Message}";
            });
        }
        finally
        {
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

            try
            {
                if (pttRaised && _radioService is not null)
                {
                    await _radioService.SetPttAsync(false, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SstvTxIsSending = false;
                VoiceTxStatus = "TX audio idle";
                RefreshPreparedSstvTransmitSummary();
            });

            _sstvTxSendInFlight = false;
        }
    }

    [RelayCommand]
    private async Task StopSstvReplyTransmitAsync()
    {
        _sstvTxCts?.Cancel();

        try
        {
            if (_audioService is not null)
            {
                await _audioService.StopTransmitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            if (_radioService is not null)
            {
                await _radioService.SetPttAsync(false, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SstvTxIsSending = false;
            VoiceTxStatus = "TX audio idle";
            ClearPreparedSstvTransmit("SSTV TX stop requested. Prepared artifacts discarded.");
            RadioStatusSummary = "SSTV TX stop requested.";
        });
    }

    [RelayCommand]
    private void SaveSstvReplyLayoutTemplate()
    {
        Directory.CreateDirectory(_sstvTemplateDirectory);

        var normalizedName = string.IsNullOrWhiteSpace(SstvReplyTemplateName)
            ? $"Reply Template {DateTime.Now:yyyyMMdd_HHmmss}"
            : SstvReplyTemplateName.Trim();
        var fileName = string.Concat(normalizedName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var fullPath = Path.Combine(_sstvTemplateDirectory, $"{fileName}.json");

        var payload = new SstvOverlayTemplateFile(
            normalizedName,
            SstvReplyOverlayItems.Select(static item => new SstvOverlayTemplateItemFile(
                item.Text,
                item.X,
                item.Y,
                item.FontSize,
                item.FontFamilyName,
                item.ColorHex)).ToArray(),
            SstvReplyImageOverlayItems.Select(static item => new SstvImageOverlayTemplateItemFile(
                item.Label,
                item.Path,
                item.X,
                item.Y,
                item.Width,
                item.Height)).ToArray(),
            SstvReplyPresetKind);

        try
        {
            File.WriteAllText(fullPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            var presetText = string.IsNullOrWhiteSpace(SstvReplyPresetKind) ? string.Empty : $" ({SstvReplyPresetKind})";
            SstvReplyTemplateStatus = $"Saved template '{normalizedName}'{presetText}";
            LoadSstvArchiveImages();
            SelectedSstvReplyLayoutTemplate = SstvReplyLayoutTemplates.FirstOrDefault(t =>
                string.Equals(t.Path, fullPath, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            SstvReplyTemplateStatus = $"Template save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void LoadSstvReplyLayoutTemplate()
    {
        if (SelectedSstvReplyLayoutTemplate is null)
        {
            SstvReplyTemplateStatus = "Choose a saved template first";
            return;
        }

        try
        {
            var json = File.ReadAllText(SelectedSstvReplyLayoutTemplate.Path);
            var payload = JsonSerializer.Deserialize<SstvOverlayTemplateFile>(json);
            if (payload is null || (payload.Items.Count == 0 && (payload.ImageItems?.Count ?? 0) == 0))
            {
                SstvReplyTemplateStatus = "Template was empty";
                return;
            }

            var items = payload.Items.Select(static item => new SstvOverlayItemViewModel
            {
                Text = item.Text,
                X = item.X,
                Y = item.Y,
                FontSize = item.FontSize,
                FontFamilyName = item.FontFamily,
            }).ToArray();

            foreach (var (overlay, saved) in items.Zip(payload.Items))
            {
                overlay.SetColorFromHex(saved.Color);
            }

            SstvReplyOverlayItems = new ObservableCollection<SstvOverlayItemViewModel>(items);
            SelectedSstvReplyOverlayItem = SstvReplyOverlayItems.FirstOrDefault();
            var missingImages = new List<string>();
            var imageItems = new List<SstvImageOverlayItemViewModel>();
            foreach (var item in payload.ImageItems ?? [])
            {
                if (!File.Exists(item.Path))
                {
                    missingImages.Add(item.Label);
                    continue;
                }

                try
                {
                    imageItems.Add(new SstvImageOverlayItemViewModel
                    {
                        Label = item.Label,
                        Path = item.Path,
                        Bitmap = new Bitmap(item.Path),
                        X = item.X,
                        Y = item.Y,
                        Width = item.Width,
                        Height = item.Height,
                    });
                }
                catch
                {
                    missingImages.Add(item.Label);
                }
            }

            SstvReplyImageOverlayItems = new ObservableCollection<SstvImageOverlayItemViewModel>(imageItems);
            SelectedSstvReplyImageOverlayItem = SstvReplyImageOverlayItems.FirstOrDefault();
            SstvReplyTemplateName = payload.Name;
            SstvReplyPresetKind = payload.PresetKind;
            var presetText = string.IsNullOrWhiteSpace(payload.PresetKind) ? string.Empty : $" ({payload.PresetKind})";
            var missingText = missingImages.Count == 0
                ? string.Empty
                : $" Missing image overlays: {string.Join(", ", missingImages)}.";
            SstvReplyTemplateStatus = $"Loaded template '{payload.Name}'{presetText}.{missingText}";
            SstvTransmitStatus = "Reply layout changed; prepare TX when ready.";
        }
        catch (Exception ex)
        {
            SstvReplyTemplateStatus = $"Template load failed: {ex.Message}";
        }
    }

    public void ImportSstvReplyBaseImage(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            SstvReplyTemplateStatus = "Choose an existing image to import";
            return;
        }

        var extension = Path.GetExtension(sourcePath);
        var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bmp",
            ".png",
            ".jpg",
            ".jpeg",
        };
        if (!supportedExtensions.Contains(extension))
        {
            SstvReplyTemplateStatus = "Import supports BMP, PNG, JPG, and JPEG images";
            return;
        }

        try
        {
            var destination = SstvReplyArchiveStore.ImportReplyBaseImage(sourcePath, _sstvReplyDirectory);
            LoadSstvArchiveImages();
            SelectedSstvReplyBaseImage = SstvReplyImages.FirstOrDefault(item =>
                string.Equals(item.Path, destination, StringComparison.OrdinalIgnoreCase));
            SstvReplyTemplateStatus = $"Imported reply base '{Path.GetFileName(destination)}'";
            SstvTransmitStatus = "Reply base imported; choose a quick layout or prepare TX.";
        }
        catch (Exception ex)
        {
            SstvReplyTemplateStatus = $"Import failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DuplicateSelectedSstvReplyBaseImage()
    {
        if (SelectedSstvReplyBaseImage is null || !File.Exists(SelectedSstvReplyBaseImage.Path))
        {
            SstvReplyTemplateStatus = "Choose a reply base image to duplicate";
            return;
        }

        try
        {
            var destination = SstvReplyArchiveStore.DuplicateReplyBaseImage(SelectedSstvReplyBaseImage.Path, _sstvReplyDirectory);
            LoadSstvArchiveImages();
            SelectedSstvReplyBaseImage = SstvReplyImages.FirstOrDefault(item =>
                string.Equals(item.Path, destination, StringComparison.OrdinalIgnoreCase));
            SstvReplyTemplateStatus = $"Duplicated reply base '{Path.GetFileName(destination)}'";
            SstvTransmitStatus = "Reply base duplicated; prepare TX when ready.";
        }
        catch (Exception ex)
        {
            SstvReplyTemplateStatus = $"Duplicate failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ArchiveSelectedSstvReplyBaseImage()
    {
        if (SelectedSstvReplyBaseImage is null || !File.Exists(SelectedSstvReplyBaseImage.Path))
        {
            SstvReplyTemplateStatus = "Choose a reply base image to archive";
            return;
        }

        try
        {
            var archived = SstvReplyArchiveStore.ArchiveFile(SelectedSstvReplyBaseImage.Path, Path.Combine(_sstvReplyDirectory, "archived"));
            LoadSstvArchiveImages();
            SstvReplyTemplateStatus = $"Archived reply base '{Path.GetFileName(archived)}'";
            ClearPreparedSstvTransmit("Reply base archived; prepare TX when ready.");
        }
        catch (Exception ex)
        {
            SstvReplyTemplateStatus = $"Archive failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ArchiveSelectedSstvReplyLayoutTemplate()
    {
        if (SelectedSstvReplyLayoutTemplate is null || !File.Exists(SelectedSstvReplyLayoutTemplate.Path))
        {
            SstvReplyTemplateStatus = "Choose a template to archive";
            return;
        }

        try
        {
            var archived = SstvReplyArchiveStore.ArchiveFile(SelectedSstvReplyLayoutTemplate.Path, Path.Combine(_sstvTemplateDirectory, "archived"));
            LoadSstvArchiveImages();
            SstvReplyTemplateStatus = $"Archived template '{Path.GetFileNameWithoutExtension(archived)}'";
        }
        catch (Exception ex)
        {
            SstvReplyTemplateStatus = $"Template archive failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddSstvReplyOverlay(string? template = null)
    {
        SstvReplyPresetKind = null;
        var text = string.IsNullOrWhiteSpace(template)
            ? "QSL SSTV - TNX DE %m"
            : template.Trim();
        var item = new SstvOverlayItemViewModel
        {
            Text = ExpandSstvReplyMacro(text),
            X = 24 + (SstvReplyOverlayItems.Count * 12),
            Y = 24 + (SstvReplyOverlayItems.Count * 28),
            FontSize = 18,
            FontFamilyName = "Segoe UI",
        };
        SstvReplyOverlayItems.Add(item);
        SelectedSstvReplyOverlayItem = item;
        SstvReplyTemplateStatus = $"Added text box '{item.Text}'";
        SstvTransmitStatus = "Reply layout changed; prepare TX when ready.";
    }

    [RelayCommand]
    private void ReplyToSelectedSstvReceivedImage()
    {
        if (SelectedSstvReceivedImage is null)
        {
            SstvReplyTemplateStatus = "Choose a received image first";
            return;
        }

        if (SelectedSstvReplyBaseImage is null)
        {
            SelectedSstvReplyBaseImage = SstvReplyImages.FirstOrDefault();
        }

        if (SelectedSstvReplyBaseImage is null)
        {
            SstvReplyTemplateStatus = "Choose or import a reply base image first";
            return;
        }

        ApplySstvReplyLayoutPreset(new SstvReplyLayoutPreset("QSL + RX", "qsl-rx"));
        SstvReplyTemplateStatus = $"Reply layout staged for '{SelectedSstvReceivedImage.Label}'";
    }

    [RelayCommand]
    private void ApplySstvReplyLayoutPreset(SstvReplyLayoutPreset preset)
    {
        SstvReplyPresetKind = preset.Kind;
        SstvReplyOverlayItems.Clear();
        SstvReplyImageOverlayItems.Clear();

        switch (preset.Kind)
        {
            case "cq":
                SstvReplyTemplateName = "CQ SSTV";
                AddSstvReplyTextOverlay("CQ SSTV", 86, 48, 34, "#FFFFD166");
                AddSstvReplyTextOverlay("DE %m", 98, 104, 30, "#FFFFFFFF");
                AddSstvReplyTextOverlay("%g  |  %f", 66, 164, 18, "#FF9BE7FF");
                AddSstvReplyTextOverlay("PSE K", 130, 210, 18, "#FFC4C8D8");
                break;
            case "qsl-rx":
                SstvReplyTemplateName = "QSL With RX Thumbnail";
                AddSelectedSstvReceivedImageOverlay(24, 24, 118, 92);
                AddSstvReplyTextOverlay("QSL - TNX SSTV", 154, 38, 24, "#FFFFD166");
                AddSstvReplyTextOverlay("DE %m", 166, 88, 24, "#FFFFFFFF");
                AddSstvReplyTextOverlay("%g", 206, 128, 18, "#FF9BE7FF");
                AddSstvReplyTextOverlay("73!", 218, 186, 28, "#FFFFFFFF");
                break;
            case "report":
                SstvReplyTemplateName = "SSTV Signal Report";
                AddSstvReplyTextOverlay("SSTV REPORT", 78, 40, 28, "#FFFFD166");
                AddSstvReplyTextOverlay("RSV 595", 104, 100, 34, "#FFFFFFFF");
                AddSstvReplyTextOverlay("Good copy - 73", 78, 166, 20, "#FF9BE7FF");
                AddSstvReplyTextOverlay("DE %m", 110, 210, 20, "#FFC4C8D8");
                break;
            case "73":
                SstvReplyTemplateName = "TNX QSO 73";
                AddSstvReplyTextOverlay("TNX QSO", 92, 58, 34, "#FFFFD166");
                AddSstvReplyTextOverlay("73 DE %m", 78, 124, 28, "#FFFFFFFF");
                AddSstvReplyTextOverlay("%g", 132, 184, 20, "#FF9BE7FF");
                break;
            case "id":
            default:
                SstvReplyTemplateName = "Station ID";
                AddSstvReplyTextOverlay("%m", 92, 74, 42, "#FFFFFFFF");
                AddSstvReplyTextOverlay("%g", 128, 140, 24, "#FFFFD166");
                AddSstvReplyTextOverlay("%d %t", 94, 192, 18, "#FFC4C8D8");
                break;
        }

        SelectedSstvReplyOverlayItem = SstvReplyOverlayItems.FirstOrDefault();
        SelectedSstvReplyImageOverlayItem = SstvReplyImageOverlayItems.FirstOrDefault();
        SstvReplyTemplateStatus = $"Applied preset '{preset.Label}'";
        SstvTransmitStatus = "Reply layout changed; prepare TX when ready.";
    }

    private void AddSstvReplyTextOverlay(string text, double x, double y, double fontSize, string colorHex)
    {
        var item = new SstvOverlayItemViewModel
        {
            Text = ExpandSstvReplyMacro(text),
            X = x,
            Y = y,
            FontSize = fontSize,
            FontFamilyName = "Segoe UI",
        };
        item.SetColorFromHex(colorHex);
        SstvReplyOverlayItems.Add(item);
    }

    private bool AddSelectedSstvReceivedImageOverlay(double x, double y, double width, double height)
    {
        var image = SelectedSstvReceivedImage ?? SstvReceivedImages.FirstOrDefault();
        if (image is null)
        {
            return false;
        }

        var item = SstvImageOverlayItemViewModel.FromImage(image, SstvReplyImageOverlayItems.Count);
        item.X = x;
        item.Y = y;
        item.Width = width;
        item.Height = height;
        SstvReplyImageOverlayItems.Add(item);
        return true;
    }

    private string ExpandSstvReplyMacro(string value)
    {
        var stationCallsign = string.IsNullOrWhiteSpace(SettingsCallsign)
            ? "CALL"
            : SettingsCallsign.Trim().ToUpperInvariant();
        var toCall = string.IsNullOrWhiteSpace(SstvDecodedFskIdCallsign)
            ? "TOCALL"
            : SstvDecodedFskIdCallsign.Trim().ToUpperInvariant();
        var grid = string.IsNullOrWhiteSpace(SettingsGridSquare)
            ? "GRID"
            : SettingsGridSquare.Trim().ToUpperInvariant();
        var now = DateTime.Now;
        return (value ?? string.Empty)
            .Replace("%tocall", toCall, StringComparison.OrdinalIgnoreCase)
            .Replace("%m", stationCallsign, StringComparison.OrdinalIgnoreCase)
            .Replace("%g", grid, StringComparison.OrdinalIgnoreCase)
            .Replace("%f", SstvSelectedFrequency, StringComparison.OrdinalIgnoreCase)
            .Replace("%r", SelectedSstvReceivedImage?.Label ?? "RX IMAGE", StringComparison.OrdinalIgnoreCase)
            .Replace("%d", now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase)
            .Replace("%t", now.ToString("HH:mm"), StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void AddSstvReceivedThumbnail()
    {
        SstvReplyPresetKind = null;
        if (SelectedSstvReceivedImage is null)
        {
            SstvReplyTemplateStatus = "Choose a received image first";
            return;
        }

        AddSelectedSstvReceivedImageOverlay(24 + (SstvReplyImageOverlayItems.Count * 14), 24 + (SstvReplyImageOverlayItems.Count * 14), 112, 86);
        var item = SstvReplyImageOverlayItems.Last();
        SelectedSstvReplyImageOverlayItem = item;
        SstvReplyTemplateStatus = $"Added received thumbnail '{item.Label}'";
        SstvTransmitStatus = "Reply layout changed; prepare TX when ready.";
    }

    [RelayCommand]
    private void RemoveSstvReceivedThumbnail()
    {
        SstvReplyPresetKind = null;
        if (SelectedSstvReplyImageOverlayItem is null)
        {
            return;
        }

        var item = SelectedSstvReplyImageOverlayItem;
        SstvReplyImageOverlayItems.Remove(item);
        SelectedSstvReplyImageOverlayItem = SstvReplyImageOverlayItems.FirstOrDefault();
        SstvTransmitStatus = "Reply layout changed; prepare TX when ready.";
    }

    [RelayCommand]
    private void RemoveSstvReplyOverlay()
    {
        SstvReplyPresetKind = null;
        if (SelectedSstvReplyOverlayItem is null)
        {
            return;
        }

        var item = SelectedSstvReplyOverlayItem;
        SstvReplyOverlayItems.Remove(item);
        SelectedSstvReplyOverlayItem = SstvReplyOverlayItems.FirstOrDefault();
        SstvTransmitStatus = "Reply layout changed; prepare TX when ready.";
    }

    private async Task StartSstvReceiveCoreAsync()
    {
        if (_sstvDecoderHost is null)
        {
            return;
        }

        await TuneRadioForSstvAsync(SstvSelectedFrequency);
        var config = new SstvDecoderConfiguration(NormalizeSstvModeSelection(SstvSelectedMode), SstvSelectedFrequency);
        await _sstvDecoderHost.ConfigureAsync(config, CancellationToken.None);
        await _sstvDecoderHost.StartAsync(CancellationToken.None);
    }

    private async Task ForceStartSstvReceiveCoreAsync()
    {
        if (_sstvDecoderHost is null)
        {
            return;
        }

        await TuneRadioForSstvAsync(SstvSelectedFrequency);
        var config = new SstvDecoderConfiguration(NormalizeSstvModeSelection(SstvSelectedMode), SstvSelectedFrequency);
        await _sstvDecoderHost.ConfigureAsync(config, CancellationToken.None);
        await _sstvDecoderHost.StartAsync(CancellationToken.None);
        await _sstvDecoderHost.ForceStartAsync(CancellationToken.None);
        SstvRxStatus = $"Force-start requested for {config.Mode}";
        SstvSessionNotes = $"ShackStack SSTV native sidecar  |  Signal  ---%  |  Mode {config.Mode}  |  FSKID none";
    }

    private async Task TuneRadioForSstvAsync(string frequencyLabel, bool strict = false)
    {
        if (_radioService is null || CanConnect)
        {
            if (strict)
            {
                throw new InvalidOperationException("Radio is not connected.");
            }

            return;
        }

        if (!TryParseUiFrequencyHz(frequencyLabel, out var hz))
        {
            if (strict)
            {
                throw new InvalidOperationException($"Could not parse SSTV frequency '{frequencyLabel}'.");
            }

            return;
        }

        try
        {
            var mode = frequencyLabel.Contains("LSB", StringComparison.OrdinalIgnoreCase)
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            await _radioService.SetModeAsync(mode, CancellationToken.None);
            await _radioService.SetFrequencyAsync(hz, CancellationToken.None);
            RadioStatusSummary = $"SSTV tuned: {hz:N0} Hz {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"SSTV tune failed: {ex.Message}";
            if (strict)
            {
                throw;
            }
        }
    }

    private async Task VerifySstvPttRaisedAsync(CancellationToken ct)
    {
        if (_radioService is null)
        {
            throw new InvalidOperationException("Radio service unavailable.");
        }

        if (_radioService.CurrentState.IsPttActive)
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(100 + (attempt * 100), ct).ConfigureAwait(false);
            try
            {
                await _radioService.RefreshStateAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // If the rig cannot be polled while PTT is changing, keep the successful
                // SetPttAsync command as authoritative instead of dropping transmit.
                return;
            }

            if (_radioService.CurrentState.IsPttActive)
            {
                return;
            }
        }

        // Some Icom CI-V paths can lag or momentarily report RX after accepting the PTT command.
        // SetPttAsync throws on command failure, so do not abort SSTV audio on an ambiguous readback.
    }

    private string? ValidateSstvLiveTransmitInterlock()
    {
        if (_radioService is null || CanConnect)
        {
            return "SSTV TX blocked: radio not connected.";
        }

        if (!_radioService.CurrentState.IsConnected)
        {
            return "SSTV TX blocked: radio control is not connected.";
        }

        if (_audioService is null)
        {
            return "SSTV TX blocked: audio service unavailable.";
        }

        if (SelectedTxDevice is null)
        {
            return "SSTV TX blocked: TX audio device not configured.";
        }

        if (string.IsNullOrWhiteSpace(SelectedTxDevice.DeviceId))
        {
            return "SSTV TX blocked: selected TX audio device has no device id.";
        }

        if (SelectedSstvReplyBaseImage is null)
        {
            return "SSTV TX blocked: no reply base image selected.";
        }

        if (_sstvPreparedTransmitClip is null)
        {
            return "SSTV TX blocked: prepare TX audio first.";
        }

        if (_sstvPreparedTransmitClip.PcmBytes.Length == 0)
        {
            return "SSTV TX blocked: prepared TX audio is empty.";
        }

        if (!string.Equals(_sstvPreparedTransmitFingerprint, BuildSstvTransmitFingerprint(), StringComparison.Ordinal))
        {
            return "SSTV TX blocked: prepared image/audio is stale. Press Prepare TX again.";
        }

        return null;
    }

    private static string NormalizeSstvModeSelection(string selection) => selection switch
    {
        "Lock Martin M1" or "Martin M1" or "Martin 1" => "Martin 1",
        "Lock Martin M2" or "Martin M2" or "Martin 2" => "Martin 2",
        "Lock Scottie 1" or "Scottie 1" => "Scottie 1",
        "Lock Scottie 2" or "Scottie 2" => "Scottie 2",
        "Lock Robot 36" or "Robot 36" => "Robot 36",
        "Lock PD 120" or "PD 120" => "PD 120",
        "Auto Detect" => "Auto Detect",
        _ => "Auto Detect",
    };

    [RelayCommand]
    private async Task ApplySstvPostReceiveSlantAsync()
    {
        if (_sstvDecoderHost is null)
        {
            SstvRxStatus = "SSTV decoder host unavailable";
            return;
        }

        try
        {
            await _sstvDecoderHost.ApplyPostReceiveSlantCorrectionAsync(CancellationToken.None);
            SstvRxStatus = "MMSSTV post-receive slant correction requested";
        }
        catch (Exception ex)
        {
            SstvRxStatus = $"MMSSTV slant correction failed: {ex.Message}";
        }
    }

    private void UpdateSstvPreview(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            SstvPreviewBitmap = null;
            SstvHasPreview = false;
            OnPropertyChanged(nameof(SstvShowPlaceholder));
            return;
        }

        try
        {
            SstvPreviewBitmap = new Bitmap(imagePath);
            SstvHasPreview = true;
            OnPropertyChanged(nameof(SstvShowPlaceholder));
            AddOrSelectSstvArchiveImage(imagePath);
        }
        catch
        {
            SstvPreviewBitmap = null;
            SstvHasPreview = false;
            OnPropertyChanged(nameof(SstvShowPlaceholder));
        }
    }


    private void LoadSstvArchiveImages()
    {
        var selectedReceivedPath = SelectedSstvReceivedImage?.Path;
        var selectedReplyPath = SelectedSstvReplyBaseImage?.Path;
        var selectedTemplatePath = SelectedSstvReplyLayoutTemplate?.Path;
        var archive = SstvReplyArchiveStore.Load(_sstvReceivedDirectory, _sstvReplyDirectory, _sstvTemplateDirectory);

        SstvReceivedImages = new ObservableCollection<SstvImageItem>(archive.ReceivedImages);
        SstvReplyImages = new ObservableCollection<SstvImageItem>(archive.ReplyImages);
        SstvReplyLayoutTemplates = new ObservableCollection<SstvTemplateItem>(archive.LayoutTemplates);
        SelectedSstvReceivedImage = SstvReplyArchiveStore.SelectByPathOrFirst(SstvReceivedImages, selectedReceivedPath);
        SelectedSstvReplyBaseImage = SstvReplyArchiveStore.SelectByPathOrFirst(SstvReplyImages, selectedReplyPath);
        SelectedSstvReplyLayoutTemplate = SstvReplyArchiveStore.SelectByPathOrFirst(SstvReplyLayoutTemplates, selectedTemplatePath);
        if (SstvReplyOverlayItems.Count == 0)
        {
            var defaultOverlay = new SstvOverlayItemViewModel();
            SstvReplyOverlayItems.Add(defaultOverlay);
            SelectedSstvReplyOverlayItem = defaultOverlay;
        }
        OnPropertyChanged(nameof(SstvSelectedReceivedBitmap));
        OnPropertyChanged(nameof(SstvSelectedReceivedPath));
        OnPropertyChanged(nameof(SstvReplyPreviewBitmap));
        OnPropertyChanged(nameof(SstvReplyCanvasWidth));
        OnPropertyChanged(nameof(SstvReplyCanvasHeight));
        OnPropertyChanged(nameof(SstvReplyHasBaseImage));
        OnPropertyChanged(nameof(SstvReplyShowPlaceholder));
        OnPropertyChanged(nameof(SstvReceivedFolderPath));
        OnPropertyChanged(nameof(SstvReplyFolderPath));
        OnPropertyChanged(nameof(SstvTemplateFolderPath));
    }


    private void AddOrSelectSstvArchiveImage(string imagePath)
    {
        var existing = SstvReceivedImages.FirstOrDefault(item =>
            string.Equals(item.Path, imagePath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            try
            {
                existing = SstvReplyArchiveStore.TryCreateImageItem(imagePath);
                if (existing is null)
                {
                    return;
                }

                SstvReceivedImages.Insert(0, existing);
            }
            catch
            {
                return;
            }
        }

        SelectedSstvReceivedImage = existing;
        OnPropertyChanged(nameof(SstvSelectedReceivedBitmap));
        OnPropertyChanged(nameof(SstvSelectedReceivedPath));
    }


    partial void OnSelectedSstvReceivedImageChanged(SstvImageItem? value)
    {
        OnPropertyChanged(nameof(SstvSelectedReceivedBitmap));
        OnPropertyChanged(nameof(SstvSelectedReceivedPath));
    }

    partial void OnSelectedSstvReplyBaseImageChanged(SstvImageItem? value)
    {
        OnPropertyChanged(nameof(SstvReplyPreviewBitmap));
        OnPropertyChanged(nameof(SstvReplyCanvasWidth));
        OnPropertyChanged(nameof(SstvReplyCanvasHeight));
        OnPropertyChanged(nameof(SstvReplyHasBaseImage));
        OnPropertyChanged(nameof(SstvReplyShowPlaceholder));
        SstvTransmitStatus = value is null
            ? "Choose a reply image to prepare TX."
            : "Reply image changed; prepare TX when ready.";
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnSstvSelectedTxModeChanged(string value)
    {
        SstvTransmitStatus = "TX mode changed; prepare TX when ready.";
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnSstvTxCwIdEnabledChanged(bool value)
    {
        SstvTransmitStatus = "CW ID setting changed; prepare TX when ready.";
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnSstvTxFskIdEnabledChanged(bool value)
    {
        SstvTransmitStatus = "FSKID setting changed; prepare TX when ready.";
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnSstvTxCwIdTextChanged(string value)
    {
        SstvTransmitStatus = "CW ID text changed; prepare TX when ready.";
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnSstvTxCwIdFrequencyHzChanged(int value)
    {
        SstvTransmitStatus = "CW ID frequency changed; prepare TX when ready.";
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnSstvTxCwIdWpmChanged(int value)
    {
        SstvTransmitStatus = "CW ID speed changed; prepare TX when ready.";
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnSelectedSstvReplyLayoutTemplateChanged(SstvTemplateItem? value)
    {
        if (value is not null)
        {
            SstvReplyTemplateName = value.Name;
        }
    }

    partial void OnSstvReplyOverlayItemsChanged(ObservableCollection<SstvOverlayItemViewModel> value)
    {
        AttachSstvReplyLayoutChangeTracking(value);
        MarkSstvReplyLayoutDirty();
    }

    partial void OnSstvReplyImageOverlayItemsChanged(ObservableCollection<SstvImageOverlayItemViewModel> value)
    {
        AttachSstvReplyLayoutChangeTracking(value);
        MarkSstvReplyLayoutDirty();
    }

    partial void OnSelectedTxDeviceChanged(AudioDeviceInfo? value)
    {
        RefreshPreparedSstvTransmitSummary();
    }
}