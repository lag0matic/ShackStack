namespace ShackStack.Core.Abstractions.Models;

public sealed record LongwaveQrzUploadResult(
    string LogbookId,
    bool Uploaded,
    string Message
);
