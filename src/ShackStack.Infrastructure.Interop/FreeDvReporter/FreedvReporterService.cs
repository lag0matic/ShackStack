using System.Collections.Concurrent;
using System.Text.Json;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;
using SocketIOClient;
using SocketIOClient.Transport;
using SocketIoClientType = SocketIOClient.SocketIO;

namespace ShackStack.Infrastructure.Interop.FreeDvReporter;

public sealed class FreedvReporterService : IFreedvReporterService, IDisposable
{
    private const string DefaultHostname = "qso.freedv.org";
    private const int ProtocolVersion = 2;

    private readonly SimpleSubject<FreedvReporterSnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<string, FreedvReporterStation> _stations = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SocketIoClientType? _socket;
    private FreedvReporterConfiguration? _configuration;
    private string _status = "Reporter disconnected";
    private bool _isConnected;
    private long _lastFrequencyHz;
    private string _lastMode = string.Empty;
    private bool _lastTransmitState;
    private string _lastMessage = string.Empty;

    public IObservable<FreedvReporterSnapshot> SnapshotStream => _snapshots;

    public async Task ConnectAsync(FreedvReporterConfiguration configuration, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            _configuration = NormalizeConfiguration(configuration);
            _stations.Clear();
            _status = $"Connecting FreeDV Reporter: {_configuration.Hostname}";
            Publish();

            var auth = BuildAuth(_configuration);
            var uri = new Uri($"https://{_configuration.Hostname}");
            var socket = new SocketIoClientType(uri, new SocketIOOptions
            {
                Reconnection = true,
                ReconnectionAttempts = 10,
                ReconnectionDelay = 5000,
                Transport = TransportProtocol.WebSocket,
                Auth = auth,
            });

            socket.OnConnected += async (_, _) =>
            {
                _status = "FreeDV Reporter socket connected; waiting for auth.";
                Publish();
                await ReplayStateAsync(CancellationToken.None).ConfigureAwait(false);
            };
            socket.OnDisconnected += (_, reason) =>
            {
                _isConnected = false;
                _status = $"FreeDV Reporter disconnected: {reason}";
                Publish();
            };
            socket.OnReconnectAttempt += (_, attempt) =>
            {
                _status = $"FreeDV Reporter reconnect attempt {attempt}";
                Publish();
            };
            socket.OnError += (_, error) =>
            {
                _status = $"FreeDV Reporter error: {error}";
                Publish();
            };

            RegisterHandlers(socket);
            _socket = socket;
            await socket.ConnectAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            _stations.Clear();
            _status = "Reporter disconnected";
            _isConnected = false;
            Publish();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateFrequencyAsync(long frequencyHz, CancellationToken ct)
    {
        _lastFrequencyHz = Math.Max(0, frequencyHz);
        if (_socket?.Connected == true && _lastFrequencyHz > 0)
        {
            await _socket.EmitAsync("freq_change", new { freq = _lastFrequencyHz }).ConfigureAwait(false);
        }
    }

    public async Task UpdateTransmitAsync(string mode, bool isTransmitting, CancellationToken ct)
    {
        _lastMode = mode.Trim();
        _lastTransmitState = isTransmitting;
        if (_socket?.Connected == true)
        {
            await _socket.EmitAsync("tx_report", new { mode = _lastMode, transmitting = isTransmitting }).ConfigureAwait(false);
        }
    }

    public async Task UpdateMessageAsync(string message, CancellationToken ct)
    {
        _lastMessage = message.Trim();
        if (_socket?.Connected == true)
        {
            await _socket.EmitAsync("message_update", new { message = _lastMessage }).ConfigureAwait(false);
        }
    }

    public async Task ReportReceiveAsync(string callsign, string mode, double snrDb, CancellationToken ct)
    {
        var cleanCall = callsign.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(cleanCall) || _socket?.Connected != true)
        {
            return;
        }

        await _socket.EmitAsync("rx_report", new
        {
            callsign = cleanCall,
            mode = mode.Trim(),
            snr = (int)Math.Round(snrDb),
        }).ConfigureAwait(false);
    }

    private void RegisterHandlers(SocketIoClientType socket)
    {
        socket.On("connection_successful", response =>
        {
            _isConnected = true;
            _status = "FreeDV Reporter connected";
            Publish();
            _ = ReplayStateAsync(CancellationToken.None);
        });
        socket.On("new_connection", response => HandleConnection(response, isRemove: false));
        socket.On("remove_connection", response => HandleConnection(response, isRemove: true));
        socket.On("freq_change", HandleFrequencyChange);
        socket.On("tx_report", HandleTransmitReport);
        socket.On("rx_report", HandleReceiveReport);
        socket.On("message_update", HandleMessageUpdate);
        socket.On("bulk_update", HandleBulkUpdate);
    }

    private void HandleConnection(SocketIOResponse response, bool isRemove) =>
        HandleConnection(GetJson(response), isRemove);

    private void HandleConnection(JsonElement json, bool isRemove)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var sid = GetString(json, "sid");
        if (string.IsNullOrWhiteSpace(sid))
        {
            return;
        }

        if (isRemove)
        {
            _stations.TryRemove(sid, out _);
            Publish();
            return;
        }

        var station = GetOrCreate(sid) with
        {
            Callsign = GetString(json, "callsign"),
            GridSquare = GetString(json, "grid_square"),
            Version = GetString(json, "version"),
            ReceiveOnly = GetBool(json, "rx_only"),
            ConnectedAtUtc = ParseDate(GetString(json, "connect_time")),
            LastUpdatedUtc = ParseDate(GetString(json, "last_update")),
        };
        _stations[sid] = station;
        Publish();
    }

    private void HandleFrequencyChange(SocketIOResponse response) =>
        HandleFrequencyChange(GetJson(response));

    private void HandleFrequencyChange(JsonElement json)
    {
        var sid = GetString(json, "sid");
        if (string.IsNullOrWhiteSpace(sid))
        {
            return;
        }

        _stations[sid] = GetOrCreate(sid) with
        {
            Callsign = Coalesce(GetString(json, "callsign"), GetOrCreate(sid).Callsign),
            GridSquare = Coalesce(GetString(json, "grid_square"), GetOrCreate(sid).GridSquare),
            FrequencyHz = GetLong(json, "freq"),
            LastUpdatedUtc = ParseDate(GetString(json, "last_update")),
        };
        Publish();
    }

    private void HandleTransmitReport(SocketIOResponse response) =>
        HandleTransmitReport(GetJson(response));

    private void HandleTransmitReport(JsonElement json)
    {
        var sid = GetString(json, "sid");
        if (string.IsNullOrWhiteSpace(sid))
        {
            return;
        }

        _stations[sid] = GetOrCreate(sid) with
        {
            Callsign = Coalesce(GetString(json, "callsign"), GetOrCreate(sid).Callsign),
            GridSquare = Coalesce(GetString(json, "grid_square"), GetOrCreate(sid).GridSquare),
            Mode = GetString(json, "mode"),
            IsTransmitting = GetBool(json, "transmitting"),
            LastTransmitUtc = ParseDate(GetString(json, "last_tx")),
            LastUpdatedUtc = ParseDate(GetString(json, "last_update")),
        };
        Publish();
    }

    private void HandleReceiveReport(SocketIOResponse response) =>
        HandleReceiveReport(GetJson(response));

    private void HandleReceiveReport(JsonElement json)
    {
        var receiverSid = GetString(json, "sid");
        if (string.IsNullOrWhiteSpace(receiverSid))
        {
            return;
        }

        _stations[receiverSid] = GetOrCreate(receiverSid) with
        {
            Callsign = Coalesce(GetString(json, "receiver_callsign"), GetOrCreate(receiverSid).Callsign),
            GridSquare = Coalesce(GetString(json, "receiver_grid_square"), GetOrCreate(receiverSid).GridSquare),
            LastHeardCallsign = GetString(json, "callsign"),
            LastHeardSnrDb = GetDouble(json, "snr"),
            LastHeardMode = GetString(json, "mode"),
            LastUpdatedUtc = ParseDate(GetString(json, "last_update")),
        };
        Publish();
    }

    private void HandleMessageUpdate(SocketIOResponse response) =>
        HandleMessageUpdate(GetJson(response));

    private void HandleMessageUpdate(JsonElement json)
    {
        var sid = GetString(json, "sid");
        if (string.IsNullOrWhiteSpace(sid))
        {
            return;
        }

        _stations[sid] = GetOrCreate(sid) with
        {
            Message = GetString(json, "message"),
            LastUpdatedUtc = ParseDate(GetString(json, "last_update")),
        };
        Publish();
    }

    private void HandleBulkUpdate(SocketIOResponse response)
    {
        var json = GetJson(response);
        if (json.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in json.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 2)
            {
                continue;
            }

            var eventName = entry[0].GetString();
            var payload = entry[1];
            switch (eventName)
            {
                case "new_connection":
                    HandleConnection(payload, isRemove: false);
                    break;
                case "remove_connection":
                    HandleConnection(payload, isRemove: true);
                    break;
                case "freq_change":
                    HandleFrequencyChange(payload);
                    break;
                case "tx_report":
                    HandleTransmitReport(payload);
                    break;
                case "rx_report":
                    HandleReceiveReport(payload);
                    break;
                case "message_update":
                    HandleMessageUpdate(payload);
                    break;
            }
        }
    }

    private async Task ReplayStateAsync(CancellationToken ct)
    {
        if (_socket?.Connected != true)
        {
            return;
        }

        if (_lastFrequencyHz > 0)
        {
            await UpdateFrequencyAsync(_lastFrequencyHz, ct).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(_lastMode) || _lastTransmitState)
        {
            await UpdateTransmitAsync(_lastMode, _lastTransmitState, ct).ConfigureAwait(false);
        }
    }

    private async Task DisconnectCoreAsync()
    {
        if (_socket is not null)
        {
            try
            {
                await _socket.DisconnectAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            _socket.Dispose();
            _socket = null;
        }
    }

    private void Publish()
    {
        var rows = _stations.Values
            .Where(station => !string.IsNullOrWhiteSpace(station.Callsign))
            .OrderByDescending(station => station.IsTransmitting)
            .ThenBy(station => station.FrequencyHz ?? long.MaxValue)
            .ThenBy(station => station.Callsign, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _snapshots.OnNext(new FreedvReporterSnapshot(_isConnected, _status, rows));
    }

    private FreedvReporterStation GetOrCreate(string sid) =>
        _stations.TryGetValue(sid, out var station) ? station : FreedvReporterStation.Empty(sid);

    private static FreedvReporterConfiguration NormalizeConfiguration(FreedvReporterConfiguration configuration) =>
        configuration with
        {
            Hostname = string.IsNullOrWhiteSpace(configuration.Hostname) ? DefaultHostname : configuration.Hostname.Trim(),
            Callsign = configuration.Callsign.Trim().ToUpperInvariant(),
            GridSquare = configuration.GridSquare.Trim().ToUpperInvariant(),
            Software = string.IsNullOrWhiteSpace(configuration.Software) ? "ShackStack" : configuration.Software.Trim(),
        };

    private static Dictionary<string, object> BuildAuth(FreedvReporterConfiguration configuration)
    {
        var auth = new Dictionary<string, object>
        {
            ["protocol_version"] = ProtocolVersion,
        };

        if (!configuration.ReportStation || string.IsNullOrWhiteSpace(configuration.Callsign) || string.IsNullOrWhiteSpace(configuration.GridSquare))
        {
            auth["role"] = "view";
            return auth;
        }

        auth["role"] = "report";
        auth["callsign"] = configuration.Callsign;
        auth["grid_square"] = configuration.GridSquare;
        auth["version"] = configuration.Software;
        auth["rx_only"] = configuration.ReceiveOnly;
        auth["os"] = Environment.OSVersion.VersionString;
        return auth;
    }

    private static JsonElement GetJson(SocketIOResponse response) => response.GetValue<JsonElement>();

    private static string GetString(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : string.Empty;

    private static bool GetBool(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static long? GetLong(JsonElement json, string name)
    {
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static double? GetDouble(JsonElement json, string name)
    {
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static DateTimeOffset? ParseDate(string value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;

    private static string Coalesce(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    public void Dispose()
    {
        _ = DisconnectCoreAsync();
        _gate.Dispose();
    }
}
