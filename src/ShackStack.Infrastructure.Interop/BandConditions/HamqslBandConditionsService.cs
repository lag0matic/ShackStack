using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;
using System.Xml.Linq;

namespace ShackStack.Infrastructure.Interop.BandConditions;

public sealed class HamqslBandConditionsService : IBandConditionsService, IDisposable
{
    private static readonly Uri FeedUri = new("https://www.hamqsl.com/solarxml.php");
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);

    private readonly HttpClient _httpClient;
    private readonly SimpleSubject<BandConditionsSnapshot> _stream = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private int _started;

    public HamqslBandConditionsService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ShackStack.Avalonia/1.0");
    }

    public IObservable<BandConditionsSnapshot> SnapshotStream => _stream;

    public Task StartAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return Task.CompletedTask;
        }

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => LoopAsync(_loopCts.Token), _loopCts.Token);
        return Task.CompletedTask;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            await FetchAndPublishAsync(ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(RefreshInterval, ct).ConfigureAwait(false);
                await FetchAndPublishAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task FetchAndPublishAsync(CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(FeedUri, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var snapshot = Parse(xml);
        _stream.OnNext(snapshot);
    }

    private static BandConditionsSnapshot Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var solar = doc.Root?.Element("solardata");
        if (solar is null)
        {
            return Empty("Unavailable");
        }

        string G(string tag) => solar.Element(tag)?.Value?.Trim() ?? "—";

        var bands = new List<BandConditionCell>();
        foreach (var bandLabel in new[] { "80m-40m", "30m-20m", "17m-15m", "12m-10m" })
        {
            var day = solar.Elements("calculatedconditions")
                .Elements("band")
                .FirstOrDefault(x => (string?)x.Attribute("name") == bandLabel && (string?)x.Attribute("time") == "day")
                ?.Value?.Trim() ?? "—";
            var night = solar.Elements("calculatedconditions")
                .Elements("band")
                .FirstOrDefault(x => (string?)x.Attribute("name") == bandLabel && (string?)x.Attribute("time") == "night")
                ?.Value?.Trim() ?? "—";
            bands.Add(new BandConditionCell(bandLabel, day, night));
        }

        return new BandConditionsSnapshot(
            G("updated"),
            G("solarflux"),
            G("sunspots"),
            G("aindex"),
            G("kindex"),
            G("xray"),
            G("geomagfield"),
            bands);
    }

    private static BandConditionsSnapshot Empty(string updated) =>
        new(
            updated,
            "—",
            "—",
            "—",
            "—",
            "—",
            "—",
            [
                new BandConditionCell("80m-40m", "—", "—"),
                new BandConditionCell("30m-20m", "—", "—"),
                new BandConditionCell("17m-15m", "—", "—"),
                new BandConditionCell("12m-10m", "—", "—"),
            ]);

    public void Dispose()
    {
        _loopCts?.Cancel();
        _httpClient.Dispose();
        _loopCts?.Dispose();
    }
}
