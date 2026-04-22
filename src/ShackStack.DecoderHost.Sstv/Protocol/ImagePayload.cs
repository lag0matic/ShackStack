namespace ShackStack.DecoderHost.Sstv.Protocol;

internal sealed record ImagePayload(
    string Type,
    string Status,
    string? ImagePath);

