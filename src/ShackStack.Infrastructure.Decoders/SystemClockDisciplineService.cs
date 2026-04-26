using System.Diagnostics;
using System.Net.Sockets;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Decoders;

public sealed class SystemClockDisciplineService : IClockDisciplineService, IDisposable
{
    private static readonly string[] SntpServers =
    [
        "time.cloudflare.com",
        "pool.ntp.org",
        "time.windows.com"
    ];

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
        var windowsSnapshot = await QueryWindowsTimeStatusAsync().ConfigureAwait(false);
        var snapshot = await QuerySntpStatusAsync(windowsSnapshot).ConfigureAwait(false)
            ?? windowsSnapshot;
        _current = snapshot;
        _snapshots.OnNext(snapshot);
    }

    private static async Task<ClockDisciplineSnapshot?> QuerySntpStatusAsync(ClockDisciplineSnapshot windowsSnapshot)
    {
        foreach (var server in SntpServers)
        {
            var result = await TryQuerySntpServerAsync(server).ConfigureAwait(false);
            if (result is null)
            {
                continue;
            }

            var status = $"SNTP offset {result.Value.OffsetMs:+0.0;-0.0;0.0} ms | Windows: {windowsSnapshot.Status}";
            return new ClockDisciplineSnapshot(
                status,
                true,
                result.Value.OffsetMs,
                $"SNTP {server}",
                DateTimeOffset.UtcNow);
        }

        return null;
    }

    private static async Task<(double OffsetMs, double RoundTripMs)?> TryQuerySntpServerAsync(string server)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 1500;
            udp.Connect(server, 123);

            var request = new byte[48];
            request[0] = 0x1B;

            var transmitStarted = DateTimeOffset.UtcNow;
            WriteNtpTimestamp(request, 40, transmitStarted);
            await udp.SendAsync(request, request.Length).ConfigureAwait(false);

            var receiveTask = udp.ReceiveAsync();
            var completed = await Task.WhenAny(receiveTask, Task.Delay(TimeSpan.FromMilliseconds(1500))).ConfigureAwait(false);
            if (completed != receiveTask)
            {
                return null;
            }

            var response = receiveTask.Result.Buffer;
            var destination = DateTimeOffset.UtcNow;
            if (response.Length < 48)
            {
                return null;
            }

            var originate = ReadNtpTimestamp(response, 24);
            var receive = ReadNtpTimestamp(response, 32);
            var transmit = ReadNtpTimestamp(response, 40);
            if (receive == DateTimeOffset.MinValue || transmit == DateTimeOffset.MinValue)
            {
                return null;
            }

            if (originate != DateTimeOffset.MinValue
                && Math.Abs((originate - transmitStarted).TotalMilliseconds) > 1000)
            {
                return null;
            }

            // RFC 5905 offset: ((serverReceive - clientSend) + (serverTransmit - clientReceive)) / 2.
            var offset = ((receive - transmitStarted).TotalMilliseconds + (transmit - destination).TotalMilliseconds) / 2.0;
            var roundTrip = (destination - transmitStarted).TotalMilliseconds - (transmit - receive).TotalMilliseconds;
            if (!double.IsFinite(offset) || !double.IsFinite(roundTrip) || roundTrip < 0)
            {
                return null;
            }

            return (offset, roundTrip);
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset ReadNtpTimestamp(byte[] buffer, int offset)
    {
        if (buffer.Length < offset + 8)
        {
            return DateTimeOffset.MinValue;
        }

        var seconds = ReadUInt32BigEndian(buffer, offset);
        var fraction = ReadUInt32BigEndian(buffer, offset + 4);
        if (seconds == 0 && fraction == 0)
        {
            return DateTimeOffset.MinValue;
        }

        var milliseconds = (seconds * 1000.0) + (fraction * 1000.0 / 0x1_0000_0000L);
        return DateTimeOffset.UnixEpoch.AddMilliseconds(milliseconds - 2_208_988_800_000.0);
    }

    private static void WriteNtpTimestamp(byte[] buffer, int offset, DateTimeOffset timestamp)
    {
        var millisecondsSinceNtpEpoch = (timestamp - DateTimeOffset.UnixEpoch).TotalMilliseconds + 2_208_988_800_000.0;
        var seconds = (uint)Math.Floor(millisecondsSinceNtpEpoch / 1000.0);
        var fraction = (uint)(((millisecondsSinceNtpEpoch % 1000.0) / 1000.0) * 0x1_0000_0000L);
        WriteUInt32BigEndian(buffer, offset, seconds);
        WriteUInt32BigEndian(buffer, offset + 4, fraction);
    }

    private static uint ReadUInt32BigEndian(byte[] buffer, int offset) =>
        ((uint)buffer[offset] << 24)
        | ((uint)buffer[offset + 1] << 16)
        | ((uint)buffer[offset + 2] << 8)
        | buffer[offset + 3];

    private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
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
