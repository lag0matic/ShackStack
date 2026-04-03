namespace ShackStack.Core.Abstractions.Utilities;

public sealed class SimpleSubject<T> : IObservable<T>
{
    private readonly object _gate = new();
    private readonly List<IObserver<T>> _observers = [];

    public IDisposable Subscribe(IObserver<T> observer)
    {
        lock (_gate)
        {
            _observers.Add(observer);
        }

        return new Subscription(this, observer);
    }

    public void OnNext(T value)
    {
        IObserver<T>[] observers;
        lock (_gate)
        {
            observers = _observers.ToArray();
        }

        foreach (var observer in observers)
        {
            observer.OnNext(value);
        }
    }

    public void OnError(Exception error)
    {
        IObserver<T>[] observers;
        lock (_gate)
        {
            observers = _observers.ToArray();
        }

        foreach (var observer in observers)
        {
            observer.OnError(error);
        }
    }

    public void OnCompleted()
    {
        IObserver<T>[] observers;
        lock (_gate)
        {
            observers = _observers.ToArray();
            _observers.Clear();
        }

        foreach (var observer in observers)
        {
            observer.OnCompleted();
        }
    }

    private void Unsubscribe(IObserver<T> observer)
    {
        lock (_gate)
        {
            _observers.Remove(observer);
        }
    }

    private sealed class Subscription(SimpleSubject<T> owner, IObserver<T> observer) : IDisposable
    {
        public void Dispose()
        {
            owner.Unsubscribe(observer);
        }
    }
}
