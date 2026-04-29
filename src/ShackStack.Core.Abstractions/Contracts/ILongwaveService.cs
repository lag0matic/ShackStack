using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface ILongwaveService
{
    Task<LongwaveOperatorContext> GetOperatorContextAsync(LongwaveSettings settings, CancellationToken cancellationToken);

    Task<IReadOnlyList<LongwaveLogbook>> GetLogbooksAsync(LongwaveSettings settings, CancellationToken cancellationToken);

    Task<LongwaveLogbook> CreateLogbookAsync(
        LongwaveSettings settings,
        string name,
        string operatorCallsign,
        string? notes,
        CancellationToken cancellationToken);

    Task<LongwaveLogbook> UpdateLogbookAsync(
        LongwaveSettings settings,
        string logbookId,
        string name,
        string operatorCallsign,
        string? parkReference,
        string? activationDate,
        string? notes,
        CancellationToken cancellationToken);

    Task DeleteLogbookAsync(LongwaveSettings settings, string logbookId, CancellationToken cancellationToken);

    Task<IReadOnlyList<LongwaveSpot>> GetPotaSpotsAsync(LongwaveSettings settings, CancellationToken cancellationToken);

    Task<LongwaveSpot> CreatePotaSpotAsync(
        LongwaveSettings settings,
        LongwavePotaSpotDraft draft,
        CancellationToken cancellationToken);

    Task<LongwaveCallsignLookup> LookupCallsignAsync(LongwaveSettings settings, string callsign, CancellationToken cancellationToken);

    Task<IReadOnlyList<LongwaveContact>> GetContactsAsync(
        LongwaveSettings settings,
        string? logbookId,
        CancellationToken cancellationToken);

    Task<LongwaveLogbook> GetOrCreateLogbookAsync(
        LongwaveSettings settings,
        string operatorCallsign,
        CancellationToken cancellationToken);

    Task<LongwaveContact> CreateContactAsync(
        LongwaveSettings settings,
        LongwaveContactDraft draft,
        CancellationToken cancellationToken);

    Task<LongwaveContact> UpdateContactAsync(
        LongwaveSettings settings,
        string contactId,
        LongwaveContactDraft draft,
        CancellationToken cancellationToken);

    Task DeleteContactAsync(LongwaveSettings settings, string contactId, CancellationToken cancellationToken);

    Task<LongwaveQrzUploadResult> UploadLogbookToQrzAsync(
        LongwaveSettings settings,
        string logbookId,
        CancellationToken cancellationToken);

    Task<string> ExportLogbookAdifAsync(
        LongwaveSettings settings,
        string logbookId,
        CancellationToken cancellationToken);
}
