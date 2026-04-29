using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShackStack.Core.Abstractions.Models;
using System.Collections.ObjectModel;
using System.IO;

namespace ShackStack.UI.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private IReadOnlyList<string> wefaxModeOptions =
    [
        "IOC 576 / 120 LPM",
        "IOC 576 / 90 LPM",
        "IOC 576 / 60 LPM",
        "IOC 288 / 120 LPM",
    ];

    [ObservableProperty]
    private string wefaxSelectedMode = "IOC 576 / 120 LPM";

    [ObservableProperty]
    private IReadOnlyList<string> wefaxFrequencyOptions =
    [
        "NOAA Atlantic 4235.0 kHz USB-D",
        "NOAA Atlantic 6340.5 kHz USB-D",
        "NOAA Atlantic 9110.0 kHz USB-D",
        "NOAA Atlantic 12750.0 kHz USB-D",
        "NOAA Pacific 4346.0 kHz USB-D",
        "NOAA Pacific 8682.0 kHz USB-D",
        "NOAA Pacific 12786.0 kHz USB-D",
        "NOAA Pacific 17151.2 kHz USB-D",
        "NOAA Gulf 4317.9 kHz USB-D",
        "NOAA Gulf 8503.9 kHz USB-D",
        "NOAA Gulf 12789.9 kHz USB-D",
        "NOAA Hawaii 9982.5 kHz USB-D",
        "NOAA Hawaii 11090.0 kHz USB-D",
        "NOAA Kodiak 8459.0 kHz USB-D",
        "NOAA Kodiak 12412.5 kHz USB-D",
    ];

    [ObservableProperty]
    private string wefaxSelectedFrequency = "NOAA Atlantic 12750.0 kHz USB-D";

    [ObservableProperty]
    private ObservableCollection<WefaxScheduleItem> wefaxScheduleItems = [];

    [ObservableProperty]
    private WefaxScheduleItem? selectedWefaxScheduleItem;

    [ObservableProperty]
    private string wefaxScheduleStatus = "NOAA radiofax schedule loaded from built-in UTC table.";

    [ObservableProperty]
    private int wefaxManualSlant;

    [ObservableProperty]
    private int wefaxManualOffset;

    [ObservableProperty]
    private int wefaxCenterHz = 1900;

    [ObservableProperty]
    private int wefaxShiftHz = 800;

    [ObservableProperty]
    private int wefaxMaxRows = 1500;

    [ObservableProperty]
    private IReadOnlyList<string> wefaxFilterOptions = ["Narrow", "Medium", "Wide"];

    [ObservableProperty]
    private string wefaxSelectedFilter = "Medium";

    [ObservableProperty]
    private bool wefaxAutoAlign = false;

    [ObservableProperty]
    private int wefaxAutoAlignAfterRows = 30;

    [ObservableProperty]
    private int wefaxAutoAlignEveryRows = 10;

    [ObservableProperty]
    private int wefaxAutoAlignStopRows = 500;

    [ObservableProperty]
    private double wefaxCorrelationThreshold = 0.05;

    [ObservableProperty]
    private int wefaxCorrelationRows = 15;

    [ObservableProperty]
    private bool wefaxInvertImage;

    [ObservableProperty]
    private bool wefaxBinaryImage;

    [ObservableProperty]
    private int wefaxBinaryThreshold = 128;

    [ObservableProperty]
    private bool wefaxNoiseRemoval;

    [ObservableProperty]
    private int wefaxNoiseThreshold = 24;

    [ObservableProperty]
    private int wefaxNoiseMargin = 1;

    [ObservableProperty]
    private string wefaxRxStatus = "WeFAX receiver ready";

    [ObservableProperty]
    private string wefaxImageStatus = "No WeFAX image captured yet";

    [ObservableProperty]
    private string wefaxSessionNotes = "Live auto-align is off by default because real maps can fool seam tracking. Use Start RX for scheduled captures, or Start Now if you joined late.";

    [ObservableProperty]
    private Bitmap? wefaxPreviewBitmap;

    [ObservableProperty]
    private bool wefaxHasPreview;

    public bool WefaxShowPlaceholder => !WefaxHasPreview;

    [ObservableProperty]
    private ObservableCollection<WefaxImageItem> wefaxReceivedImages = [];

    [ObservableProperty]
    private WefaxImageItem? selectedWefaxReceivedImage;

    public Bitmap? WefaxSelectedReceivedBitmap => SelectedWefaxReceivedImage?.Bitmap;

    public string WefaxSelectedReceivedPath => SelectedWefaxReceivedImage?.Path ?? "No saved WeFAX image selected";

    public string WefaxReceivedFolderPath => _wefaxReceivedDirectory;

    [RelayCommand]
    private void StartWefaxReceive()
    {
        if (_wefaxDecoderHost is null)
        {
            WefaxRxStatus = "WeFAX decoder host unavailable";
            return;
        }

        _ = StartWefaxReceiveCoreAsync(forceNow: false);
    }

    [RelayCommand]
    private void StartWefaxNow()
    {
        if (_wefaxDecoderHost is null)
        {
            WefaxRxStatus = "WeFAX decoder host unavailable";
            return;
        }

        _ = StartWefaxReceiveCoreAsync(forceNow: true);
    }

    [RelayCommand]
    private void StopWefaxReceive()
    {
        if (_wefaxDecoderHost is null)
        {
            return;
        }

        _ = _wefaxDecoderHost.StopAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void ResetWefaxSession()
    {
        if (_wefaxDecoderHost is not null)
        {
            _ = _wefaxDecoderHost.ResetAsync(CancellationToken.None);
        }

        WefaxRxStatus = "WeFAX receiver ready";
        WefaxImageStatus = "No WeFAX image captured yet";
        WefaxSessionNotes = "Live auto-align is off by default because real maps can fool seam tracking. Use Start RX for scheduled captures, or Start Now if you joined late.";
        UpdateWefaxPreview(null);
    }

    [RelayCommand]
    private void RefreshWefaxArchive()
    {
        LoadWefaxArchiveImages();
    }

    [RelayCommand]
    private void RefreshWefaxSchedule()
    {
        RebuildWefaxSchedule();
    }

    [RelayCommand]
    private void ApplyWefaxScheduleItem(WefaxScheduleItem? item)
    {
        if (item is null)
        {
            return;
        }

        WefaxSelectedMode = item.ModeLabel;
        WefaxSelectedFrequency = item.FrequencyLabel;
        SelectedWefaxScheduleItem = item;
        WefaxScheduleStatus = $"Selected {item.Station}: {item.Product} at {item.TimeText} UTC.";
    }

    private async Task StartWefaxReceiveCoreAsync(bool forceNow)
    {
        if (_wefaxDecoderHost is null)
        {
            return;
        }

        var (ioc, lpm) = ParseWefaxMode(WefaxSelectedMode);
        await TuneRadioForWefaxAsync(WefaxSelectedFrequency);
        var config = new WefaxDecoderConfiguration(
            WefaxSelectedMode,
            ioc,
            lpm,
            WefaxSelectedFrequency,
            WefaxManualSlant,
            WefaxManualOffset,
            Math.Clamp(WefaxCenterHz, 1000, 2400),
            Math.Clamp(WefaxShiftHz, 750, 900),
            Math.Clamp(WefaxMaxRows, 1000, 10000),
            WefaxSelectedFilter,
            WefaxAutoAlign,
            Math.Clamp(WefaxAutoAlignAfterRows, 1, 500),
            Math.Clamp(WefaxAutoAlignEveryRows, 1, 100),
            Math.Clamp(WefaxAutoAlignStopRows, 1, 5000),
            Math.Clamp(WefaxCorrelationThreshold, 0.01, 0.10),
            Math.Clamp(WefaxCorrelationRows, 2, 25),
            WefaxInvertImage,
            WefaxBinaryImage,
            Math.Clamp(WefaxBinaryThreshold, 0, 255),
            WefaxNoiseRemoval,
            Math.Clamp(WefaxNoiseThreshold, 1, 96),
            Math.Clamp(WefaxNoiseMargin, 1, 2));
        await _wefaxDecoderHost.ConfigureAsync(config, CancellationToken.None);
        if (forceNow)
        {
            await _wefaxDecoderHost.StartNowAsync(CancellationToken.None);
        }
        else
        {
            await _wefaxDecoderHost.StartAsync(CancellationToken.None);
        }
    }

    private void RebuildWefaxSchedule()
    {
        var now = DateTime.UtcNow;
        var items = BuildWefaxScheduleItems(now);
        WefaxScheduleItems = new ObservableCollection<WefaxScheduleItem>(items);
        WefaxScheduleStatus = $"Schedule shown in UTC. Updated {now:HH:mm}Z from built-in NOAA/NWS radiofax table.";
    }

    private async Task TuneRadioForWefaxAsync(string frequencyLabel)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        if (!TryParseWefaxPublishedFrequencyHz(frequencyLabel, out var publishedHz))
        {
            return;
        }

        var dialHz = publishedHz - 1_900L;
        if (dialHz <= 0)
        {
            return;
        }

        try
        {
            var mode = frequencyLabel.Contains("LSB", StringComparison.OrdinalIgnoreCase)
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            await _radioService.SetModeAsync(mode, CancellationToken.None);
            await ApplyPureDigitalReceivePresetAsync().ConfigureAwait(false);
            await _radioService.SetFrequencyAsync(dialHz, CancellationToken.None);
            RadioStatusSummary = $"WeFAX tuned: {dialHz:N0} Hz {FormatModeDisplay(mode)}  |  FIL1 NB/NR/AN off";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"WeFAX tune failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyWefaxManualSlantAsync()
    {
        if (_wefaxDecoderHost is null)
        {
            return;
        }

        try
        {
            await _wefaxDecoderHost.SetManualSlantAsync(WefaxManualSlant, CancellationToken.None);
            await _wefaxDecoderHost.SetManualOffsetAsync(WefaxManualOffset, CancellationToken.None);
            WefaxRxStatus = $"WeFAX alignment set: slant {WefaxManualSlant}, offset {WefaxManualOffset}";
        }
        catch (Exception ex)
        {
            WefaxRxStatus = $"WeFAX alignment apply failed: {ex.Message}";
        }
    }

    private void UpdateWefaxPreview(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            WefaxPreviewBitmap = null;
            WefaxHasPreview = false;
            OnPropertyChanged(nameof(WefaxShowPlaceholder));
            return;
        }

        try
        {
            WefaxPreviewBitmap = new Bitmap(imagePath);
            WefaxHasPreview = true;
            OnPropertyChanged(nameof(WefaxShowPlaceholder));
            AddOrSelectWefaxArchiveImage(imagePath);
        }
        catch
        {
            WefaxPreviewBitmap = null;
            WefaxHasPreview = false;
            OnPropertyChanged(nameof(WefaxShowPlaceholder));
        }
    }

    private void LoadWefaxArchiveImages()
    {
        Directory.CreateDirectory(_wefaxReceivedDirectory);

        var receivedItems = new List<WefaxImageItem>();
        foreach (var file in new DirectoryInfo(_wefaxReceivedDirectory)
                     .EnumerateFiles("*.png", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(f => f.LastWriteTimeUtc)
                     .Take(60))
        {
            try
            {
                receivedItems.Add(new WefaxImageItem(
                    Path.GetFileNameWithoutExtension(file.Name),
                    file.FullName,
                    file.LastWriteTime,
                    new Bitmap(file.FullName)));
            }
            catch
            {
            }
        }

        WefaxReceivedImages = new ObservableCollection<WefaxImageItem>(receivedItems);
        SelectedWefaxReceivedImage ??= WefaxReceivedImages.FirstOrDefault();
        OnPropertyChanged(nameof(WefaxSelectedReceivedBitmap));
        OnPropertyChanged(nameof(WefaxSelectedReceivedPath));
        OnPropertyChanged(nameof(WefaxReceivedFolderPath));
    }

    private void AddOrSelectWefaxArchiveImage(string imagePath)
    {
        var existing = WefaxReceivedImages.FirstOrDefault(item =>
            string.Equals(item.Path, imagePath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            try
            {
                var file = new FileInfo(imagePath);
                existing = new WefaxImageItem(
                    Path.GetFileNameWithoutExtension(file.Name),
                    file.FullName,
                    file.LastWriteTime,
                    new Bitmap(file.FullName));
                WefaxReceivedImages.Insert(0, existing);
            }
            catch
            {
                return;
            }
        }

        SelectedWefaxReceivedImage = existing;
        OnPropertyChanged(nameof(WefaxSelectedReceivedBitmap));
        OnPropertyChanged(nameof(WefaxSelectedReceivedPath));
    }

    partial void OnSelectedWefaxReceivedImageChanged(WefaxImageItem? value)
    {
        OnPropertyChanged(nameof(WefaxSelectedReceivedBitmap));
        OnPropertyChanged(nameof(WefaxSelectedReceivedPath));
    }

    partial void OnSelectedWefaxScheduleItemChanged(WefaxScheduleItem? value)
    {
        if (value is null)
        {
            return;
        }

        WefaxSelectedMode = value.ModeLabel;
        WefaxSelectedFrequency = value.FrequencyLabel;
        WefaxScheduleStatus = $"Selected {value.Station}: {value.Product} at {value.TimeText} UTC.";
    }

    private static (int Ioc, int Lpm) ParseWefaxMode(string modeLabel)
    {
        var normalized = modeLabel?.Trim() ?? string.Empty;
        if (normalized.Contains("288", StringComparison.OrdinalIgnoreCase))
        {
            return (288, normalized.Contains("90", StringComparison.OrdinalIgnoreCase) ? 90 : normalized.Contains("60", StringComparison.OrdinalIgnoreCase) ? 60 : 120);
        }

        if (normalized.Contains("90", StringComparison.OrdinalIgnoreCase))
        {
            return (576, 90);
        }

        if (normalized.Contains("60", StringComparison.OrdinalIgnoreCase))
        {
            return (576, 60);
        }

        return (576, 120);
    }

    private static bool TryParseWefaxPublishedFrequencyHz(string frequencyLabel, out long hz)
    {
        hz = 0;
        if (string.IsNullOrWhiteSpace(frequencyLabel))
        {
            return false;
        }

        var match = System.Text.RegularExpressions.Regex.Match(frequencyLabel, @"(\d+(?:\.\d+)?)\s*kHz", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        if (!double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var khz))
        {
            return false;
        }

        hz = (long)Math.Round(khz * 1000.0);
        return hz > 0;
    }

    private static IReadOnlyList<WefaxScheduleItem> BuildWefaxScheduleItems(DateTime utcNow)
    {
        var items = new List<WefaxScheduleItem>();
        foreach (var definition in BuildWefaxScheduleDefinitions())
        {
            var start = new DateTime(
                utcNow.Year,
                utcNow.Month,
                utcNow.Day,
                definition.Hour,
                definition.Minute,
                0,
                DateTimeKind.Utc);
            var end = start.AddMinutes(definition.DurationMinutes);
            if (end <= utcNow)
            {
                start = start.AddDays(1);
                end = end.AddDays(1);
            }

            var isOnAir = start <= utcNow && utcNow < end;
            var status = isOnAir ? "ON AIR" : "NEXT";
            var until = isOnAir
                ? $"ends in {FormatWefaxScheduleDelta(end - utcNow)}"
                : $"in {FormatWefaxScheduleDelta(start - utcNow)}";
            items.Add(new WefaxScheduleItem(
                status,
                start.ToString("HH:mm"),
                until,
                definition.Station,
                definition.Product,
                definition.FrequencyLabel,
                "IOC 576 / 120 LPM",
                definition.Source,
                start,
                end,
                BuildWefaxScheduleStatusBrush(isOnAir)));
        }

        return items
            .OrderBy(item => item.Status == "ON AIR" ? 0 : 1)
            .ThenBy(item => item.StartUtc)
            .Take(14)
            .ToList();
    }

    private static IReadOnlyList<WefaxScheduleDefinition> BuildWefaxScheduleDefinitions()
    {
        var schedule = new List<WefaxScheduleDefinition>();

        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 9110.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 0233, "00Z Preliminary Surface Analysis");
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 9110.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 0325, "00Z Surface Analysis Part 1 NE Atlantic", 13);
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 9110.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 0338, "00Z Surface Analysis Part 2 NW Atlantic", 13);
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 9110.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 0805, "24Hr Surface Forecast");
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 9110.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 0845, "48Hr Surface Forecast");
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 12750.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 1453, "12Z Preliminary Surface Analysis");
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 12750.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 1810, "24Hr Surface Forecast");
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 12750.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 1955, "48Hr Surface Forecast");
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 12750.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 2125, "18Z Surface Analysis Part 1 NE Atlantic", 13);
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 12750.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 2138, "18Z Surface Analysis Part 2 NW Atlantic", 13);

        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 8682.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 0143, "NE Pacific GOES IR Satellite Image");
        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 8682.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 0305, "Prelim Surface Analysis Part 1 NE Pacific", 13);
        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 8682.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 0318, "Prelim Surface Analysis Part 2 NW Pacific", 13);
        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 8682.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 0828, "48Hr Surface Forecast");
        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 12786.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 1403, "NE Pacific GOES IR Satellite Image");
        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 12786.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 1822, "24Hr Surface Forecast");
        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 12786.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 2013, "48Hr Surface Forecast");
        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 12786.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 2113, "Pacific GOES IR Satellite Image");

        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 8503.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 0005, "U.S./Tropical Surface Analysis W Half", 15);
        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 8503.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 0020, "Tropical Surface Analysis E Half", 15);
        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 8503.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 0655, "24Hr Surface Forecast");
        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 8503.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 0705, "48Hr Surface Forecast");
        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 12789.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 1805, "U.S./Tropical Surface Analysis W Half", 15);
        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 12789.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 1820, "Tropical Surface Analysis E Half", 15);
        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 12789.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 1855, "24Hr Surface Forecast");
        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 12789.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 1905, "48Hr Surface Forecast");

        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 11090.0 kHz USB-D", "NOAA Hawaii hfhi.txt", 0535, "Cyclone Danger Area");
        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 11090.0 kHz USB-D", "NOAA Hawaii hfhi.txt", 0615, "Surface Analysis");
        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 11090.0 kHz USB-D", "NOAA Hawaii hfhi.txt", 0701, "24Hr Surface Forecast");
        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 11090.0 kHz USB-D", "NOAA Hawaii hfhi.txt", 0917, "Surface Analysis Part 1 NE Pacific", 13);
        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 11090.0 kHz USB-D", "NOAA Hawaii hfhi.txt", 1530, "Surface Analysis Part 1 NE Pacific", 13);
        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 9982.5 kHz USB-D", "NOAA Hawaii hfhi.txt", 1719, "Test Pattern");
        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 9982.5 kHz USB-D", "NOAA Hawaii hfhi.txt", 1815, "Surface Analysis");
        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 9982.5 kHz USB-D", "NOAA Hawaii hfhi.txt", 1914, "48Hr Surface Forecast");

        return schedule;
    }

    private static void AddWefax(
        List<WefaxScheduleDefinition> schedule,
        string station,
        string frequencyLabel,
        string source,
        int hhmm,
        string product,
        int durationMinutes = 10)
    {
        schedule.Add(new WefaxScheduleDefinition(
            station,
            frequencyLabel,
            source,
            hhmm / 100,
            hhmm % 100,
            durationMinutes,
            product));
    }

    private static string FormatWefaxScheduleDelta(TimeSpan delta)
    {
        var totalMinutes = Math.Max(0, (int)Math.Round(delta.TotalMinutes, MidpointRounding.AwayFromZero));
        if (totalMinutes < 60)
        {
            return $"{totalMinutes}m";
        }

        return $"{totalMinutes / 60}h {totalMinutes % 60:00}m";
    }

    private static IBrush BuildWefaxScheduleStatusBrush(bool isOnAir) =>
        new SolidColorBrush(Color.Parse(isOnAir ? "#1E6F5C" : "#24304A"));

    private sealed record WefaxScheduleDefinition(
        string Station,
        string FrequencyLabel,
        string Source,
        int Hour,
        int Minute,
        int DurationMinutes,
        string Product);
}