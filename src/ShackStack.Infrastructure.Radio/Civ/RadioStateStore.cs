using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Radio.Civ;

public sealed class RadioStateStore
{
    private readonly object _gate = new();
    private readonly SimpleSubject<RadioState> _stream = new();
    private RadioState _current = new(false, 0L, 0L, 0L, RadioMode.Usb, false, false, false, 0, 2, 2400, 0, 0, false, false, false, 0, false, false, 1, 128, false, false, 50, 0, 100, 700, 20);

    public RadioState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public IObservable<RadioState> StateStream => _stream;

    public void Update(RadioState next)
    {
        lock (_gate)
        {
            _current = next;
        }

        _stream.OnNext(next);
    }

    public void Update(Func<RadioState, RadioState> updater)
    {
        RadioState next;
        lock (_gate)
        {
            next = updater(_current);
            _current = next;
        }

        _stream.OnNext(next);
    }
}
