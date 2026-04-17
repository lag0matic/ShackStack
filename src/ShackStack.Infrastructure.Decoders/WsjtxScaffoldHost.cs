using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Decoders;

public sealed class WsjtxScaffoldHost : IWsjtxModeHost, IDisposable
{
    private readonly SimpleSubject<WsjtxModeTelemetry> _telemetry = new();
    private readonly SimpleSubject<WsjtxDecodeMessage> _decode = new();
    private readonly object _sync = new();
    private readonly IClockDisciplineService _clockDisciplineService;

    private WsjtxModeConfiguration _configuration = new("FT8", "20m FT8 14.074 MHz USB-D", AutoSequenceEnabled: true, CallCQEnabled: false, Ft8SubtractionEnabled: false, Ft8ApEnabled: false, Ft8OsdEnabled: false, CycleLengthSeconds: 15.0, RequiresAccurateClock: true, StationCallsign: string.Empty, StationGridSquare: string.Empty, TransmitFirstEnabled: false);
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private bool _isRunning;
    private int _decodeCount;

    public WsjtxScaffoldHost(IClockDisciplineService clockDisciplineService)
    {
        _clockDisciplineService = clockDisciplineService;
        PublishTelemetry("WSJT-style scaffold ready");
    }

    public IObservable<WsjtxModeTelemetry> TelemetryStream => _telemetry;

    public IObservable<WsjtxDecodeMessage> DecodeStream => _decode;

    public Task ConfigureAsync(WsjtxModeConfiguration configuration, CancellationToken ct)
    {
        lock (_sync)
        {
            _configuration = configuration;
        }

        PublishTelemetry("WSJT-style scaffold configured");
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (_isRunning)
        {
            return Task.CompletedTask;
        }

        _isRunning = true;
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token), _loopCts.Token);
        PublishTelemetry("Listening for weak-signal digital traffic");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _isRunning = false;
        if (_loopCts is not null)
        {
            _loopCts.Cancel();
        }

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _loopTask = null;
        _loopCts?.Dispose();
        _loopCts = null;
        PublishTelemetry("Weak-signal digital receive stopped");
    }

    public Task ResetAsync(CancellationToken ct)
    {
        _decodeCount = 0;
        PublishTelemetry("Weak-signal session reset");
        return Task.CompletedTask;
    }

    public Task<WsjtxPreparedTransmitResult> PrepareTransmitAsync(string modeLabel, string messageText, int txAudioFrequencyHz, CancellationToken ct)
    {
        return Task.FromResult(new WsjtxPreparedTransmitResult(
            false,
            $"{modeLabel} TX audio preparation is unavailable in scaffold mode",
            null,
            null));
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            PublishTelemetry("Listening for weak-signal digital traffic");
        }
    }

    private void PublishTelemetry(string status)
    {
        WsjtxModeConfiguration configuration;
        lock (_sync)
        {
            configuration = _configuration;
        }

        var cycleLength = configuration.CycleLengthSeconds > 0 ? configuration.CycleLengthSeconds : GetCycleLengthSeconds(configuration.ModeLabel);
        var utcNow = DateTimeOffset.UtcNow;
        var secondsIntoMinute = utcNow.Second + (utcNow.Millisecond / 1000.0);
        var cycleIndex = Math.Floor(secondsIntoMinute / cycleLength);
        var cycleEnd = (cycleIndex + 1) * cycleLength;
        var secondsToNextCycle = cycleEnd - secondsIntoMinute;
        if (secondsToNextCycle < 0)
        {
            secondsToNextCycle += cycleLength;
        }
        var clock = _clockDisciplineService.Current;
        var clockStatus = configuration.RequiresAccurateClock
            ? $"{clock.Status} | Source {clock.SourceLabel}"
            : $"Clock source {clock.SourceLabel}";

        _telemetry.OnNext(new WsjtxModeTelemetry(
            _isRunning,
            status,
            "WSJT scaffold host",
            configuration.ModeLabel,
            clockStatus,
            clock.IsSynchronized,
            clock.OffsetMs,
            cycleLength,
            secondsToNextCycle,
            _decodeCount,
            configuration.AutoSequenceEnabled,
            configuration.CallCQEnabled));
    }

    private static double GetCycleLengthSeconds(string modeLabel) => modeLabel switch
    {
        "FT4" => 7.5,
        "FST4" => 15.0,
        "Q65" => 15.0,
        "JT65" => 60.0,
        "JT9" => 60.0,
        "WSPR" => 120.0,
        _ => 15.0,
    };

    public void Dispose()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
    }
}
