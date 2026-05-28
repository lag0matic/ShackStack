param(
    [string]$SourceUrl = "https://github.com/peterbmarks/radae_nopy.git",
    [string]$SourceDir = "",
    [string]$BuildDir = "",
    [switch]$SkipIfBuilt
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $SourceDir = Join-Path $repoRoot ".tmp-freedv-native\radae_nopy"
}

if ([string]::IsNullOrWhiteSpace($BuildDir)) {
    $BuildDir = Join-Path $repoRoot ".tmp-freedv-native\radae_nopy-build-mingw"
}

$outputDir = Join-Path $BuildDir "src"
$libradePath = Join-Path $outputDir "librade.dll"
$lpcnetDemoPath = Join-Path $outputDir "lpcnet_demo.exe"

if ($SkipIfBuilt -and (Test-Path $libradePath) -and (Test-Path $lpcnetDemoPath)) {
    Write-Host "FreeDV RADE native artifacts already built in $outputDir"
    return
}

$cmakeCandidates = @(
    (Join-Path $env:ProgramFiles "Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"),
    "cmake"
)

$cmake = $cmakeCandidates | Where-Object {
    try {
        $cmd = Get-Command $_ -ErrorAction Stop
        $cmd.Source
    }
    catch {
        $null
    }
} | Select-Object -First 1

if (-not $cmake) {
    throw "CMake was not found. Install Visual Studio CMake tools or put cmake.exe on PATH."
}

$mingwBin = "C:\msys64\ucrt64\bin"
$msysBin = "C:\msys64\usr\bin"
foreach ($path in @($mingwBin, $msysBin)) {
    if (-not (Test-Path $path)) {
        throw "Required MSYS2 path was not found: $path"
    }
}

$env:PATH = "$mingwBin;$msysBin;$env:PATH"

if (-not (Test-Path $SourceDir)) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SourceDir) | Out-Null
    & git clone --depth 1 $SourceUrl $SourceDir
}

if (-not (Test-Path (Join-Path $SourceDir ".git"))) {
    throw "RADE source directory does not look like a git checkout: $SourceDir"
}

Push-Location $SourceDir
try {
    & git submodule update --init --recursive
}
finally {
    Pop-Location
}

$buildOpusPath = Join-Path $SourceDir "cmake\BuildOpus.cmake"
if (-not (Test-Path $buildOpusPath)) {
    throw "Expected RADE Opus build script not found: $buildOpusPath"
}

$buildOpus = Get-Content -Raw -Path $buildOpusPath
$configureReplacement = 'set(CONFIGURE_COMMAND sh -lc "export PATH=/ucrt64/bin:/usr/bin:$PATH && cd <SOURCE_DIR> && ./autogen.sh && ./configure --with-pic --enable-osce --enable-dred --disable-shared --disable-doc --disable-extra-programs")'
$buildOpus = [regex]::Replace(
    $buildOpus,
    'set\(CONFIGURE_COMMAND\s+\.\/configure --with-pic --enable-osce --enable-dred --disable-shared --disable-doc --disable-extra-programs\)',
    [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $configureReplacement })
$buildOpus = $buildOpus.Replace('BUILD_COMMAND $(MAKE)', 'BUILD_COMMAND sh -lc "export PATH=/ucrt64/bin:/usr/bin:$PATH && cd <SOURCE_DIR> && make"')
Set-Content -Path $buildOpusPath -Value $buildOpus -NoNewline

$lpcnetDemoSourcePath = Join-Path $SourceDir "src\lpcnet_demo.c"
if (-not (Test-Path $lpcnetDemoSourcePath)) {
    throw "Expected lpcnet_demo source not found: $lpcnetDemoSourcePath"
}

$lpcnetDemo = Get-Content -Raw -Path $lpcnetDemoSourcePath
$newline = if ($lpcnetDemo.Contains("`r`n")) { "`r`n" } else { "`n" }
$lpcnetDemo = [regex]::Replace(
    $lpcnetDemo,
    '(?m)^(\s*fwrite\(features, sizeof\(float\), NB_TOTAL_FEATURES, fout\);\r?\n)(?!\s*fflush\(fout\);)',
    '$1            fflush(fout);' + $newline)
$lpcnetDemo = [regex]::Replace(
    $lpcnetDemo,
    '(?m)^(\s*fwrite\(pcm, sizeof\(pcm\[0\]\), LPCNET_FRAME_SIZE, fout\);\r?\n)(?!\s*fflush\(fout\);)',
    '$1            fflush(fout);' + $newline)
$lpcnetDemo = [regex]::Replace(
    $lpcnetDemo,
    '(?m)^(\s*fwrite\(features, sizeof\(features\[0\]\), 36, fout\);\r?\n)(?!\s*fflush\(fout\);)',
    '$1            fflush(fout);' + $newline)
Set-Content -Path $lpcnetDemoSourcePath -Value $lpcnetDemo -NoNewline

New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null

& $cmake -S $SourceDir -B $BuildDir -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release
& $cmake --build $BuildDir --config Release

if (-not (Test-Path $libradePath)) {
    throw "RADE build completed without librade.dll: $libradePath"
}

if (-not (Test-Path $lpcnetDemoPath)) {
    throw "RADE build completed without lpcnet_demo.exe: $lpcnetDemoPath"
}

Write-Host "FreeDV RADE native artifacts built in $outputDir"
