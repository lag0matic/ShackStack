param(
    [string]$InputWav = "",
    [int]$ChunkMs = 20,
    [int]$OffsetHz = 0,
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($InputWav)) {
    $InputWav = "C:\Users\lag0m\ShackStack\freedv-debug\roundtrip_radev1_modem_from_recording17_current.wav"
}

if (-not [System.IO.Path]::IsPathRooted($InputWav)) {
    $InputWav = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $InputWav))
}

if (-not (Test-Path $InputWav)) {
    throw "Input WAV not found: $InputWav"
}

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $env:USERPROFILE "ShackStack\freedv-debug"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutDir)) {
    $OutDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutDir))
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$scratch = Join-Path $repoRoot ".tmp-freedv-rx-chain-harness"
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

$csprojPath = Join-Path $scratch "FreedvRxChainHarness.csproj"
$programPath = Join-Path $scratch "Program.cs"

Set-Content -Path $csprojPath -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$repoRoot\src\ShackStack.Core.Abstractions\ShackStack.Core.Abstractions.csproj" />
    <ProjectReference Include="$repoRoot\src\ShackStack.Infrastructure.Decoders\ShackStack.Infrastructure.Decoders.csproj" />
    <PackageReference Include="NAudio" Version="2.2.1" />
  </ItemGroup>
</Project>
"@ -NoNewline

Set-Content -Path $programPath -Value @'
using System.Text.Json;
using NAudio.Wave;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;
using ShackStack.Infrastructure.Decoders;

var inputWav = args.Length > 0 ? args[0] : throw new ArgumentException("input WAV required");
var outDir = args.Length > 1 ? args[1] : throw new ArgumentException("output dir required");
var chunkMs = args.Length > 2 ? Math.Max(5, int.Parse(args[2])) : 20;
var offsetHz = args.Length > 3 ? int.Parse(args[3]) : 0;

Directory.CreateDirectory(outDir);
var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
var decodedPath = Path.Combine(outDir, $"freedv_rx_chain_{stamp}_decoded.wav");

var audio = new FakeAudioService();
using var host = new Codec2FreedvDigitalVoiceHost(audio);

var telemetry = new List<FreedvDigitalVoiceTelemetry>();
var speechBytes = new List<byte>();
using var telemetrySub = host.TelemetryStream.Subscribe(new Observer<FreedvDigitalVoiceTelemetry>(item => telemetry.Add(item)));
using var speechSub = host.SpeechStream.Subscribe(new Observer<Pcm16AudioClip>(clip =>
{
    if (clip.Channels != 1 || clip.SampleRate != 16000)
    {
        // The current RADEV1 path should emit mono 16 kHz speech. Keep the
        // harness strict so changes here do not silently alter the audio path.
        throw new InvalidOperationException($"Unexpected speech clip format: {clip.SampleRate} Hz, {clip.Channels} ch");
    }

    speechBytes.AddRange(clip.PcmBytes);
}));

await host.ConfigureAsync(new FreedvDigitalVoiceConfiguration(
    "RADEV1",
    Path.GetFileName(inputWav),
    UseCurrentRadioFrequency: true,
    RxFrequencyOffsetHz: offsetHz,
    TransmitCallsign: "W8STR"), CancellationToken.None);
await host.StartAsync(CancellationToken.None);

var (sampleRate, channels, samples) = ReadWavAsFloatInterleaved(inputWav);
var framesPerChunk = Math.Max(1, sampleRate * chunkMs / 1000);
var samplesPerChunk = framesPerChunk * channels;

for (var offset = 0; offset < samples.Length; offset += samplesPerChunk)
{
    var take = Math.Min(samplesPerChunk, samples.Length - offset);
    var chunk = new float[take];
    Array.Copy(samples, offset, chunk, 0, take);
    audio.PushReceive(new AudioBuffer(chunk, sampleRate, channels));

    // Give DecoderAudioPump a realistic chance to drain. This intentionally
    // exercises the bounded queue instead of bulk-feeding the sidecar directly.
    await Task.Delay(Math.Max(1, chunkMs / 4));
}

await Task.Delay(1000);
await host.StopAsync(CancellationToken.None);
await Task.Delay(250);

if (speechBytes.Count > 0)
{
    using var writer = new WaveFileWriter(decodedPath, new WaveFormat(16000, 16, 1));
    writer.Write(speechBytes.ToArray(), 0, speechBytes.Count);
}

var rxFrames = telemetry.Count(t => t.Status.Contains("RX frame", StringComparison.OrdinalIgnoreCase));
var syncFrames = telemetry.Count(t => t.Status.Contains("RX frame", StringComparison.OrdinalIgnoreCase) && t.SyncPercent > 0);
var snrs = telemetry.Where(t => t.Status.Contains("RX frame", StringComparison.OrdinalIgnoreCase)).Select(t => t.SnrDb).ToArray();
var callsigns = telemetry.Select(t => t.RadeCallsign).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
var decodedSamples = BytesToInt16(speechBytes.ToArray());
var maxDiff = 0;
for (var i = 1; i < decodedSamples.Length; i++)
{
    maxDiff = Math.Max(maxDiff, Math.Abs(decodedSamples[i] - decodedSamples[i - 1]));
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    status = speechBytes.Count > 0 ? "passed" : "failed",
    inputWav,
    inputFormat = $"{sampleRate} Hz, {channels} ch",
    chunkMs,
    offsetHz,
    decodedWav = speechBytes.Count > 0 ? decodedPath : "",
    decodedSeconds = Math.Round((speechBytes.Count / 2d) / 16000d, 2),
    rxFrames,
    syncFrames,
    averageSnrDb = snrs.Length > 0 ? Math.Round(snrs.Average(), 2) : 0,
    callsigns,
    decodedPeak = decodedSamples.Length > 0 ? decodedSamples.Max(s => Math.Abs((int)s)) : 0,
    decodedMaxDiff = maxDiff,
    lastTelemetry = telemetry.TakeLast(8).Select(t => t.Status).ToArray(),
}, new JsonSerializerOptions { WriteIndented = true }));

static (int SampleRate, int Channels, float[] Samples) ReadWavAsFloatInterleaved(string path)
{
    using var reader = new WaveFileReader(path);
    var provider = reader.ToSampleProvider();
    var samples = new List<float>();
    var buffer = new float[provider.WaveFormat.SampleRate * provider.WaveFormat.Channels / 2];
    while (true)
    {
        var read = provider.Read(buffer, 0, buffer.Length);
        if (read <= 0)
        {
            break;
        }

        for (var i = 0; i < read; i++)
        {
            samples.Add(float.IsFinite(buffer[i]) ? Math.Clamp(buffer[i], -1f, 1f) : 0f);
        }
    }

    return (provider.WaveFormat.SampleRate, provider.WaveFormat.Channels, samples.ToArray());
}

static short[] BytesToInt16(byte[] bytes)
{
    var values = new short[bytes.Length / sizeof(short)];
    Buffer.BlockCopy(bytes, 0, values, 0, values.Length * sizeof(short));
    return values;
}

sealed class FakeAudioService : IAudioService
{
    private readonly SimpleSubject<AudioBuffer> _receive = new();
    private readonly SimpleSubject<AudioBuffer> _mic = new();
    private readonly SimpleSubject<AudioLevels> _levels = new();

    public IObservable<AudioBuffer> ReceiveStream => _receive;
    public IObservable<AudioBuffer> MicStream => _mic;
    public IObservable<AudioLevels> LevelStream => _levels;

    public void PushReceive(AudioBuffer buffer) => _receive.OnNext(buffer);

    public Task<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AudioDeviceInfo>>([]);
    public Task StartReceiveAsync(AudioRoute route, CancellationToken ct) => Task.CompletedTask;
    public Task StartReceiveCaptureAsync(AudioRoute route, CancellationToken ct) => Task.CompletedTask;
    public Task StopReceiveAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartTransmitAsync(AudioRoute route, CancellationToken ct) => Task.CompletedTask;
    public Task StartMicCaptureAsync(AudioRoute route, CancellationToken ct) => Task.CompletedTask;
    public Task StartTransmitPcmAsync(AudioRoute route, Pcm16AudioClip clip, CancellationToken ct) => Task.CompletedTask;
    public Task PlayTransmitPcmAsync(AudioRoute route, Pcm16AudioClip clip, CancellationToken ct) => Task.CompletedTask;
    public Task StopTransmitAsync(CancellationToken ct) => Task.CompletedTask;
    public Task PlayMonitorPcmAsync(AudioRoute route, Pcm16AudioClip clip, CancellationToken ct) => Task.CompletedTask;
    public Task PlayDecodedMonitorPcmAsync(AudioRoute route, Pcm16AudioClip clip, CancellationToken ct) => Task.CompletedTask;
    public Task SetMonitorVolumeAsync(float volume, CancellationToken ct) => Task.CompletedTask;
    public Task SetDecodedMonitorVolumeAsync(float volume, CancellationToken ct) => Task.CompletedTask;
    public Task SetMicGainAsync(float gain, CancellationToken ct) => Task.CompletedTask;
    public Task SetVoiceCompressionAsync(float amount, CancellationToken ct) => Task.CompletedTask;
    public Task SetMicMonitorAsync(bool enabled, float level, CancellationToken ct) => Task.CompletedTask;
}
'@ -NoNewline

dotnet run --project $csprojPath -c Release -- $InputWav $OutDir $ChunkMs $OffsetHz
