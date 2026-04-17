using System.Diagnostics;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Decoders;

public sealed class SystemClockDisciplineService : IClockDisciplineService, IDisposable
{
    private readonly SimpleSubject<ClockDisciplineSnapshot> _snapshots = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;

    private ClockDisciplineSnapshot _current = new(
        "System clock status not checked yet",
        false,
        0,
        "System UTC",
        DateTimeOffset.UtcNow);

    public SystemClockDisciplineService()
    {
        _snapshots.OnNext(_current);
        _loopTask = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    public ClockDisciplineSnapshot Current => _current;

    public IObservable<ClockDisciplineSnapshot> SnapshotStream => _snapshots;

    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        while (!ct.IsCancellationRequested)
        {
            await RefreshAsync().ConfigureAwait(false);

            try
            {
                await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RefreshAsync()
    {
        var snapshot = await QueryWindowsTimeStatusAsync().ConfigureAwait(false);
        _current = snapshot;
        _snapshots.OnNext(snapshot);
    }

    private static async Task<ClockDisciplineSnapshot> QueryWindowsTimeStatusAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "w32tm",
                Arguments = "/query /status",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return new ClockDisciplineSnapshot(
                    string.IsNullOrWhiteSpace(error) ? "Windows time service unavailable" : $"Windows time query failed: {error.Trim()}",
                    false,
                    0,
                    "System UTC",
                    DateTimeOffset.UtcNow);
            }

            var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var source = lines.FirstOrDefault(line => line.StartsWith("Source:", StringComparison.OrdinalIgnoreCase));
            var stratum = lines.FirstOrDefault(line => line.StartsWith("Stratum:", StringComparison.OrdinalIgnoreCase));
            var lastSync = lines.FirstOrDefault(line => line.StartsWith("Last Successful Sync Time:", StringComparison.OrdinalIgnoreCase));

            var sourceValue = source?.Split(':', 2)[1].Trim();
            var stratumValue = stratum?.Split(':', 2)[1].Trim();
            var status = lastSync is not null
                ? $"{lastSync}{(stratumValue is not null ? $" | {stratumValue}" : string.Empty)}"
                : $"Clock source {(string.IsNullOrWhiteSpace(sourceValue) ? "unknown" : sourceValue)}";

            var isSynchronized = !string.IsNullOrWhiteSpace(sourceValue)
                && !sourceValue.Contains("Local CMOS Clock", StringComparison.OrdinalIgnoreCase)
                && !sourceValue.Contains("Free-running", StringComparison.OrdinalIgnoreCase);

            return new ClockDisciplineSnapshot(
                status,
                isSynchronized,
                0,
                string.IsNullOrWhiteSpace(sourceValue) ? "System UTC" : sourceValue,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return new ClockDisciplineSnapshot(
                $"Clock discipline check unavailable: {ex.Message}",
                false,
                0,
                "System UTC",
                DateTimeOffset.UtcNow);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loopTask.Wait(250);
        }
        catch
        {
        }

        _cts.Dispose();
    }
}
