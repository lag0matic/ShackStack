using System.IO.Ports;
using System.Threading.Channels;

namespace ShackStack.Infrastructure.Radio.Civ;

public sealed class CivConnection : IAsyncDisposable
{
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
            ReadTimeout = -1,
            WriteTimeout = -1,
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
        if (_cts is not null)
        {
            _cts.Cancel();
        }

        await Task.WhenAll(
            _readerTask ?? Task.CompletedTask,
            _writerTask ?? Task.CompletedTask,
            _timeoutSweepTask ?? Task.CompletedTask).ConfigureAwait(false);

        if (_serialPort is not null)
        {
            _serialPort.DtrEnable = false;
        }

        _serialPort?.Dispose();
        _serialPort = null;
        _cts?.Dispose();
        _cts = null;
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
        var completion = new TaskCompletionSource<CivFrame?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new OutboundCommand(frameBytes, matcher, completion);
        await _writeQueue.Writer.WriteAsync(command, cancellationToken).ConfigureAwait(false);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return await completion.Task.ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _serialPort is not null)
            {
                var bytesRead = await _serialPort.BaseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
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
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _writeQueue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_writeQueue.Reader.TryRead(out var command))
                {
                    if (_serialPort is null)
                    {
                        command.Completion.TrySetException(new InvalidOperationException("Serial port is not open."));
                        continue;
                    }

                    Guid? pendingId = null;
                    if (command.ResponseMatcher is not null)
                    {
                        pendingId = _dispatcher.RegisterPending(command.ResponseMatcher, command.Completion, TimeSpan.FromMilliseconds(750));
                    }

                    await _serialPort.BaseStream.WriteAsync(command.FrameBytes, cancellationToken).ConfigureAwait(false);
                    await _serialPort.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);

                    if (command.ResponseMatcher is null)
                    {
                        command.Completion.TrySetResult(null);
                    }

                    _ = pendingId;
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
