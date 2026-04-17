param(
    [string]$SourceBin = "",
    [string]$DestinationBin = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SourceBin)) {
    $SourceBin = "C:\WSJT\wsjtx\bin"
}

if ([string]::IsNullOrWhiteSpace($DestinationBin)) {
    $DestinationBin = Join-Path $env:USERPROFILE "ShackStack\wsjtx-tools\runtime\bin"
}

if (-not (Test-Path $SourceBin)) {
    throw "WSJT-X runtime source folder not found: $SourceBin"
}

New-Item -ItemType Directory -Force -Path $DestinationBin | Out-Null

$filesToCopy = @(
    "jt9.exe",
    "wsprd.exe",
    "ft8sim.exe",
    "q65sim.exe",
    "fst4sim.exe",
    "ALLCALL7.TXT",
    "libboost_filesystem-mgw7-mt-x64-1_74.dll",
    "libboost_log-mgw7-mt-x64-1_74.dll",
    "libboost_log_setup-mgw7-mt-x64-1_74.dll",
    "libboost_regex-mgw7-mt-x64-1_74.dll",
    "libboost_thread-mgw7-mt-x64-1_74.dll",
    "libfftw3f-3.dll",
    "libgcc_s_seh-1.dll",
    "libgfortran-4.dll",
    "libgomp-1.dll",
    "libhamlib-4.dll",
    "libhamlib-4_old.dll",
    "libportaudio-2.dll",
    "libquadmath-0.dll",
    "libstdc++-6.dll",
    "libusb-1.0.dll",
    "libwinpthread-1.dll",
    "Qt5Core.dll",
    "Qt5Gui.dll",
    "Qt5Multimedia.dll",
    "Qt5Network.dll",
    "Qt5PrintSupport.dll",
    "Qt5SerialPort.dll",
    "Qt5Sql.dll",
    "Qt5Svg.dll",
    "Qt5WebSockets.dll",
    "Qt5Widgets.dll",
    "qt.conf"
)

foreach ($file in $filesToCopy) {
    $sourcePath = Join-Path $SourceBin $file
    if (-not (Test-Path $sourcePath)) {
        Write-Warning "Skipping missing WSJT-X runtime file: $sourcePath"
        continue
    }

    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $DestinationBin $file) -Force
}

$soundSource = Join-Path $SourceBin "sounds"
$soundDestination = Join-Path $DestinationBin "sounds"
if (Test-Path $soundSource) {
    New-Item -ItemType Directory -Force -Path $soundDestination | Out-Null
    Copy-Item -LiteralPath (Join-Path $soundSource "*") -Destination $soundDestination -Recurse -Force
}

Write-Host "WSJT-X runtime tools synced into $DestinationBin"
