namespace ShackStack.Infrastructure.Radio.Civ;

public sealed record CivFrame(
    byte[] RawBytes,
    byte Destination,
    byte Source,
    byte Command,
    byte? SubCommand,
    byte[] Payload,
    CivFrameKind Kind = CivFrameKind.Unknown
);
