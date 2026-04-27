param(
    [string]$Message = "W8STRTEST123",
    [string]$Js8CallSource = "",
    [string]$MsysRoot = "C:\msys64",
    [switch]$KeepArtifacts
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Js8CallSource)) {
    $Js8CallSource = Join-Path $repoRoot ".tmp-js8call-repo"
}

$sourceRoot = Resolve-Path $Js8CallSource
$libRoot = Join-Path $sourceRoot "lib"
$ucrtBin = Join-Path $MsysRoot "ucrt64\bin"
$gfortran = Join-Path $ucrtBin "gfortran.exe"
$gxx = Join-Path $ucrtBin "g++.exe"
$jt9 = Join-Path $repoRoot "src\ShackStack.Desktop\js8call-tools\runtime\bin\jt9.exe"
$sidecarProject = Join-Path $repoRoot "src\ShackStack.DecoderHost.GplWsjtx\ShackStack.DecoderHost.GplWsjtx.csproj"

foreach ($path in @($gfortran, $gxx, $jt9, $sidecarProject)) {
    if (-not (Test-Path $path)) {
        throw "Required JS8 smoke dependency missing: $path"
    }
}

$Message = $Message.Trim().ToUpperInvariant()
if ($Message.Length -ne 12) {
    throw "JS8 synthetic smoke message must be exactly 12 characters for the compact JS8Call encoder. Example: W8STRTEST123"
}

$buildRoot = Join-Path $repoRoot ".tmp-js8-synth-build"
$runtimeRoot = Join-Path $repoRoot ".tmp-js8-synth-runtime"
$wavPath = Join-Path $repoRoot ".tmp-js8-synth.wav"
Remove-Item -Recurse -Force $buildRoot, $runtimeRoot -ErrorAction SilentlyContinue
Remove-Item -Force $wavPath -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $buildRoot, $runtimeRoot | Out-Null

$env:PATH = "$ucrtBin;$env:PATH"
$toneProgram = @"
program emit_js8_tones
  implicit none
  integer :: i
  integer :: msgbits(77)
  integer :: itone(79)
  character(len=12) :: msg
  character(len=22) :: msgsent
  msg = '$Message'
  call genjs8(msg, 1, 0, msgsent, msgbits, itone)
  do i = 1, 79
    write(*,'(I0)', advance='no') itone(i)
    if (i .lt. 79) write(*,'(A)', advance='no') ' '
  end do
  write(*,*)
end program emit_js8_tones
"@

Push-Location $buildRoot
try {
    $toneSource = Join-Path $buildRoot "emit_js8_tones.f90"
    Set-Content -Path $toneSource -Encoding ASCII -Value $toneProgram

    & $gxx -O2 -c (Join-Path $libRoot "crc12.cpp")
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to compile JS8 CRC support."
    }

    & $gfortran -O2 -ffree-line-length-none `
        -I $libRoot `
        -I (Join-Path $libRoot "js8") `
        -c `
        (Join-Path $libRoot "crc.f90") `
        (Join-Path $libRoot "ft8\encode174.f90") `
        (Join-Path $libRoot "js8\genjs8.f90") `
        $toneSource
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to compile JS8 tone generator."
    }

    & $gfortran -o emit_js8_tones.exe .\crc.o .\crc12.o .\encode174.o .\genjs8.o .\emit_js8_tones.o -lstdc++
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to link JS8 tone generator."
    }

    $toneText = (& .\emit_js8_tones.exe).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "JS8 tone generator failed."
    }
}
finally {
    Pop-Location
}

$tones = $toneText -split "\s+" | ForEach-Object { [int]$_ }
if ($tones.Count -ne 79) {
    throw "Expected 79 JS8 tones, got $($tones.Count): $toneText"
}

$sampleRate = 12000
$nsps = 1920
$startDelaySamples = 6000
$totalSamples = $sampleRate * 15
$toneSpacingHz = $sampleRate / $nsps
$audioHz = 1500.0
$phase = 0.0
$twoPi = [Math]::PI * 2.0
$samples = New-Object single[] $totalSamples

for ($i = 0; $i -lt $totalSamples; $i++) {
    $symbol = [Math]::Floor(($i - $startDelaySamples) / $nsps)
    if ($symbol -ge 0 -and $symbol -lt $tones.Count) {
        $frequency = $audioHz + ($tones[$symbol] * $toneSpacingHz)
        $phase += $twoPi * $frequency / $sampleRate
        if ($phase -gt $twoPi) {
            $phase -= $twoPi
        }

        $samples[$i] = [single]([Math]::Sin($phase) * 0.45)
    }
}

$stream = [System.IO.File]::Create($wavPath)
try {
    $writer = [System.IO.BinaryWriter]::new($stream, [System.Text.Encoding]::ASCII, $false)
    $dataLength = $samples.Length * 2
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("RIFF"))
    $writer.Write([int](36 + $dataLength))
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("WAVE"))
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("fmt "))
    $writer.Write([int]16)
    $writer.Write([int16]1)
    $writer.Write([int16]1)
    $writer.Write([int]$sampleRate)
    $writer.Write([int]($sampleRate * 2))
    $writer.Write([int16]2)
    $writer.Write([int16]16)
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("data"))
    $writer.Write([int]$dataLength)

    foreach ($sample in $samples) {
        $writer.Write([int16][Math]::Round($sample * 32767.0))
    }
}
finally {
    $stream.Dispose()
}

$directArgs = @(
    "-8",
    "-b", "A",
    "-d", "3",
    "-L", "200",
    "-H", "4000",
    "-f", "1500",
    "-a", $runtimeRoot,
    "-t", $runtimeRoot,
    $wavPath
)
$directOutput = & $jt9 @directArgs
if ($LASTEXITCODE -ne 0) {
    throw "Direct JS8 jt9 synthetic decode failed."
}

if (-not ($directOutput | Where-Object { $_ -match [Regex]::Escape($Message) })) {
    throw "Direct JS8 jt9 synthetic decode did not contain $Message.`n$($directOutput -join "`n")"
}

$audioBytes = New-Object byte[] ($samples.Length * 4)
for ($i = 0; $i -lt $samples.Length; $i++) {
    [BitConverter]::GetBytes($samples[$i]).CopyTo($audioBytes, $i * 4)
}

$processStart = [System.Diagnostics.ProcessStartInfo]::new(
    "dotnet",
    "run --project `"$sidecarProject`" --configuration Release --no-launch-profile")
$processStart.WorkingDirectory = Split-Path -Parent $sidecarProject
$processStart.UseShellExecute = $false
$processStart.RedirectStandardInput = $true
$processStart.RedirectStandardOutput = $true
$processStart.RedirectStandardError = $true
$processStart.CreateNoWindow = $true

$process = [System.Diagnostics.Process]::Start($processStart)
try {
    $messages = @(
        @{
            type = "configure"
            modeLabel = "JS8 Normal"
            frequencyLabel = "20m JS8 14.078 MHz USB-D"
            stationCallsign = "W8STR"
            stationGridSquare = "EM79"
        },
        @{
            type = "start"
            modeLabel = "JS8 Normal"
            frequencyLabel = "20m JS8 14.078 MHz USB-D"
            stationCallsign = "W8STR"
            stationGridSquare = "EM79"
        },
        @{
            type = "audio"
            sampleRate = 12000
            channels = 1
            utcNowUnixMs = 0
            samples = [Convert]::ToBase64String($audioBytes)
        },
        @{ type = "shutdown" }
    )

    foreach ($jsonMessage in $messages) {
        $process.StandardInput.WriteLine(($jsonMessage | ConvertTo-Json -Compress -Depth 5))
        $process.StandardInput.Flush()
    }
    $process.StandardInput.Close()

    if (-not $process.WaitForExit(60000)) {
        $process.Kill($true)
        throw "JS8 synthetic sidecar smoke timed out."
    }

    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $lines = $stdout -split "`r?`n" | Where-Object { $_ }
    $lines

    if ($stderr.Trim().Length -gt 0) {
        Write-Warning $stderr.Trim()
    }

    if ($process.ExitCode -ne 0) {
        throw "JS8 synthetic sidecar smoke failed with exit code $($process.ExitCode)."
    }

    $decodeLines = $lines | Where-Object { $_ -match '"type":"decode"' }
    if (-not $decodeLines) {
        throw "JS8 synthetic sidecar smoke did not emit a decode.`n$stdout"
    }

    if ($decodeLines | Where-Object { $_ -match [Regex]::Escape("$Message         0") }) {
        throw "JS8 synthetic sidecar smoke leaked the JS8 frame-type digit into user text."
    }
}
finally {
    if (-not $process.HasExited) {
        $process.Kill($true)
    }
    $process.Dispose()

    if (-not $KeepArtifacts) {
        Remove-Item -Recurse -Force $buildRoot, $runtimeRoot -ErrorAction SilentlyContinue
        Remove-Item -Force $wavPath -ErrorAction SilentlyContinue
    }
}

Write-Host "JS8 synthetic smoke decoded $Message through bundled jt9.exe and the ShackStack sidecar."
