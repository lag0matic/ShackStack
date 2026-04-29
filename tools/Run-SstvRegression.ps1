param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\ShackStack.DecoderHost.Sstv.Harness\ShackStack.DecoderHost.Sstv.Harness.csproj"

Push-Location $repoRoot
try {
    dotnet run --project $project -c $Configuration -- --regression
}
finally {
    Pop-Location
}
