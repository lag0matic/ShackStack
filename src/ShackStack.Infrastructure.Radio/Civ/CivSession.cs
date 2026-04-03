using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Infrastructure.Radio.Civ;

public sealed class CivSession : IAsyncDisposable
{
    private readonly CivDispatcher _dispatcher;
    private readonly CivConnection _connection;
    private readonly RadioStateStore _stateStore;
    private CancellationTokenSource? _reconcileCts;
    private Task? _reconcileTask;

    public CivSession(CivDispatcher dispatcher, CivConnection connection, RadioStateStore stateStore)
    {
        _dispatcher = dispatcher;
        _connection = connection;
        _stateStore = stateStore;
    }

    public IObservable<RadioState> StateStream => _stateStore.StateStream;

    public Task ConnectAsync(RadioConnectionOptions options, CancellationToken cancellationToken) =>
        _connection.OpenAsync(options.PortName, options.BaudRate, cancellationToken);

    public async Task DisconnectAsync()
    {
        if (_reconcileCts is not null)
        {
            _reconcileCts.Cancel();
        }

        if (_reconcileTask is not null)
        {
            await _reconcileTask.ConfigureAwait(false);
        }

        await _connection.CloseAsync().ConfigureAwait(false);
    }

    public Task<CivFrame?> SendCommandAsync(byte destination, byte source, byte command, ReadOnlySpan<byte> payload, Func<CivFrame, bool>? matcher, CancellationToken cancellationToken)
    {
        var frame = CivCodec.Encode(destination, source, command, payload);
        return _connection.SendAsync(frame, matcher, TimeSpan.FromMilliseconds(750), cancellationToken);
    }

    public Task SetDtrAsync(bool enabled, CancellationToken cancellationToken) =>
        _connection.SetDtrAsync(enabled, cancellationToken);

    public void StartReconciliationLoop(Func<CancellationToken, Task> reconcile, CancellationToken cancellationToken, TimeSpan interval)
    {
        _reconcileCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _reconcileTask = Task.Run(async () =>
        {
            try
            {
                while (!_reconcileCts.IsCancellationRequested)
                {
                    await Task.Delay(interval, _reconcileCts.Token).ConfigureAwait(false);
                    await reconcile(_reconcileCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, _reconcileCts.Token);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
