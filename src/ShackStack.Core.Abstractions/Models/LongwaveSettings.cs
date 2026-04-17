namespace ShackStack.Core.Abstractions.Models;

public sealed record LongwaveSettings(
    bool Enabled,
    string BaseUrl,
    string ClientApiToken,
    string DefaultLogbookName,
    string DefaultLogbookNotes
);
