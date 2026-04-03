using System.Net;

namespace ShackStack.Infrastructure.Interop.Flrig;

public sealed class FlrigHttpServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly FlrigMethodDispatcher _dispatcher;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public FlrigHttpServer(FlrigMethodDispatcher dispatcher, string host, int port)
    {
        _dispatcher = dispatcher;
        var baseUrl = BuildBaseUrl(host, port);
        _listener.Prefixes.Add(baseUrl);
        _listener.Prefixes.Add($"{baseUrl}RPC2/");
    }

    private static string BuildBaseUrl(string host, int port)
    {
        var normalizedHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        return $"http://{normalizedHost}:{port}/";
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_listener.IsListening)
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        _loopTask = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
        }

        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        if (_loopTask is not null)
        {
            await _loopTask.ConfigureAwait(false);
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (context is null)
            {
                continue;
            }

            await HandleAsync(context).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            FlrigTraceLog.Write($"GET {context.Request.RawUrl} ua={context.Request.UserAgent ?? "-"}");
            var body = System.Text.Encoding.UTF8.GetBytes("FLRIG XML-RPC Server");
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html";
            TrySetServerHeader(context);
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            context.Response.OutputStream.Close();
            return;
        }

        byte[] responseBytes;

        try
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var body = await reader.ReadToEndAsync().ConfigureAwait(false);
            FlrigTraceLog.Write($"POST {context.Request.RawUrl} body={body}");
            var request = XmlRpcParser.Parse(body);
            FlrigTraceLog.Write($"METHOD {request.MethodName} params={string.Join(" | ", request.Parameters.Select(p => $"{p.Type}:{p.AsString()}"))}");
            var result = _dispatcher.Dispatch(request);
            FlrigTraceLog.Write($"RESULT {request.MethodName} => {result?.ToString() ?? "<null>"}");
            responseBytes = XmlRpcResponseWriter.WriteValue(result);
        }
        catch (Exception ex)
        {
            FlrigTraceLog.Write($"ERROR {ex.GetType().Name}: {ex.Message}");
            responseBytes = XmlRpcResponseWriter.WriteValue(string.Empty);
        }

        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/xml";
        TrySetServerHeader(context);
        context.Response.ContentLength64 = responseBytes.Length;
        await context.Response.OutputStream.WriteAsync(responseBytes).ConfigureAwait(false);
        context.Response.OutputStream.Close();
    }

    private static void TrySetServerHeader(HttpListenerContext context)
    {
        try
        {
            context.Response.Headers["Server"] = "XMLRPC++ 0.8";
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _listener.Close();
        _cts?.Dispose();
    }
}
