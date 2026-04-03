using System.Text.Json;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Infrastructure.Configuration;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string SettingsFilePath { get; }

    public JsonAppSettingsStore()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(roaming, "ShackStack");
        SettingsFilePath = Path.Combine(directory, "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(SettingsFilePath))
        {
            var defaults = AppSettings.Default;
            await SaveAsync(defaults, cancellationToken).ConfigureAwait(false);
            return defaults;
        }

        var json = await File.ReadAllTextAsync(SettingsFilePath, cancellationToken).ConfigureAwait(false);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        var normalized = ApplySchemaDefaults(
            NormalizeSettings(MigrateLegacyDefaults(MigrateFromLegacyConfigIfNeeded(settings ?? AppSettings.Default))),
            json);

        if (!string.Equals(json.Trim(), JsonSerializer.Serialize(normalized, JsonOptions), StringComparison.Ordinal))
        {
            await SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
        }

        return normalized;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        settings = NormalizeSettings(settings);
        await using var stream = File.Create(SettingsFilePath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static AppSettings MigrateFromLegacyConfigIfNeeded(AppSettings settings)
    {
        if (!string.Equals(settings.Radio.CivPort, "auto", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.Radio.CivPort))
        {
            return settings;
        }

        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".shackstack",
            "config.json");

        if (!File.Exists(legacyPath))
        {
            return settings;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(legacyPath));
            if (!doc.RootElement.TryGetProperty("wfview", out var wfview))
            {
                return settings;
            }

            var civPort = wfview.TryGetProperty("civ_port", out var portProp)
                ? portProp.GetString()
                : null;
            var civBaud = wfview.TryGetProperty("civ_baud", out var baudProp) && baudProp.TryGetInt32(out var parsedBaud)
                ? parsedBaud
                : settings.Radio.CivBaud;
            var civAddress = wfview.TryGetProperty("civ_radio_address", out var addrProp) && addrProp.TryGetInt32(out var parsedAddr)
                ? parsedAddr
                : settings.Radio.CivAddress;

            if (string.IsNullOrWhiteSpace(civPort) || string.Equals(civPort, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return settings;
            }

            return settings with
            {
                Radio = settings.Radio with
                {
                    CivPort = civPort,
                    CivBaud = civBaud,
                    CivAddress = civAddress,
                }
            };
        }
        catch
        {
            return settings;
        }
    }

    private static AppSettings MigrateLegacyDefaults(AppSettings settings)
    {
        settings = NormalizeSettings(settings);

        if (settings.Ui.WindowWidth == 1280 && settings.Ui.WindowHeight == 780)
        {
            return settings with
            {
                Ui = settings.Ui with
                {
                    WindowWidth = 1920,
                    WindowHeight = 1080,
                }
            };
        }

        return settings;
    }

    private static AppSettings NormalizeSettings(AppSettings settings)
    {
        return settings with
        {
            Station = settings.Station,
            Ui = settings.Ui with
            {
                Theme = string.IsNullOrWhiteSpace(settings.Ui.Theme) ? "dark" : settings.Ui.Theme,
                WaterfallColormap = string.IsNullOrWhiteSpace(settings.Ui.WaterfallColormap) ? "classic" : settings.Ui.WaterfallColormap,
            }
        };
    }

    private static AppSettings ApplySchemaDefaults(AppSettings settings, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var hasStation = root.TryGetProperty("station", out _);
            var hasBandConditionsEnabled =
                root.TryGetProperty("ui", out var uiElement)
                && uiElement.ValueKind == JsonValueKind.Object
                && uiElement.TryGetProperty("bandConditionsEnabled", out _);
            var hasShowExperimentalCw =
                root.TryGetProperty("ui", out uiElement)
                && uiElement.ValueKind == JsonValueKind.Object
                && uiElement.TryGetProperty("showExperimentalCw", out _);
            var hasWaterfallFloorPercent =
                root.TryGetProperty("ui", out uiElement)
                && uiElement.ValueKind == JsonValueKind.Object
                && uiElement.TryGetProperty("waterfallFloorPercent", out _);
            var hasWaterfallCeilingPercent =
                root.TryGetProperty("ui", out uiElement)
                && uiElement.ValueKind == JsonValueKind.Object
                && uiElement.TryGetProperty("waterfallCeilingPercent", out _);
            var hasMonitorVolumePercent =
                root.TryGetProperty("audio", out var audioElement)
                && audioElement.ValueKind == JsonValueKind.Object
                && audioElement.TryGetProperty("monitorVolumePercent", out _);

            return settings with
            {
                Audio = settings.Audio with
                {
                    MonitorVolumePercent = hasMonitorVolumePercent
                        ? settings.Audio.MonitorVolumePercent
                        : AppSettings.Default.Audio.MonitorVolumePercent,
                },
                Station = hasStation ? settings.Station : AppSettings.Default.Station,
                Ui = settings.Ui with
                {
                    BandConditionsEnabled = hasBandConditionsEnabled
                        ? settings.Ui.BandConditionsEnabled
                        : AppSettings.Default.Ui.BandConditionsEnabled,
                    ShowExperimentalCw = hasShowExperimentalCw
                        ? settings.Ui.ShowExperimentalCw
                        : AppSettings.Default.Ui.ShowExperimentalCw,
                    WaterfallFloorPercent = hasWaterfallFloorPercent
                        ? settings.Ui.WaterfallFloorPercent
                        : AppSettings.Default.Ui.WaterfallFloorPercent,
                    WaterfallCeilingPercent = hasWaterfallCeilingPercent
                        ? settings.Ui.WaterfallCeilingPercent
                        : AppSettings.Default.Ui.WaterfallCeilingPercent,
                }
            };
        }
        catch
        {
            return settings;
        }
    }
}
