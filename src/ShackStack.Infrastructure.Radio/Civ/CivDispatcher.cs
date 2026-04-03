using System.Collections.Concurrent;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Radio.Civ;

public sealed class CivDispatcher
{
    private readonly ConcurrentDictionary<Guid, PendingRequest> _pending = new();
    private readonly SimpleSubject<CivFrame> _unsolicited = new();
    private readonly SimpleSubject<CivFrame> _streamData = new();

    public IObservable<CivFrame> UnsolicitedFrames => _unsolicited;
    public IObservable<CivFrame> StreamFrames => _streamData;

    public Guid RegisterPending(Func<CivFrame, bool> matcher, TaskCompletionSource<CivFrame?> completion, TimeSpan timeout)
    {
        var id = Guid.NewGuid();
        _pending[id] = new PendingRequest(matcher, completion, DateTimeOffset.UtcNow.Add(timeout));
        return id;
    }

    public void RemovePending(Guid id)
    {
        _pending.TryRemove(id, out _);
    }

    public void Dispatch(CivFrame frame)
    {
        var classified = Classify(frame);

        foreach (var pair in _pending)
        {
            if (pair.Value.Matcher(classified))
            {
                if (_pending.TryRemove(pair.Key, out var pending))
                {
                    pending.Completion.TrySetResult(classified);
                    return;
                }
            }
        }

        if (classified.Kind == CivFrameKind.StreamData)
        {
            _streamData.OnNext(classified);
            return;
        }

        _unsolicited.OnNext(classified);
    }

    public void FailAll(Exception exception)
    {
        foreach (var pair in _pending)
        {
            if (_pending.TryRemove(pair.Key, out var pending))
            {
                pending.Completion.TrySetException(exception);
            }
        }
    }

    public void SweepExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _pending)
        {
            if (pair.Value.ExpiresAt <= now && _pending.TryRemove(pair.Key, out var pending))
            {
                pending.Completion.TrySetException(new TimeoutException("CI-V request timed out."));
            }
        }
    }

    private static CivFrame Classify(CivFrame frame)
    {
        if (frame.Command == 0xFB)
        {
            return frame with { Kind = CivFrameKind.Acknowledge };
        }

        if (frame.Command == 0xFA)
        {
            return frame with { Kind = CivFrameKind.NegativeAcknowledge };
        }

        if (frame.Command == 0x27)
        {
            return frame with { Kind = CivFrameKind.StreamData };
        }

        return frame with { Kind = CivFrameKind.UnsolicitedEvent };
    }
}
