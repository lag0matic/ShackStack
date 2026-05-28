param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet(11025, 12000)]
    [int]$WorkingSampleRate = 12000
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\ShackStack.DecoderHost.Sstv.Harness\ShackStack.DecoderHost.Sstv.Harness.csproj"

Push-Location $repoRoot
try {
    $previousWorkingRate = $env:SHACKSTACK_SSTV_WORKING_SAMPLE_RATE
    $env:SHACKSTACK_SSTV_WORKING_SAMPLE_RATE = "$WorkingSampleRate"
    Write-Host "SSTV working sample rate: $WorkingSampleRate Hz"
    dotnet run --project $project -c $Configuration -- --regression
}
finally {
    if ($null -eq $previousWorkingRate) {
        Remove-Item Env:\SHACKSTACK_SSTV_WORKING_SAMPLE_RATE -ErrorAction SilentlyContinue
    }
    else {
        $env:SHACKSTACK_SSTV_WORKING_SAMPLE_RATE = $previousWorkingRate
    }
    Pop-Location
}
