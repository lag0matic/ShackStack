using System.Text.Json;
using ShackStack.DecoderHost.Sstv;
using ShackStack.DecoderHost.Sstv.Protocol;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var runtime = new SstvSidecarRuntime();
runtime.EmitStartup();

while (Console.ReadLine() is { } line)
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    DecoderCommand? command = null;
    try
    {
        command = JsonSerializer.Deserialize<DecoderCommand>(line, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    catch (Exception ex)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            type = "telemetry",
            isRunning = false,
            status = $"Protocol error: {ex.Message}",
            activeWorker = "ShackStack SSTV native sidecar",
            signalLevelPercent = 0,
            detectedMode = "Unknown",
        }));
        continue;
    }

    if (command is null)
    {
        continue;
    }

    runtime.Handle(command);
    if (string.Equals(command.Type, "shutdown", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }
}
