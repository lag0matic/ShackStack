param(
    [string]$Js8CallSource = "",
    [string]$MsysRoot = "C:\msys64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Js8CallSource)) {
    $Js8CallSource = Join-Path $repoRoot ".tmp-js8call-repo"
}

$sourceRoot = Resolve-Path $Js8CallSource
$libRoot = Join-Path $sourceRoot "lib"
$ucrtBin = Join-Path $MsysRoot "ucrt64\bin"
$ucrtLib = Join-Path $MsysRoot "ucrt64\lib"
$gfortran = Join-Path $ucrtBin "gfortran.exe"
$gcc = Join-Path $ucrtBin "gcc.exe"
$gxx = Join-Path $ucrtBin "g++.exe"

foreach ($tool in @($gfortran, $gcc, $gxx)) {
    if (-not (Test-Path $tool)) {
        throw "Required MSYS2 UCRT tool missing: $tool"
    }
}

$buildRoot = Join-Path $repoRoot ".tmp-js8call-build"
$distRoot = Join-Path $repoRoot "src\ShackStack.Desktop\js8call-tools\runtime\bin"
Remove-Item -Recurse -Force $buildRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
New-Item -ItemType Directory -Force -Path $distRoot | Out-Null

$env:PATH = "$ucrtBin;$env:PATH"
$fortranFlags = @(
    "-O2",
    "-ffree-line-length-none",
    "-I", $libRoot,
    "-I", (Join-Path $libRoot "js8"),
    "-I", (Join-Path $MsysRoot "ucrt64\include")
)

$sources = @(
    "options.f90",
    "prog_args.f90",
    "fftw3mod.f90",
    "timer_module.f90",
    "iso_c_utilities.f90",
    "timer_impl.f90",
    "readwav.f90",
    "crc.f90",
    "packjt.f90",
    "js8a_module.f90",
    "js8b_module.f90",
    "js8c_module.f90",
    "js8e_module.f90",
    "js8i_module.f90",
    "js8a_decode.f90",
    "js8b_decode.f90",
    "js8c_decode.f90",
    "js8e_decode.f90",
    "js8i_decode.f90",
    "decoder.f90",
    "db.f90",
    "four2a.f90",
    "filbig.f90",
    "indexx.f90",
    "pctile.f90",
    "shell.f90",
    "smo.f90",
    "nuttal_window.f90",
    "polyfit.f90",
    "fil4.f90",
    "fmtmsg.f90",
    "stdmsg.f90",
    "twkfreq.f90",
    "sleep_msec.f90",
    "ft8\bpdecode174.f90",
    "ft8\osd174.f90",
    "ft8\chkcrc12a.f90",
    "ft8\extractmessage174.f90",
    "ft8\encode174.f90",
    "ft8\twkfreq1.f90",
    "js8\genjs8.f90",
    "grid2deg.f90",
    "deg2grid.f90",
    "determ.f90",
    "jt9.f90"
)

Push-Location $buildRoot
try {
    & $gxx -O2 -I (Join-Path $MsysRoot "ucrt64\include") -c `
        (Join-Path $libRoot "crc10.cpp") `
        (Join-Path $libRoot "crc12.cpp") `
        (Join-Path $libRoot "crc14.cpp")

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to compile JS8 CRC support."
    }

    & $gcc -O2 -c (Join-Path $libRoot "usleep.c")
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to compile JS8 sleep shim."
    }

    @'
subroutine jt9a()
  print *, 'Shared-memory JS8 backend is not available in ShackStack standalone jt9 build.'
  return
end subroutine jt9a
'@ | Set-Content -Encoding ASCII (Join-Path $buildRoot "jt9a_stub.f90")
    & $gfortran -O2 -c (Join-Path $buildRoot "jt9a_stub.f90")
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to compile standalone jt9a stub."
    }

    foreach ($source in $sources) {
        Write-Host "Compiling $source"
        & $gfortran @fortranFlags -c (Join-Path $libRoot $source)
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to compile $source"
        }
    }

    $objects = Get-ChildItem -Filter *.o | ForEach-Object { $_.FullName }
    & $gfortran -o jt9.exe @objects -L$ucrtLib -lfftw3f_threads -lfftw3f -lstdc++
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to link JS8Call standalone jt9.exe"
    }

    Copy-Item -Force (Join-Path $buildRoot "jt9.exe") (Join-Path $distRoot "jt9.exe")
}
finally {
    Pop-Location
}

$runtimeDlls = @(
    "libfftw3f-3.dll",
    "libfftw3f_threads-3.dll",
    "libgcc_s_seh-1.dll",
    "libgfortran-5.dll",
    "libquadmath-0.dll",
    "libstdc++-6.dll",
    "libwinpthread-1.dll"
)

foreach ($dll in $runtimeDlls) {
    $source = Join-Path $ucrtBin $dll
    if (-not (Test-Path $source)) {
        throw "Required runtime DLL missing: $source"
    }

    Copy-Item -Force $source (Join-Path $distRoot $dll)
}

Copy-Item -Force (Join-Path $sourceRoot "COPYING") (Join-Path $distRoot "JS8CALL-COPYING.txt")
Copy-Item -Force (Join-Path $sourceRoot "jsc_map.cpp") (Join-Path $distRoot "jsc_map.cpp")

& (Join-Path $distRoot "jt9.exe") --help | Select-Object -First 8
Write-Host "JS8Call standalone decoder built into $distRoot"
