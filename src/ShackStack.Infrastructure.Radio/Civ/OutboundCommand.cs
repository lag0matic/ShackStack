namespace ShackStack.Infrastructure.Radio.Civ;

public sealed record OutboundCommand(
    byte[] FrameBytes,
    Func<CivFrame, bool>? ResponseMatcher,
    TaskCompletionSource<CivFrame?> Completion,
    bool HighPriority = false
);
