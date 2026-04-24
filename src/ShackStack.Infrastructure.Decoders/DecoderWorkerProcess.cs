using System.Diagnostics;
using System.Text.Json;

namespace ShackStack.Infrastructure.Decoders;

internal sealed class DecoderWorkerProcess : IDisposable
{
    private readonly DecoderWorkerLaunch _launch;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _stdoutTask;
    private Task? _stderrTask;

    public DecoderWorkerProcess(DecoderWorkerLaunch launch)
    {
        _launch = launch;
    }

    public bool Exists => _launch.Exists;

    public string DisplayPath => _launch.DisplayPath;

    public bool IsStarted => _process is not null && !_process.HasExited && _stdin is not null;

    public Task EnsureStartedAsync(
        Func<string, Task> handleStdoutLineAsync,
        Func<string, Task> handleStderrLineAsync,
        Action onExited,
        CancellationToken ct)
    {
        if (!_launch.Exists || IsStarted)
        {
            return Task.CompletedTask;
        }

        var process = new Process
        {
            StartInfo = BundledDecoderWorkerLocator.CreateStartInfo(_launch),
            EnableRaisingEvents = true,
        };
        process.Exited += (_, _) => onExited();
        process.Start();

        _process = process;
        _stdin = process.StandardInput;
        _stdin.AutoFlush = true;
        _stdoutTask = Task.Run(() => ReadLineLoopAsync(process.StandardOutput, handleStdoutLineAsync), ct);
        _stderrTask = Task.Run(() => ReadLineLoopAsync(process.StandardError, handleStderrLineAsync), ct);
        return Task.CompletedTask;
    }

    public async Task SendJsonAsync<T>(T payload, CancellationToken ct)
    {
        if (_stdin is null)
        {
            return;
        }

        var line = JsonSerializer.Serialize(payload, _jsonOptions);
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stdin.WriteLineAsync(line).ConfigureAwait(false);
            await _stdin.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static async Task ReadLineLoopAsync(TextReader reader, Func<string, Task> handleLineAsync)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            await handleLineAsync(line).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        DecoderHostProcessCleanup.Shutdown(_process, _stdin, _stdoutTask, _stderrTask, _writeGate);
        _writeGate.Dispose();
    }
}
