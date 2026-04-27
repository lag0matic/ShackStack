param(
    [string]$ModeLabel = "JS8 Normal",
    [string]$FrequencyLabel = "20m JS8 14.078 MHz USB-D",
    [string]$StationCallsign = "W8STR",
    [string]$StationGridSquare = "EM79"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\ShackStack.DecoderHost.GplWsjtx\ShackStack.DecoderHost.GplWsjtx.csproj"
if (-not (Test-Path $project)) {
    throw "WSJT/JS8 sidecar project missing: $project"
}

$processStart = [System.Diagnostics.ProcessStartInfo]::new(
    "dotnet",
    "run --project `"$project`" --configuration Release --no-launch-profile")
$processStart.WorkingDirectory = Split-Path -Parent $project
$processStart.UseShellExecute = $false
$processStart.RedirectStandardInput = $true
$processStart.RedirectStandardOutput = $true
$processStart.RedirectStandardError = $true
$processStart.CreateNoWindow = $true

$process = [System.Diagnostics.Process]::Start($processStart)
try {
    $cycleSeconds = switch -Regex ($ModeLabel) {
        "Fast" { 10; break }
        "Turbo" { 6; break }
        "Slow" { 28; break }
        default { 15 }
    }

    $sampleCount = 12000 * $cycleSeconds
    $bytes = New-Object byte[] ($sampleCount * 4)
    $base64 = [Convert]::ToBase64String($bytes)
    $messages = @(
        @{
            type = "configure"
            modeLabel = $ModeLabel
            frequencyLabel = $FrequencyLabel
            stationCallsign = $StationCallsign
            stationGridSquare = $StationGridSquare
        },
        @{
            type = "start"
            modeLabel = $ModeLabel
            frequencyLabel = $FrequencyLabel
            stationCallsign = $StationCallsign
            stationGridSquare = $StationGridSquare
        },
        @{
            type = "audio"
            sampleRate = 12000
            channels = 1
            utcNowUnixMs = 0
            samples = $base64
        },
        @{ type = "shutdown" }
    )

    foreach ($message in $messages) {
        $process.StandardInput.WriteLine(($message | ConvertTo-Json -Compress -Depth 5))
        $process.StandardInput.Flush()
    }
    $process.StandardInput.Close()

    if (-not $process.WaitForExit(60000)) {
        $process.Kill($true)
        throw "JS8 sidecar smoke timed out."
    }

    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $lines = $stdout -split "`r?`n" | Where-Object { $_ }
    $lines

    if ($stderr.Trim().Length -gt 0) {
        Write-Warning $stderr.Trim()
    }

    if ($process.ExitCode -ne 0) {
        throw "JS8 sidecar smoke failed with exit code $($process.ExitCode)."
    }

    if (-not ($lines | Where-Object { $_ -match "JS8 cycle 1: jt9\.exe .* finished" })) {
        throw "JS8 sidecar smoke did not observe a completed JS8 decoder cycle."
    }
}
finally {
    if (-not $process.HasExited) {
        $process.Kill($true)
    }
    $process.Dispose()
}
