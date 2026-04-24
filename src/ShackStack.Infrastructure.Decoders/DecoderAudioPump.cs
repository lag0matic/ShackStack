using System.Threading.Channels;
using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Infrastructure.Decoders;

internal sealed class DecoderAudioPump : IDisposable
{
    private readonly Func<AudioBuffer, CancellationToken, Task> _sendAsync;
    private readonly Func<bool> _isRunning;
    private readonly Channel<AudioBuffer> _audioQueue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pumpTask;

    public DecoderAudioPump(Func<AudioBuffer, CancellationToken, Task> sendAsync, Func<bool> isRunning, int capacity = 2)
    {
        _sendAsync = sendAsync;
        _isRunning = isRunning;
        _audioQueue = Channel.CreateBounded<AudioBuffer>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _pumpTask = Task.Run(() => PumpAsync(_cts.Token));
    }

    public void Enqueue(AudioBuffer buffer)
    {
        if (!_isRunning())
        {
            return;
        }

        var copiedSamples = new float[buffer.Samples.Length];
        Array.Copy(buffer.Samples, copiedSamples, copiedSamples.Length);
        _audioQueue.Writer.TryWrite(new AudioBuffer(copiedSamples, buffer.SampleRate, buffer.Channels));
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var buffer in _audioQueue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (!_isRunning())
                {
                    continue;
                }

                await _sendAsync(buffer, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _audioQueue.Writer.TryComplete();
        _cts.Cancel();
        try
        {
            _pumpTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        _cts.Dispose();
    }
}
