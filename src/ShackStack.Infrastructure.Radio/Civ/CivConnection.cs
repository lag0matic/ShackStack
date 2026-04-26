using System.IO.Ports;
using System.Threading.Channels;

namespace ShackStack.Infrastructure.Radio.Civ;

public sealed class CivConnection : IAsyncDisposable
{
    private static readonly TimeSpan CloseTaskWaitTimeout = TimeSpan.FromSeconds(2);
    private readonly CivParser _parser = new();
    private readonly CivDispatcher _dispatcher;
    private readonly Channel<OutboundCommand> _writeQueue;
    private SerialPort? _serialPort;
    private CancellationTokenSource? _cts;
    private Task? _readerTask;
    private Task? _writerTask;
    private Task? _timeoutSweepTask;

    public CivConnection(CivDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _writeQueue = Channel.CreateBounded<OutboundCommand>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public bool IsConnected => _serialPort?.IsOpen == true;

    public async Task OpenAsync(string portName, int baudRate, CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        _serialPort = new SerialPort(portName, baudRate)
        {
            ReadTimeout = 250,
            WriteTimeout = 1000,
            DtrEnable = false,
            RtsEnable = false,
        };
        _serialPort.Open();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readerTask = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
        _writerTask = Task.Run(() => WriteLoopAsync(_cts.Token), _cts.Token);
        _timeoutSweepTask = Task.Run(() => TimeoutSweepLoopAsync(_cts.Token), _cts.Token);
        await Task.CompletedTask;
    }

    public async Task CloseAsync()
    {
        var serialPort = _serialPort;
        _serialPort = null;

        if (_cts is not null)
        {
            _cts.Cancel();
        }

        while (_writeQueue.Reader.TryRead(out var command))
        {
            command.Completion.TrySetCanceled();
        }

        if (serialPort is not null)
        {
            try
            {
                serialPort.DtrEnable = false;
            }
            catch
            {
                // Best effort during shutdown; the port may already be gone.
            }

            serialPort.Dispose();
        }

        _dispatcher.FailAll(new OperationCanceledException("CI-V connection closed."));

        try
        {
            await Task.WhenAll(
                    _readerTask ?? Task.CompletedTask,
                    _writerTask ?? Task.CompletedTask,
                    _timeoutSweepTask ?? Task.CompletedTask)
                .WaitAsync(CloseTaskWaitTimeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // SerialPort can be stubborn on Windows. The port has already been
            // disposed, so do not let shutdown strand the UI.
        }
        catch (OperationCanceledException)
        {
        }

        _cts?.Dispose();
        _cts = null;
        _readerTask = null;
        _writerTask = null;
        _timeoutSweepTask = null;
    }

    public Task SetDtrAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_serialPort is null || !_serialPort.IsOpen)
        {
            throw new InvalidOperationException("Serial port is not open.");
        }

        _serialPort.DtrEnable = enabled;
        return Task.CompletedTask;
    }

    public async Task<CivFrame?> SendAsync(byte[] frameBytes, Func<CivFrame, bool>? matcher, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout + TimeSpan.FromSeconds(2));
        var completion = new TaskCompletionSource<CivFrame?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new OutboundCommand(frameBytes, matcher, completion);
        await _writeQueue.Writer.WriteAsync(command, timeoutCts.Token).ConfigureAwait(false);
        using var registration = timeoutCts.Token.Register(() => completion.TrySetCanceled(timeoutCts.Token));
        return await completion.Task.ConfigureAwait(false);
    }

    private Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _serialPort is not null)
            {
                int bytesRead;
                try
                {
                    bytesRead = _serialPort.Read(buffer, 0, buffer.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }

                if (bytesRead <= 0)
                {
                    continue;
                }

                foreach (var frame in _parser.Feed(buffer.AsSpan(0, bytesRead)))
                {
                    _dispatcher.Dispatch(frame);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _dispatcher.FailAll(ex);
        }

        return Task.CompletedTask;
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _writeQueue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_writeQueue.Reader.TryRead(out var command))
                {
                    if (command.Completion.Task.IsCompleted)
                    {
                        continue;
                    }

                    if (_serialPort is null)
                    {
                        command.Completion.TrySetException(new InvalidOperationException("Serial port is not open."));
                        continue;
                    }

                    Guid? pendingId = null;
                    try
                    {
                        if (command.ResponseMatcher is not null)
                        {
                            pendingId = _dispatcher.RegisterPending(command.ResponseMatcher, command.Completion, TimeSpan.FromMilliseconds(750));
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        _serialPort.Write(command.FrameBytes, 0, command.FrameBytes.Length);

                        if (command.ResponseMatcher is null)
                        {
                            command.Completion.TrySetResult(null);
                        }
                    }
                    catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or IOException)
                    {
                        if (pendingId is Guid id)
                        {
                            _dispatcher.RemovePending(id);
                        }

                        command.Completion.TrySetException(ex);
                        _dispatcher.FailAll(ex);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _dispatcher.FailAll(ex);
        }
    }

    private async Task TimeoutSweepLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _dispatcher.SweepExpired();
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
    }
}
