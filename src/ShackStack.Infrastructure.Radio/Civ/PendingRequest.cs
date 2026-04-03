namespace ShackStack.Infrastructure.Radio.Civ;

internal sealed class PendingRequest(
    Func<CivFrame, bool> matcher,
    TaskCompletionSource<CivFrame?> completion,
    DateTimeOffset expiresAt)
{
    public Func<CivFrame, bool> Matcher { get; } = matcher;
    public TaskCompletionSource<CivFrame?> Completion { get; } = completion;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
}
