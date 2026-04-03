namespace ShackStack.Core.Abstractions.Utilities;

public sealed class Observer<T>(Action<T> onNext) : IObserver<T>
{
    public void OnNext(T value) => onNext(value);

    public void OnError(Exception error)
    {
    }

    public void OnCompleted()
    {
    }
}
