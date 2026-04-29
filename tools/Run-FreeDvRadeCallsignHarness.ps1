param(
    [string]$RadeSourceDir = ".tmp-radae-nopy-repo",
    [string]$RadeBuildDir = ".tmp-radae-nopy-build-mingw-sh3",
    [string]$OutDir = ".tmp-freedv-rade-callsign-harness"
)

$ErrorActionPreference = "Stop"
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue) {
    $Global:PSNativeCommandUseErrorActionPreference = $false
}
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

function Invoke-NativeCapture {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @()
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $escapedArguments = foreach ($argument in $ArgumentList) {
        if ($argument -match '[\s"]') {
            '"' + ($argument -replace '"', '\"') + '"'
        } else {
            $argument
        }
    }
    $startInfo.Arguments = $escapedArguments -join " "
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output = @($stdout, $stderr) -join ""
    }
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$RadeSourceDir = if ([System.IO.Path]::IsPathRooted($RadeSourceDir)) {
    [System.IO.Path]::GetFullPath($RadeSourceDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $RadeSourceDir))
}
$RadeBuildDir = if ([System.IO.Path]::IsPathRooted($RadeBuildDir)) {
    [System.IO.Path]::GetFullPath($RadeBuildDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $RadeBuildDir))
}
$OutDir = if ([System.IO.Path]::IsPathRooted($OutDir)) {
    [System.IO.Path]::GetFullPath($OutDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutDir))
}

$radeSrc = Join-Path $RadeSourceDir "src"
$radeDll = Join-Path $RadeBuildDir "src\librade.dll"
$radeImportLib = Join-Path $RadeBuildDir "src\librade.dll.a"
$opusBuild = Join-Path $RadeBuildDir "build_opus-prefix\src\build_opus"

foreach ($required in @(
    (Join-Path $radeSrc "rade_callsign_test.c"),
    (Join-Path $radeSrc "rade_ofdm.c"),
    (Join-Path $radeSrc "rade_dsp.c"),
    $radeDll,
    $radeImportLib,
    (Join-Path $opusBuild "dnn\nnet.h")
)) {
    if (!(Test-Path $required)) {
        throw "Required RADE callsign harness dependency missing: $required"
    }
}

$gcc = "C:\msys64\ucrt64\bin\gcc.exe"
if (!(Test-Path $gcc)) {
    throw "MSYS2 UCRT gcc not found: $gcc"
}
$env:PATH = "C:\msys64\ucrt64\bin;C:\msys64\usr\bin;$env:PATH"

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$exePath = Join-Path $OutDir "rade_callsign_test.exe"
$logPath = Join-Path $OutDir "rade_callsign_test.log"
$opusInclude = Join-Path $opusBuild "include"
$opusDnn = Join-Path $opusBuild "dnn"
$radeBuildSrc = Join-Path $RadeBuildDir "src"

$arguments = @(
    "-O2",
    "-I$radeSrc",
    "-I$opusBuild",
    "-I$opusInclude",
    "-I$opusDnn",
    (Join-Path $radeSrc "rade_callsign_test.c"),
    (Join-Path $radeSrc "rade_ofdm.c"),
    (Join-Path $radeSrc "rade_dsp.c"),
    "-L$radeBuildSrc",
    "-lrade",
    "-o",
    $exePath
)

$compile = Invoke-NativeCapture -FilePath $gcc -ArgumentList $arguments
$compile.Output | Tee-Object -FilePath $logPath
if ($compile.ExitCode -ne 0) {
    throw "RADE callsign harness compile failed. See $logPath"
}

$env:PATH = "$radeBuildSrc;$env:PATH"
$test = Invoke-NativeCapture -FilePath $exePath
$output = $test.Output
$output | Tee-Object -FilePath $logPath -Append
if ($test.ExitCode -ne 0) {
    throw "RADE callsign harness failed. See $logPath"
}

if (($output -join "`n") -notmatch "6/6 PASSED") {
    throw "RADE callsign harness did not report full pass. See $logPath"
}

[pscustomobject]@{
    Status = "Passed"
    Cases = "6/6"
    Executable = $exePath
    Log = $logPath
}
