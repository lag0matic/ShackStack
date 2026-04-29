using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Infrastructure.Interop.Longwave;

public sealed class LongwaveService : ILongwaveService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;

    public LongwaveService()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = static (request, _, _, errors) =>
            {
                if (errors == SslPolicyErrors.None)
                {
                    return true;
                }

                var host = request?.RequestUri?.Host;
                return IsLocalOrPrivateHost(host);
            }
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ShackStack.Avalonia/1.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<LongwaveOperatorContext> GetOperatorContextAsync(LongwaveSettings settings, CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        using var request = CreateRequest(settings, HttpMethod.Get, "me");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Longwave operator lookup failed").ConfigureAwait(false);
        var payload = await ReadJsonAsync<MeResponse>(response, cancellationToken).ConfigureAwait(false);
        return new LongwaveOperatorContext(payload.Id, payload.Username, payload.Callsign.ToUpperInvariant());
    }

    public async Task<IReadOnlyList<LongwaveLogbook>> GetLogbooksAsync(LongwaveSettings settings, CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        using var request = CreateRequest(settings, HttpMethod.Get, "logbooks");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Longwave logbook lookup failed").ConfigureAwait(false);
        var payload = await ReadJsonAsync<List<LogbookResponse>>(response, cancellationToken).ConfigureAwait(false);
        return payload.Select(ToLongwaveLogbook).ToArray();
    }

    public async Task<LongwaveLogbook> CreateLogbookAsync(
        LongwaveSettings settings,
        string name,
        string operatorCallsign,
        string? notes,
        CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        var payload = JsonSerializer.Serialize(new
        {
            name = name.Trim(),
            operator_callsign = operatorCallsign.Trim().ToUpperInvariant(),
            notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        });

        using var request = CreateRequest(settings, HttpMethod.Post, "logbooks", payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Longwave logbook creation failed").ConfigureAwait(false);
        var created = await ReadJsonAsync<LogbookResponse>(response, cancellationToken).ConfigureAwait(false);
        return ToLongwaveLogbook(created);
    }

    public async Task<LongwaveLogbook> UpdateLogbookAsync(
        LongwaveSettings settings,
        string logbookId,
        string name,
        string operatorCallsign,
        string? parkReference,
        string? activationDate,
        string? notes,
        CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        var payload = JsonSerializer.Serialize(new
        {
            name = name.Trim(),
            operator_callsign = operatorCallsign.Trim().ToUpperInvariant(),
            park_reference = string.IsNullOrWhiteSpace(parkReference) ? null : parkReference.Trim().ToUpperInvariant(),
            activation_date = string.IsNullOrWhiteSpace(activationDate) ? null : activationDate.Trim(),
            notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        });

        using var request = CreateRequest(settings, HttpMethod.Patch, $"logbooks/{Uri.EscapeDataString(logbookId.Trim())}", payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Longwave logbook update failed").ConfigureAwait(false);
        var updated = await ReadJsonAsync<LogbookResponse>(response, cancellationToken).ConfigureAwait(false);
        return ToLongwaveLogbook(updated);
    }

    public async Task DeleteLogbookAsync(LongwaveSettings settings, string logbookId, CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        using var request = CreateRequest(settings, HttpMethod.Delete, $"logbooks/{Uri.EscapeDataString(logbookId.Trim())}");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Longwave logbook delete failed").ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LongwaveSpot>> GetPotaSpotsAsync(LongwaveSettings settings, CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        using var request = CreateRequest(settings, HttpMethod.Get, "spots/pota");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Longwave POTA fetch failed").ConfigureAwait(false);
        var payload = await ReadJsonAsync<List<SpotResponse>>(response, cancellationToken).ConfigureAwait(false);
        return payload.Select(ToLongwaveSpot).ToArray();
    }

    public async Task<LongwaveSpot> CreatePotaSpotAsync(
        LongwaveSettings settings,
        LongwavePotaSpotDraft draft,
        CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        var payload = JsonSerializer.Serialize(new
        {
            activator_callsign = draft.ActivatorCallsign.Trim().ToUpperInvariant(),
            park_reference = draft.ParkReference.Trim().ToUpperInvariant(),
            frequency_khz = draft.FrequencyKhz,
            mode = draft.Mode.Trim().ToUpperInvariant(),
            band = draft.Band.Trim(),
            comments = string.IsNullOrWhiteSpace(draft.Comments) ? null : draft.Comments.Trim(),
            spotter_callsign = string.IsNullOrWhiteSpace(draft.SpotterCallsign) ? null : draft.SpotterCallsign.Trim().ToUpperInvariant(),
        });

        using var request = CreateRequest(settings, HttpMethod.Post, "spots/pota", payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Longwave POTA spot post failed").ConfigureAwait(false);
        var created = await ReadJsonAsync<SpotResponse>(response, cancellationToken).ConfigureAwait(false);
        return ToLongwaveSpot(created);
    }

    public async Task<LongwaveCallsignLookup> LookupCallsignAsync(LongwaveSettings settings, string callsign, CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        var normalized = callsign.Trim().ToUpperInvariant();
        using var request = CreateRequest(settings, HttpMethod.Get, $"lookups/qrz/{Uri.EscapeDataString(normalized)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"Longwave QRZ lookup failed for {normalized}").ConfigureAwait(false);
        var payload = await ReadJsonAsync<CallsignLookupResponse>(response, cancellationToken).ConfigureAwait(false);
        return new LongwaveCallsignLookup(
            payload.Callsign.ToUpperInvariant(),
            payload.Name,
            payload.Qth,
            payload.County,
            payload.GridSquare,
            payload.Country,
            payload.State,
            payload.Dxcc,
            payload.Latitude,
            payload.Longitude,
            payload.QrzUrl);
    }

    public async Task<IReadOnlyList<LongwaveContact>> GetContactsAsync(
        LongwaveSettings settings,
        string? logbookId,
        CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        var relativePath = string.IsNullOrWhiteSpace(logbookId)
            ? "contacts"
            : $"contacts?logbook_id={Uri.EscapeDataString(logbookId.Trim())}";
        using var request = CreateRequest(settings, HttpMethod.Get, relativePath);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Longwave contacts fetch failed").ConfigureAwait(false);
        var payload = await ReadJsonAsync<List<ContactResponse>>(response, cancellationToken).ConfigureAwait(false);
        return payload.Select(ToLongwaveContact).ToArray();
    }

    public async Task<LongwaveLogbook> GetOrCreateLogbookAsync(
        LongwaveSettings settings,
        string operatorCallsign,
        CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        var normalizedCall = operatorCallsign.Trim().ToUpperInvariant();
        var targetName = settings.DefaultLogbookName.Trim();

        var existing = await GetLogbooksAsync(settings, cancellationToken).ConfigureAwait(false);
        var match = existing.FirstOrDefault(logbook =>
                string.Equals(logbook.Name, targetName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(logbook.OperatorCallsign, normalizedCall, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            return match;
        }

        var createBody = JsonSerializer.Serialize(
            new
            {
                name = targetName,
                operator_callsign = normalizedCall,
                notes = settings.DefaultLogbookNotes.Trim(),
            });

        using var createRequest = CreateRequest(settings, HttpMethod.Post, "logbooks", createBody);
        using var createResponse = await _httpClient.SendAsync(createRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(createResponse, "Longwave logbook creation failed").ConfigureAwait(false);
        var created = await ReadJsonAsync<LogbookResponse>(createResponse, cancellationToken).ConfigureAwait(false);
        return ToLongwaveLogbook(created);
    }

    public async Task<LongwaveContact> CreateContactAsync(
        LongwaveSettings settings,
        LongwaveContactDraft draft,
        CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        var payload = JsonSerializer.Serialize(BuildContactPayload(draft, includeLogbookId: true));
        using var request = CreateRequest(settings, HttpMethod.Post, "contacts", payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Longwave contact create failed").ConfigureAwait(false);
        var created = await ReadJsonAsync<ContactResponse>(response, cancellationToken).ConfigureAwait(false);
        return ToLongwaveContact(created);
    }

    public async Task<LongwaveContact> UpdateContactAsync(
        LongwaveSettings settings,
        string contactId,
        LongwaveContactDraft draft,
        CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        var payload = JsonSerializer.Serialize(BuildContactPayload(draft, includeLogbookId: false));
        using var request = CreateRequest(settings, HttpMethod.Patch, $"contacts/{Uri.EscapeDataString(contactId.Trim())}", payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Longwave contact update failed").ConfigureAwait(false);
        var updated = await ReadJsonAsync<ContactResponse>(response, cancellationToken).ConfigureAwait(false);
        return ToLongwaveContact(updated);
    }

    public async Task DeleteContactAsync(LongwaveSettings settings, string contactId, CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        using var request = CreateRequest(settings, HttpMethod.Delete, $"contacts/{Uri.EscapeDataString(contactId.Trim())}");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Longwave contact delete failed").ConfigureAwait(false);
    }

    public async Task<LongwaveQrzUploadResult> UploadLogbookToQrzAsync(
        LongwaveSettings settings,
        string logbookId,
        CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        var payload = JsonSerializer.Serialize(new
        {
            logbook_id = logbookId.Trim(),
        });

        using var request = CreateRequest(settings, HttpMethod.Post, "logs/qrz-upload", payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Longwave QRZ upload failed").ConfigureAwait(false);
        var result = await ReadJsonAsync<QrzUploadResponse>(response, cancellationToken).ConfigureAwait(false);
        return new LongwaveQrzUploadResult(result.LogbookId, result.Uploaded, result.Message);
    }

    public async Task<string> ExportLogbookAdifAsync(
        LongwaveSettings settings,
        string logbookId,
        CancellationToken cancellationToken)
    {
        ValidateSettings(settings);
        using var request = CreateRequest(settings, HttpMethod.Get, $"logs/{Uri.EscapeDataString(logbookId.Trim())}/adif");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Longwave ADIF export failed").ConfigureAwait(false);
        var result = await ReadJsonAsync<AdifExportResponse>(response, cancellationToken).ConfigureAwait(false);
        return result.Adif;
    }

    public void Dispose() => _httpClient.Dispose();

    private static LongwaveLogbook ToLongwaveLogbook(LogbookResponse response) =>
        new(
            response.Id,
            response.Name,
            response.OperatorCallsign.ToUpperInvariant(),
            response.ParkReference,
            response.ActivationDate,
            response.Notes,
            response.ContactCount);

    private static LongwaveSpot ToLongwaveSpot(SpotResponse spot) =>
        new(
            spot.Id,
            spot.Source,
            spot.ActivatorCallsign.ToUpperInvariant(),
            spot.ParkReference.ToUpperInvariant(),
            spot.FrequencyKhz,
            spot.Mode.ToUpperInvariant(),
            spot.Band,
            spot.Comments,
            string.IsNullOrWhiteSpace(spot.SpotterCallsign) ? null : spot.SpotterCallsign.ToUpperInvariant(),
            spot.SpottedAt,
            spot.Latitude,
            spot.Longitude);

    private static LongwaveContact ToLongwaveContact(ContactResponse created) =>
        new(
            created.Id,
            created.LogbookId,
            created.StationCallsign.ToUpperInvariant(),
            created.OperatorCallsign.ToUpperInvariant(),
            created.QsoDate,
            created.TimeOn,
            created.Band,
            created.Mode,
            created.FrequencyKhz,
            created.ParkReference,
            created.RstSent,
            created.RstReceived,
            created.Name,
            created.Qth,
            created.County,
            created.GridSquare,
            created.Country,
            created.State,
            created.Dxcc,
            created.QrzUploadStatus,
            created.QrzUploadDate,
            created.Latitude,
            created.Longitude,
            created.SourceSpotId);

    private static object BuildContactPayload(LongwaveContactDraft draft, bool includeLogbookId) =>
        includeLogbookId
            ? new
            {
                logbook_id = draft.LogbookId,
                station_callsign = draft.StationCallsign.Trim().ToUpperInvariant(),
                operator_callsign = draft.OperatorCallsign.Trim().ToUpperInvariant(),
                qso_date = draft.QsoDate,
                time_on = draft.TimeOn,
                band = draft.Band,
                mode = draft.Mode,
                frequency_khz = draft.FrequencyKhz,
                park_reference = string.IsNullOrWhiteSpace(draft.ParkReference) ? null : draft.ParkReference.Trim().ToUpperInvariant(),
                rst_sent = string.IsNullOrWhiteSpace(draft.RstSent) ? null : draft.RstSent.Trim().ToUpperInvariant(),
                rst_recvd = string.IsNullOrWhiteSpace(draft.RstReceived) ? null : draft.RstReceived.Trim().ToUpperInvariant(),
                name = string.IsNullOrWhiteSpace(draft.Name) ? null : draft.Name.Trim(),
                qth = string.IsNullOrWhiteSpace(draft.Qth) ? null : draft.Qth.Trim(),
                county = string.IsNullOrWhiteSpace(draft.County) ? null : draft.County.Trim(),
                grid_square = string.IsNullOrWhiteSpace(draft.GridSquare) ? null : draft.GridSquare.Trim().ToUpperInvariant(),
                country = string.IsNullOrWhiteSpace(draft.Country) ? null : draft.Country.Trim(),
                state = string.IsNullOrWhiteSpace(draft.State) ? null : draft.State.Trim().ToUpperInvariant(),
                dxcc = string.IsNullOrWhiteSpace(draft.Dxcc) ? null : draft.Dxcc.Trim(),
                lat = draft.Latitude,
                lon = draft.Longitude,
                source_spot_id = string.IsNullOrWhiteSpace(draft.SourceSpotId) ? null : draft.SourceSpotId.Trim(),
            }
            : new
            {
                station_callsign = draft.StationCallsign.Trim().ToUpperInvariant(),
                operator_callsign = draft.OperatorCallsign.Trim().ToUpperInvariant(),
                qso_date = draft.QsoDate,
                time_on = draft.TimeOn,
                band = draft.Band,
                mode = draft.Mode,
                frequency_khz = draft.FrequencyKhz,
                park_reference = string.IsNullOrWhiteSpace(draft.ParkReference) ? null : draft.ParkReference.Trim().ToUpperInvariant(),
                rst_sent = string.IsNullOrWhiteSpace(draft.RstSent) ? null : draft.RstSent.Trim().ToUpperInvariant(),
                rst_recvd = string.IsNullOrWhiteSpace(draft.RstReceived) ? null : draft.RstReceived.Trim().ToUpperInvariant(),
                name = string.IsNullOrWhiteSpace(draft.Name) ? null : draft.Name.Trim(),
                qth = string.IsNullOrWhiteSpace(draft.Qth) ? null : draft.Qth.Trim(),
                county = string.IsNullOrWhiteSpace(draft.County) ? null : draft.County.Trim(),
                grid_square = string.IsNullOrWhiteSpace(draft.GridSquare) ? null : draft.GridSquare.Trim().ToUpperInvariant(),
                country = string.IsNullOrWhiteSpace(draft.Country) ? null : draft.Country.Trim(),
                state = string.IsNullOrWhiteSpace(draft.State) ? null : draft.State.Trim().ToUpperInvariant(),
                dxcc = string.IsNullOrWhiteSpace(draft.Dxcc) ? null : draft.Dxcc.Trim(),
                lat = draft.Latitude,
                lon = draft.Longitude,
                source_spot_id = string.IsNullOrWhiteSpace(draft.SourceSpotId) ? null : draft.SourceSpotId.Trim(),
            };

    private static void ValidateSettings(LongwaveSettings settings)
    {
        if (!settings.Enabled)
        {
            throw new InvalidOperationException("Longwave integration is disabled.");
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException("Enter a Longwave base URL in Settings.");
        }

        if (string.IsNullOrWhiteSpace(settings.ClientApiToken))
        {
            throw new InvalidOperationException("Enter a Longwave client API token in Settings.");
        }
    }

    private HttpRequestMessage CreateRequest(LongwaveSettings settings, HttpMethod method, string relativePath, string? jsonBody = null)
    {
        var baseUrl = settings.BaseUrl.Trim().TrimEnd('/');
        if (!baseUrl.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = $"{baseUrl}/api/v1";
        }

        var request = new HttpRequestMessage(method, $"{baseUrl}/{relativePath}");
        request.Headers.Add("X-Api-Key", settings.ClientApiToken.Trim());

        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string prefix)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = response.ReasonPhrase ?? "unknown error";
        }

        throw new InvalidOperationException($"{prefix}: {detail.Trim()}");
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            throw new InvalidOperationException("Longwave returned an empty response.");
        }

        return payload;
    }

    private static bool IsLocalOrPrivateHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
               && (bytes[0], bytes[1]) switch
               {
                   (10, _) => true,
                   (172, >= 16 and <= 31) => true,
                   (192, 168) => true,
                   _ => false,
               };
    }

    private sealed record MeResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("callsign")] string Callsign);

    private sealed record LogbookResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("operator_callsign")] string OperatorCallsign,
        [property: JsonPropertyName("park_reference")] string? ParkReference,
        [property: JsonPropertyName("activation_date")] string? ActivationDate,
        [property: JsonPropertyName("notes")] string? Notes,
        [property: JsonPropertyName("contact_count")] int ContactCount);

    private sealed record SpotResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("activator_callsign")] string ActivatorCallsign,
        [property: JsonPropertyName("park_reference")] string ParkReference,
        [property: JsonPropertyName("frequency_khz")] double FrequencyKhz,
        [property: JsonPropertyName("mode")] string Mode,
        [property: JsonPropertyName("band")] string Band,
        [property: JsonPropertyName("comments")] string? Comments,
        [property: JsonPropertyName("spotter_callsign")] string? SpotterCallsign,
        [property: JsonPropertyName("spotted_at")] DateTime SpottedAt,
        [property: JsonPropertyName("lat")] double? Latitude,
        [property: JsonPropertyName("lon")] double? Longitude);

    private sealed record ContactResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("logbook_id")] string LogbookId,
        [property: JsonPropertyName("station_callsign")] string StationCallsign,
        [property: JsonPropertyName("operator_callsign")] string OperatorCallsign,
        [property: JsonPropertyName("qso_date")] string QsoDate,
        [property: JsonPropertyName("time_on")] string TimeOn,
        [property: JsonPropertyName("band")] string Band,
        [property: JsonPropertyName("mode")] string Mode,
        [property: JsonPropertyName("frequency_khz")] double FrequencyKhz,
        [property: JsonPropertyName("park_reference")] string? ParkReference,
        [property: JsonPropertyName("rst_sent")] string? RstSent,
        [property: JsonPropertyName("rst_recvd")] string? RstReceived,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("qth")] string? Qth,
        [property: JsonPropertyName("county")] string? County,
        [property: JsonPropertyName("grid_square")] string? GridSquare,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("dxcc")] string? Dxcc,
        [property: JsonPropertyName("qrz_upload_status")] string? QrzUploadStatus,
        [property: JsonPropertyName("qrz_upload_date")] string? QrzUploadDate,
        [property: JsonPropertyName("lat")] double? Latitude,
        [property: JsonPropertyName("lon")] double? Longitude,
        [property: JsonPropertyName("source_spot_id")] string? SourceSpotId);

    private sealed record QrzUploadResponse(
        [property: JsonPropertyName("logbook_id")] string LogbookId,
        [property: JsonPropertyName("uploaded")] bool Uploaded,
        [property: JsonPropertyName("message")] string Message);

    private sealed record AdifExportResponse(
        [property: JsonPropertyName("adif")] string Adif);

    private sealed record CallsignLookupResponse(
        [property: JsonPropertyName("callsign")] string Callsign,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("qth")] string? Qth,
        [property: JsonPropertyName("county")] string? County,
        [property: JsonPropertyName("grid_square")] string? GridSquare,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("dxcc")] string? Dxcc,
        [property: JsonPropertyName("lat")] double? Latitude,
        [property: JsonPropertyName("lon")] double? Longitude,
        [property: JsonPropertyName("qrz_url")] string? QrzUrl);
}
