param(
    [int]$Seconds = 10,
    [string]$DeviceName = "",
    [string]$DeviceId = "",
    [string]$WorkerDir = "",
    [string]$OutDir = "",
    [string]$Callsign = "W8STR"
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($WorkerDir)) {
    $WorkerDir = Join-Path $repoRoot "src\ShackStack.Desktop\DecoderWorkers\freedv_codec2_sidecar"
}
elseif (-not [System.IO.Path]::IsPathRooted($WorkerDir)) {
    $WorkerDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $WorkerDir))
}

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $env:USERPROFILE "ShackStack\freedv-debug"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutDir)) {
    $OutDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutDir))
}

$workerExe = Join-Path $WorkerDir "ShackStack.DecoderHost.GplCodec2Freedv.exe"
if (-not (Test-Path $workerExe)) {
    throw "FreeDV sidecar not found: $workerExe. Run installer\Build-DecoderWorkers.ps1 first."
}

$scratch = Join-Path $repoRoot ".tmp-freedv-mic-dryrun"
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$csprojPath = Join-Path $scratch "FreedvMicDryRun.csproj"
$programPath = Join-Path $scratch "Program.cs"

Set-Content -Path $csprojPath -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NAudio" Version="2.2.1" />
  </ItemGroup>
</Project>
'@ -NoNewline

Set-Content -Path $programPath -Value @'
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;

var workerExe = args.Length > 0 ? args[0] : throw new ArgumentException("worker exe required");
var outDir = args.Length > 1 ? args[1] : throw new ArgumentException("output directory required");
var seconds = args.Length > 2 ? Math.Max(1, int.Parse(args[2])) : 10;
var callsign = args.Length > 3 ? args[3] : "W8STR";
var deviceName = args.Length > 4 ? args[4] : string.Empty;
var deviceId = args.Length > 5 ? args[5] : string.Empty;

Directory.CreateDirectory(outDir);
var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
var micWavPath = Path.Combine(outDir, $"freedv_mic_dryrun_{stamp}_capture.wav");
var modemWavPath = Path.Combine(outDir, $"freedv_mic_dryrun_{stamp}_modem.wav");
var decodedWavPath = Path.Combine(outDir, $"freedv_mic_dryrun_{stamp}_decoded.wav");

using var enumerator = new MMDeviceEnumerator();
var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
MMDevice device;
if (!string.IsNullOrWhiteSpace(deviceId))
{
    device = devices.FirstOrDefault(d => string.Equals(d.ID, deviceId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"No active capture device matches ID {deviceId}.");
}
else if (!string.IsNullOrWhiteSpace(deviceName))
{
    device = devices.FirstOrDefault(d => d.FriendlyName.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"No active capture device contains name {deviceName}.");
}
else
{
    device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
}

Console.WriteLine($"Capturing {seconds}s from: {device.FriendlyName}");
var capture = new WasapiCapture(device);
var captured = new List<byte>();
var format = capture.WaveFormat;
using var writer = new WaveFileWriter(micWavPath, format);
var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
capture.DataAvailable += (_, e) =>
{
    writer.Write(e.Buffer, 0, e.BytesRecorded);
    for (var i = 0; i < e.BytesRecorded; i++)
    {
        captured.Add(e.Buffer[i]);
    }
};
capture.RecordingStopped += (_, _) => stopped.TrySetResult();
capture.StartRecording();
await Task.Delay(TimeSpan.FromSeconds(seconds));
capture.StopRecording();
await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
writer.Flush();
capture.Dispose();

var floats = ConvertCapturedBytesToFloatInterleaved(captured.ToArray(), format);
if (floats.Length == 0)
{
    throw new InvalidOperationException("Mic capture produced no usable float samples.");
}

var txLines = new List<object?>
{
    new { type = "configure", modeLabel = "RADEV1", rxFrequencyOffsetHz = 0, transmitCallsign = callsign },
    new { type = "startTx" },
};

var framesPerChunk = Math.Max(1, format.SampleRate / 10);
var samplesPerChunk = framesPerChunk * Math.Max(format.Channels, 1);
for (var offset = 0; offset < floats.Length; offset += samplesPerChunk)
{
    var take = Math.Min(samplesPerChunk, floats.Length - offset);
    var bytes = Float32Bytes(floats, offset, take);
    txLines.Add(new
    {
        type = "speech",
        sampleRate = format.SampleRate,
        channels = format.Channels,
        samples = Convert.ToBase64String(bytes),
    });
}

txLines.Add(new { type = "stopTx" });
txLines.Add(new { type = "shutdown" });

var tx = await RunWorkerAsync(workerExe, txLines, TimeSpan.FromSeconds(Math.Max(60, seconds * 8)));
var modemBytes = new List<byte>();
var txTail = new Queue<string>();
foreach (var message in ReadJsonLines(tx.Stdout))
{
    if (message.RootElement.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "modem")
    {
        modemBytes.AddRange(Convert.FromBase64String(message.RootElement.GetProperty("pcm16").GetString() ?? string.Empty));
    }
    else if (message.RootElement.TryGetProperty("status", out var statusEl))
    {
        PushTail(txTail, statusEl.GetString() ?? string.Empty);
    }
}

if (tx.ExitCode != 0 || modemBytes.Count == 0)
{
    throw new InvalidOperationException($"TX dry-run failed. Exit {tx.ExitCode}. Tail: {string.Join(" | ", txTail)}. stderr: {tx.Stderr}");
}

WritePcm16Wav(modemWavPath, modemBytes.ToArray(), 8000);

var modemSamples = BytesToInt16(modemBytes.ToArray());
var rxLines = new List<object?>
{
    new { type = "configure", modeLabel = "RADEV1", rxFrequencyOffsetHz = 0 },
    new { type = "start" },
};

for (var offset = 0; offset < modemSamples.Length; offset += 800)
{
    var take = Math.Min(800, modemSamples.Length - offset);
    var chunk = new float[take];
    for (var i = 0; i < take; i++)
    {
        chunk[i] = Math.Clamp(modemSamples[offset + i] / 32768f, -1f, 1f);
    }

    rxLines.Add(new
    {
        type = "audio",
        sampleRate = 8000,
        channels = 1,
        samples = Convert.ToBase64String(Float32Bytes(chunk, 0, chunk.Length)),
    });
}

rxLines.Add(new { type = "shutdown" });
var rx = await RunWorkerAsync(workerExe, rxLines, TimeSpan.FromSeconds(Math.Max(60, seconds * 8)));
var decodedBytes = new List<byte>();
var rxTail = new Queue<string>();
var syncFrames = 0;
var callsigns = new List<string>();
foreach (var message in ReadJsonLines(rx.Stdout))
{
    if (message.RootElement.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "speech")
    {
        decodedBytes.AddRange(Convert.FromBase64String(message.RootElement.GetProperty("pcm16").GetString() ?? string.Empty));
    }
    else if (message.RootElement.TryGetProperty("status", out var statusEl))
    {
        var status = statusEl.GetString() ?? string.Empty;
        PushTail(rxTail, status);
        if (status.Contains("sync 1", StringComparison.OrdinalIgnoreCase))
        {
            syncFrames++;
        }

        if (message.RootElement.TryGetProperty("radeCallsign", out var callEl))
        {
            var call = callEl.GetString();
            if (!string.IsNullOrWhiteSpace(call))
            {
                callsigns.Add(call);
            }
        }
    }
}

if (decodedBytes.Count > 0)
{
    WritePcm16Wav(decodedWavPath, decodedBytes.ToArray(), 16000);
}

var decodedSamples = BytesToInt16(decodedBytes.ToArray());
var peak = decodedSamples.Length == 0 ? 0 : decodedSamples.Max(s => Math.Abs((int)s));
var maxDiff = 0;
for (var i = 1; i < decodedSamples.Length; i++)
{
    maxDiff = Math.Max(maxDiff, Math.Abs(decodedSamples[i] - decodedSamples[i - 1]));
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    status = rx.ExitCode == 0 && decodedBytes.Count > 0 ? "passed" : "failed",
    device = device.FriendlyName,
    captureFormat = $"{format.SampleRate} Hz, {format.Channels} ch, {format.Encoding}, {format.BitsPerSample} bit",
    micCaptureWav = micWavPath,
    modemWav = modemWavPath,
    decodedWav = decodedWavPath,
    modemSeconds = Math.Round((modemBytes.Count / 2d) / 8000d, 2),
    decodedSeconds = Math.Round((decodedBytes.Count / 2d) / 16000d, 2),
    syncFrames,
    callsigns = callsigns.TakeLast(5).ToArray(),
    decodedPeak = peak,
    decodedMaxDiff = maxDiff,
    txTail = txTail.ToArray(),
    rxTail = rxTail.ToArray(),
}, new JsonSerializerOptions { WriteIndented = true }));

static float[] ConvertCapturedBytesToFloatInterleaved(byte[] bytes, WaveFormat format)
{
    if (bytes.Length == 0)
    {
        return [];
    }

    if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, floats.Length * sizeof(float));
        for (var i = 0; i < floats.Length; i++)
        {
            if (!float.IsFinite(floats[i]))
            {
                floats[i] = 0f;
            }
        }

        return floats;
    }

    if (format.BitsPerSample == 16)
    {
        var shorts = BytesToInt16(bytes);
        var floats = new float[shorts.Length];
        for (var i = 0; i < shorts.Length; i++)
        {
            floats[i] = shorts[i] / 32768f;
        }

        return floats;
    }

    throw new NotSupportedException($"Unsupported capture format: {format}");
}

static short[] BytesToInt16(byte[] bytes)
{
    var values = new short[bytes.Length / sizeof(short)];
    Buffer.BlockCopy(bytes, 0, values, 0, values.Length * sizeof(short));
    return values;
}

static byte[] Float32Bytes(float[] values, int offset, int count)
{
    var bytes = new byte[count * sizeof(float)];
    Buffer.BlockCopy(values, offset * sizeof(float), bytes, 0, bytes.Length);
    return bytes;
}

static void WritePcm16Wav(string path, byte[] pcm, int sampleRate)
{
    using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1));
    writer.Write(pcm, 0, pcm.Length);
}

static async Task<(int ExitCode, string Stdout, string Stderr)> RunWorkerAsync(string exe, IEnumerable<object?> lines, TimeSpan timeout)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        },
    };

    process.Start();
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    foreach (var line in lines)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(line));
    }

    process.StandardInput.Close();
    using var cts = new CancellationTokenSource(timeout);
    await process.WaitForExitAsync(cts.Token);
    return (process.ExitCode, await stdoutTask, await stderrTask);
}

static IEnumerable<JsonDocument> ReadJsonLines(string text)
{
    foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
    {
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch
        {
        }

        if (doc is not null)
        {
            yield return doc;
        }
    }
}

static void PushTail(Queue<string> queue, string value)
{
    queue.Enqueue(value);
    while (queue.Count > 8)
    {
        queue.Dequeue();
    }
}
'@ -NoNewline

dotnet run --project $csprojPath -c Release -- $workerExe $OutDir $Seconds $Callsign $DeviceName $DeviceId
