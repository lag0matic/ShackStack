param(
    [string]$FreeText = "CQ TEST",
    [string]$DirectedText = "W8STR: KE9CRR SNR?",
    [string]$LongText = "THIS IS A LONG JS8 TEST MESSAGE THAT SHOULD SPLIT ACROSS MORE THAN ONE FRAME",
    [string]$PlaceholderText = "CQ CQ CQ DE <MYCALL>"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$tmp = Join-Path $repoRoot ".tmp-js8tx-smoke"
$tones = Join-Path $repoRoot "src\ShackStack.Desktop\js8call-tools\runtime\bin\js8tones.exe"
$jt9 = Join-Path $repoRoot "src\ShackStack.Desktop\js8call-tools\runtime\bin\jt9.exe"

foreach ($path in @($tones, $jt9)) {
    if (-not (Test-Path $path)) {
        throw "Required JS8 TX smoke dependency missing: $path"
    }
}

Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $tmp | Out-Null

Set-Content -Encoding ASCII (Join-Path $tmp "Test.csproj") @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\src\ShackStack.Infrastructure.Decoders\ShackStack.Infrastructure.Decoders.csproj" />
  </ItemGroup>
</Project>
'@

Set-Content -Encoding ASCII (Join-Path $tmp "Program.cs") @'
using System.Globalization;
using System.Reflection;

static async Task PrepareAsync(object port, Type type, string mode, double cycleLength, string text, string wavPath)
{
    var task = (Task)type.GetMethod("PrepareAsync", BindingFlags.Public | BindingFlags.Instance)!.Invoke(port, [mode, text, 1500, cycleLength, CancellationToken.None])!;
    await task.ConfigureAwait(false);
    var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
    var success = (bool)result.GetType().GetProperty("Success")!.GetValue(result)!;
    var status = (string)result.GetType().GetProperty("Status")!.GetValue(result)!;
    if (!success)
    {
        throw new InvalidOperationException(status);
    }

    var clip = result.GetType().GetProperty("PreparedClip")!.GetValue(result)!;
    var pcm = (byte[])clip.GetType().GetProperty("PcmBytes")!.GetValue(clip)!;
    var sampleRate = (int)clip.GetType().GetProperty("SampleRate")!.GetValue(clip)!;
    var channels = (int)clip.GetType().GetProperty("Channels")!.GetValue(clip)!;
    using var fs = File.Create(wavPath);
    using var bw = new BinaryWriter(fs);
    bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
    bw.Write(36 + pcm.Length);
    bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
    bw.Write(16);
    bw.Write((short)1);
    bw.Write((short)channels);
    bw.Write(sampleRate);
    bw.Write(sampleRate * channels * 2);
    bw.Write((short)(channels * 2));
    bw.Write((short)16);
    bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
    bw.Write(pcm.Length);
    bw.Write(pcm);

    Console.WriteLine($"{Path.GetFileName(wavPath)}|{status}");
}

var mode = args[0];
var cycleLength = double.Parse(args[1], CultureInfo.InvariantCulture);
var text = args[2];
var wavPath = Path.GetFullPath(args[3]);
var asm = typeof(ShackStack.Infrastructure.Decoders.PythonWsjtxModeHost).Assembly;
var type = asm.GetType("ShackStack.Infrastructure.Decoders.WsjtxExternalWaveformPort", true)!;
var port = type.GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [])!;
await PrepareAsync(port, type, mode, cycleLength, text, wavPath);
'@

$env:SHACKSTACK_JS8_TONES_PATH = $tones

$cases = @(
    @{ Mode = "JS8 Normal"; Code = "A"; Cycle = 15.0 },
    @{ Mode = "JS8 Fast"; Code = "B"; Cycle = 10.0 },
    @{ Mode = "JS8 Turbo"; Code = "C"; Cycle = 6.0 },
    @{ Mode = "JS8 Slow"; Code = "E"; Cycle = 28.0 }
)

foreach ($case in $cases) {
    $safeMode = $case.Mode.Replace(" ", "-").ToLowerInvariant()
    $wav = Join-Path $tmp "$safeMode-free.wav"
    $status = dotnet run --project (Join-Path $tmp "Test.csproj") -c Release -- $case.Mode ([string]$case.Cycle) $FreeText $wav
    Write-Host $status

    $runtimeRoot = Join-Path $tmp "$safeMode-runtime"
    New-Item -ItemType Directory -Force $runtimeRoot | Out-Null
    $decode = & $jt9 -8 -b $case.Code -d 3 -L 200 -H 4000 -f 1500 -a $runtimeRoot -t $runtimeRoot $wav
    if (-not ($decode | Where-Object { $_ -match "\bDecodeFinished\b" })) {
        throw "JS8 $($case.Mode) TX smoke did not complete a decode.`n$($decode -join "`n")"
    }

    if (-not ($decode | Where-Object { $_ -like "*iI3ifh++++++*" })) {
        throw "JS8 $($case.Mode) TX smoke did not decode the expected CQ TEST packed frame.`n$($decode -join "`n")"
    }
}

$directedWav = Join-Path $tmp "directed-normal.wav"
dotnet run --project (Join-Path $tmp "Test.csproj") -c Release -- "JS8 Normal" "15.0" $DirectedText $directedWav | Write-Host
$directedRuntime = Join-Path $tmp "directed-runtime"
New-Item -ItemType Directory -Force $directedRuntime | Out-Null
$directedDecode = & $jt9 -8 -b A -d 3 -L 200 -H 4000 -f 1500 -a $directedRuntime -t $directedRuntime $directedWav
if (-not ($directedDecode | Where-Object { $_ -like "*Vouu6HFd0Q00*" })) {
    throw "JS8 directed TX smoke did not decode the expected packed frame.`n$($directedDecode -join "`n")"
}

$longWav = Join-Path $tmp "long-normal.wav"
$longStatus = dotnet run --project (Join-Path $tmp "Test.csproj") -c Release -- "JS8 Normal" "15.0" $LongText $longWav
Write-Host $longStatus
if ($longStatus -like "*1 frame*") {
    throw "JS8 long-message TX smoke did not split into multiple frames: $longStatus"
}

$placeholderWav = Join-Path $tmp "placeholder-normal.wav"
$placeholderStatus = dotnet run --project (Join-Path $tmp "Test.csproj") -c Release -- "JS8 Normal" "15.0" $PlaceholderText $placeholderWav
Write-Host $placeholderStatus
if ($placeholderStatus -like "*no encodable*") {
    throw "JS8 placeholder/punctuation TX smoke unexpectedly failed: $placeholderStatus"
}

Write-Host "JS8 TX smoke passed for Normal/Fast/Turbo/Slow timing, directed frames, and multi-frame splitting."
