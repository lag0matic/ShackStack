param(
    [string]$Mode = "700D",
    [double]$Seconds = 8.0,
    [string]$SpeechWav = "",
    [string]$WorkerDir = "",
    [string]$OutDir = ".tmp-freedv-harness"
)

$ErrorActionPreference = "Stop"

function Write-Pcm16Wav {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][byte[]]$PcmBytes,
        [int]$SampleRate = 8000
    )

    $channels = [int16]1
    $bitsPerSample = [int16]16
    $blockAlign = [int16]($channels * $bitsPerSample / 8)
    $byteRate = [int]($SampleRate * $blockAlign)
    $dataLength = [int]$PcmBytes.Length

    $stream = [System.IO.File]::Create($Path)
    try {
        $writer = New-Object System.IO.BinaryWriter($stream)
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes("RIFF"))
        $writer.Write([int](36 + $dataLength))
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes("WAVE"))
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes("fmt "))
        $writer.Write([int]16)
        $writer.Write([int16]1)
        $writer.Write($channels)
        $writer.Write([int]$SampleRate)
        $writer.Write($byteRate)
        $writer.Write($blockAlign)
        $writer.Write($bitsPerSample)
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes("data"))
        $writer.Write($dataLength)
        $writer.Write($PcmBytes)
    }
    finally {
        $stream.Dispose()
    }
}

function New-SyntheticSpeechPcm16 {
    param(
        [double]$DurationSeconds,
        [int]$SampleRate = 8000
    )

    $sampleCount = [int][Math]::Round($DurationSeconds * $SampleRate)
    $bytes = New-Object byte[] ($sampleCount * 2)
    for ($i = 0; $i -lt $sampleCount; $i++) {
        $t = $i / [double]$SampleRate
        $syllable = 0.55 + 0.45 * [Math]::Sin(2.0 * [Math]::PI * 3.2 * $t)
        $phraseGate = if (([int][Math]::Floor($t * 2.0) % 5) -eq 4) { 0.15 } else { 1.0 }
        $f0 = 125.0 + 25.0 * [Math]::Sin(2.0 * [Math]::PI * 0.7 * $t)
        $sample =
            (0.55 * [Math]::Sin(2.0 * [Math]::PI * $f0 * $t)) +
            (0.24 * [Math]::Sin(2.0 * [Math]::PI * ($f0 * 2.05) * $t)) +
            (0.14 * [Math]::Sin(2.0 * [Math]::PI * 730.0 * $t)) +
            (0.07 * [Math]::Sin(2.0 * [Math]::PI * 1180.0 * $t))
        $attack = [Math]::Min(1.0, $t / 0.08)
        $release = [Math]::Min(1.0, ($DurationSeconds - $t) / 0.12)
        $envelope = [Math]::Min($attack, $release) * $syllable * $phraseGate
        $scaled = [Math]::Round($sample * $envelope * 12500.0)
        if ($scaled -gt [int16]::MaxValue) {
            $scaled = [int16]::MaxValue
        } elseif ($scaled -lt [int16]::MinValue) {
            $scaled = [int16]::MinValue
        }
        $value = [int16]$scaled
        $pair = [BitConverter]::GetBytes($value)
        $bytes[$i * 2] = $pair[0]
        $bytes[$i * 2 + 1] = $pair[1]
    }

    Write-Output -NoEnumerate $bytes
}

function Read-Pcm16WavAsMono8k {
    param(
        [Parameter(Mandatory=$true)][string]$Path
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 44) {
        throw "WAV file is too short: $Path"
    }

    $riff = [System.Text.Encoding]::ASCII.GetString($bytes, 0, 4)
    $wave = [System.Text.Encoding]::ASCII.GetString($bytes, 8, 4)
    if ($riff -ne "RIFF" -or $wave -ne "WAVE") {
        throw "Unsupported WAV container: $Path"
    }

    $offset = 12
    $formatTag = 0
    $channels = 0
    $sampleRate = 0
    $bitsPerSample = 0
    $dataOffset = -1
    $dataLength = 0

    while ($offset + 8 -le $bytes.Length) {
        $chunkId = [System.Text.Encoding]::ASCII.GetString($bytes, $offset, 4)
        $chunkSize = [BitConverter]::ToInt32($bytes, $offset + 4)
        $chunkData = $offset + 8
        if ($chunkData + $chunkSize -gt $bytes.Length) {
            break
        }

        if ($chunkId -eq "fmt ") {
            $formatTag = [BitConverter]::ToInt16($bytes, $chunkData)
            $channels = [BitConverter]::ToInt16($bytes, $chunkData + 2)
            $sampleRate = [BitConverter]::ToInt32($bytes, $chunkData + 4)
            $bitsPerSample = [BitConverter]::ToInt16($bytes, $chunkData + 14)
        } elseif ($chunkId -eq "data") {
            $dataOffset = $chunkData
            $dataLength = $chunkSize
        }

        $offset = $chunkData + $chunkSize
        if (($offset % 2) -ne 0) {
            $offset++
        }
    }

    if ($formatTag -ne 1 -or $bitsPerSample -ne 16 -or $channels -lt 1 -or $sampleRate -lt 1 -or $dataOffset -lt 0) {
        throw "Only 16-bit PCM WAV input is currently supported. Got format=$formatTag channels=$channels rate=$sampleRate bits=$bitsPerSample"
    }

    $sourceFrames = [int]($dataLength / ($channels * 2))
    $monoSource = New-Object double[] $sourceFrames
    for ($frame = 0; $frame -lt $sourceFrames; $frame++) {
        $sum = 0.0
        for ($channel = 0; $channel -lt $channels; $channel++) {
            $sampleOffset = $dataOffset + (($frame * $channels + $channel) * 2)
            $sum += [BitConverter]::ToInt16($bytes, $sampleOffset)
        }
        $monoSource[$frame] = $sum / [double]$channels
    }

    $targetRate = 8000.0
    $targetFrames = [int][Math]::Max(1, [Math]::Round($sourceFrames * $targetRate / $sampleRate))
    $output = New-Object byte[] ($targetFrames * 2)
    $sourcePerOutput = $sampleRate / $targetRate

    for ($i = 0; $i -lt $targetFrames; $i++) {
        $sourceStart = $i * $sourcePerOutput
        $sourceEnd = [Math]::Min($sourceFrames, ($i + 1) * $sourcePerOutput)

        if ($sourcePerOutput -le 1.0) {
            $index = [int][Math]::Floor($sourceStart)
            $fraction = $sourceStart - $index
            $a = $monoSource[[Math]::Max(0, [Math]::Min($sourceFrames - 1, $index))]
            $b = $monoSource[[Math]::Max(0, [Math]::Min($sourceFrames - 1, $index + 1))]
            $value = $a + (($b - $a) * $fraction)
        } else {
            $first = [int][Math]::Floor($sourceStart)
            $last = [int][Math]::Ceiling($sourceEnd)
            $sum = 0.0
            $weightSum = 0.0
            for ($sourceIndex = $first; $sourceIndex -lt $last; $sourceIndex++) {
                $clamped = [Math]::Max(0, [Math]::Min($sourceFrames - 1, $sourceIndex))
                $sampleStart = [Math]::Max($sourceStart, [double]$sourceIndex)
                $sampleEnd = [Math]::Min($sourceEnd, [double]($sourceIndex + 1))
                $weight = $sampleEnd - $sampleStart
                if ($weight -le 0.0) {
                    continue
                }
                $sum += $monoSource[$clamped] * $weight
                $weightSum += $weight
            }
            $value = if ($weightSum -gt 0.0) { $sum / $weightSum } else { 0.0 }
        }

        $mono = [int16][Math]::Max([int16]::MinValue, [Math]::Min([int16]::MaxValue, [Math]::Round($value)))
        $pair = [BitConverter]::GetBytes($mono)
        $output[$i * 2] = $pair[0]
        $output[$i * 2 + 1] = $pair[1]
    }

    Write-Output -NoEnumerate $output
}

function Invoke-FreeDvTool {
    param(
        [Parameter(Mandatory=$true)][string]$ExePath,
        [Parameter(Mandatory=$true)][string[]]$Arguments,
        [Parameter(Mandatory=$true)][string]$LogPath
    )

    $stdoutPath = "$LogPath.stdout"
    $process = Start-Process `
        -FilePath $ExePath `
        -ArgumentList $Arguments `
        -NoNewWindow `
        -PassThru `
        -Wait `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $LogPath
    if ($process.ExitCode -ne 0) {
        throw "$([System.IO.Path]::GetFileName($ExePath)) exited with $($process.ExitCode). See $LogPath"
    }
}

if ([string]::IsNullOrWhiteSpace($WorkerDir)) {
    $WorkerDir = Join-Path $PSScriptRoot "..\src\ShackStack.Desktop\DecoderWorkers\freedv_codec2_sidecar"
}

$WorkerDir = if ([System.IO.Path]::IsPathRooted($WorkerDir)) {
    [System.IO.Path]::GetFullPath($WorkerDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $WorkerDir))
}
$OutDir = if ([System.IO.Path]::IsPathRooted($OutDir)) {
    [System.IO.Path]::GetFullPath($OutDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutDir))
}
$txExe = Join-Path $WorkerDir "freedv_tx.exe"
$rxExe = Join-Path $WorkerDir "freedv_rx.exe"

if (!(Test-Path $txExe) -or !(Test-Path $rxExe)) {
    throw "FreeDV demo executables not found in $WorkerDir"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$modeSafe = $Mode.ToUpperInvariant()
$inputStem = if ([string]::IsNullOrWhiteSpace($SpeechWav)) { "synthetic_speech" } else { "recorded_speech" }
$inputRaw = Join-Path $OutDir "$inputStem`_$modeSafe.raw"
$modemRaw = Join-Path $OutDir "freedv_modem_$modeSafe.raw"
$decodedRaw = Join-Path $OutDir "decoded_speech_$modeSafe.raw"
$inputWav = Join-Path $OutDir "$inputStem`_$modeSafe.wav"
$modemWav = Join-Path $OutDir "freedv_modem_$modeSafe.wav"
$decodedWav = Join-Path $OutDir "decoded_speech_$modeSafe.wav"
$txLog = Join-Path $OutDir "freedv_tx_$modeSafe.log"
$rxLog = Join-Path $OutDir "freedv_rx_$modeSafe.log"

$speechBytes = if ([string]::IsNullOrWhiteSpace($SpeechWav)) {
    New-SyntheticSpeechPcm16 -DurationSeconds $Seconds -SampleRate 8000
} else {
    Read-Pcm16WavAsMono8k -Path $SpeechWav
}
[System.IO.File]::WriteAllBytes($inputRaw, $speechBytes)
Write-Pcm16Wav -Path $inputWav -PcmBytes $speechBytes -SampleRate 8000

Push-Location $WorkerDir
try {
    Invoke-FreeDvTool -ExePath $txExe -Arguments @("--txbpf", "1", $modeSafe, $inputRaw, $modemRaw) -LogPath $txLog
    Invoke-FreeDvTool -ExePath $rxExe -Arguments @("--squelch", "-20", $modeSafe, $modemRaw, $decodedRaw) -LogPath $rxLog
}
finally {
    Pop-Location
}

$modemBytes = [System.IO.File]::ReadAllBytes($modemRaw)
$decodedBytes = [System.IO.File]::ReadAllBytes($decodedRaw)
Write-Pcm16Wav -Path $modemWav -PcmBytes $modemBytes -SampleRate 8000
Write-Pcm16Wav -Path $decodedWav -PcmBytes $decodedBytes -SampleRate 8000

$inputSamples = [int]($speechBytes.Length / 2)
$modemSamples = [int]($modemBytes.Length / 2)
$decodedSamples = [int]($decodedBytes.Length / 2)

[pscustomobject]@{
    Mode = $modeSafe
    InputSpeechSeconds = [Math]::Round($inputSamples / 8000.0, 3)
    ModemSeconds = [Math]::Round($modemSamples / 8000.0, 3)
    DecodedSpeechSeconds = [Math]::Round($decodedSamples / 8000.0, 3)
    InputWav = $inputWav
    ModemWav = $modemWav
    DecodedWav = $decodedWav
    TxLog = $txLog
    RxLog = $rxLog
}
