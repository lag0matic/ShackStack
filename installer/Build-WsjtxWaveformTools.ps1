param(
    [string]$WsjtSourceRoot = "",
    [string]$OutputBin = "",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($WsjtSourceRoot)) {
    $WsjtSourceRoot = Join-Path $repoRoot "_wsjtx_ref"
}

if (-not (Test-Path $WsjtSourceRoot)) {
    throw "WSJT-X source tree not found: $WsjtSourceRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputBin)) {
    $OutputBin = $env:SHACKSTACK_WSJTX_WAVEFORM_BIN
}
if ([string]::IsNullOrWhiteSpace($OutputBin)) {
    $OutputBin = Join-Path $env:USERPROFILE "ShackStack\wsjtx-tools\waveform\bin"
}

$ucrtBin = "C:\msys64\ucrt64\bin"
foreach ($tool in @("cmake.exe", "ninja.exe", "gfortran.exe", "gcc.exe", "g++.exe")) {
    if (-not (Test-Path (Join-Path $ucrtBin $tool))) {
        throw "Required MSYS2 tool not found: $(Join-Path $ucrtBin $tool)"
    }
}

$toolsRoot = Join-Path $repoRoot ".wsjtx-waveform-tools"
$generatedSourceRoot = Join-Path $toolsRoot "src"
$buildRoot = Join-Path $toolsRoot "build"
$installRoot = Join-Path $toolsRoot "install"

if ($Clean -and (Test-Path $toolsRoot)) {
    Remove-Item -LiteralPath $toolsRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $generatedSourceRoot | Out-Null
New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
New-Item -ItemType Directory -Force -Path $OutputBin | Out-Null

$installPrefix = [IO.Path]::GetFullPath($installRoot).Replace('\', '/')

$wsjtLines = Get-Content -LiteralPath (Join-Path $WsjtSourceRoot "CMakeLists.txt")

function Get-CMakeSlice {
    param(
        [int]$StartLine,
        [int]$EndLine
    )

    return ($wsjtLines[($StartLine - 1)..($EndLine - 1)] -join "`n")
}

function Convert-UpstreamBlock {
    param(
        [string]$Text
    )

    $text = $Text -replace '(?m)^(\s*)([A-Za-z0-9_./-]+\.(?:c|cc|cpp|cxx|f|f90|h|hpp|ui|png|pal|svg|txt))(\s*)$', '$1${WSJTX_REF_ROOT}/$2$3'
    $text = $text -replace 'set_source_files_properties \((lib/[^ )]+)', 'set_source_files_properties (${WSJTX_REF_ROOT}/$1'
    return $text
}

$sourceListBlock = Convert-UpstreamBlock (Get-CMakeSlice -StartLine 163 -EndLine 769)
$wsjtxRootCmake = [IO.Path]::GetFullPath($WsjtSourceRoot).Replace('\', '/')

$cmakeTemplate = @'
cmake_minimum_required(VERSION 3.20)
project(ShackStackWsjtxWaveformTools VERSION 2.7.0 LANGUAGES C CXX Fortran)

include(GNUInstallDirs)
set(WSJTX_REF_ROOT [=[__WSJTX_REF_ROOT__]=])
set(BUILD_TYPE_REVISION "")
set(PROJECT_VENDOR "The WSJT Project")
set(PROJECT_NAME "WSJT-X")
set(PROJECT_COPYRIGHT "Copyright (C) 2001-2025 by Joseph H. Taylor, Jr., K1JT")
set(PROJECT_MANUAL "WSJT-X User Guide")
set(PROJECT_HOMEPAGE "https://wsjt.sourceforge.io/")
set(PROJECT_MANUAL_DIRECTORY_URL "https://wsjt.sourceforge.io/wsjtx-doc/")
set(PROJECT_SAMPLES_URL "https://wsjt.sourceforge.io/samples/")
set(PROJECT_DESCRIPTION "WSJT-X waveform helper utilities")
set(HAVE_HAMLIB_OLD_CACHING 0)
set(HAVE_HAMLIB_CACHING 0)
set(HAVE_STDIO_H 1)
set(STDC_HEADERS 1)
set(HAVE_STDLIB_H 1)
set(HAVE_UNISTD_H 1)
set(HAVE_SYS_IOCTL_H 0)
set(HAVE_FCNTL_H 1)
set(HAVE_SYS_STAT_H 1)
set(HAVE_LINUX_PPDEV_H 0)
set(HAVE_DEV_PPBUS_PPI_H 0)
set(WSJT_SHARED_RUNTIME 0)
set(WSJT_SOFT_KEYING 1)
set(WSJT_ENABLE_EXPERIMENTAL_FEATURES 1)
set(WSJT_RIG_NONE_CAN_SPLIT 0)
set(WSJT_TRACE_UDP 0)
set(WSJT_FOX_OTP ON)

set(LIBM_LIBRARIES)

find_package(Boost REQUIRED COMPONENTS log_setup log)
find_library(FFTW3F_LIBRARY NAMES fftw3f REQUIRED)
find_library(FFTW3F_THREADS_LIBRARY NAMES fftw3f_threads)
set(WSJT_FFTW_LIBRARIES ${FFTW3F_LIBRARY})
if (FFTW3F_THREADS_LIBRARY)
  list(APPEND WSJT_FFTW_LIBRARIES ${FFTW3F_THREADS_LIBRARY})
endif()

set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} -Wall -Wextra")
set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -Werror -Wall -Wextra -fexceptions -frtti -Wno-pragmas")
add_definitions(-DQT5 -DCMAKE_BUILD -DBIGSYM=1)

include_directories(
  ${WSJTX_REF_ROOT}
  ${CMAKE_CURRENT_BINARY_DIR}
)

__SOURCE_LIST_BLOCK__

configure_file(
  "${WSJTX_REF_ROOT}/wsjtx_config.h.in"
  "${CMAKE_CURRENT_BINARY_DIR}/wsjtx_config.h"
  @ONLY
)

file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/fortran_modules")

set_property(SOURCE ${all_C_and_CXXSRCS} APPEND_STRING PROPERTY COMPILE_FLAGS " -include wsjtx_config.h")
set_property(SOURCE ${all_C_and_CXXSRCS} APPEND PROPERTY OBJECT_DEPENDS ${CMAKE_CURRENT_BINARY_DIR}/wsjtx_config.h)

add_library(wsjt_cxx STATIC ${wsjt_CSRCS} ${wsjt_CXXSRCS})
target_link_libraries(wsjt_cxx PRIVATE ${LIBM_LIBRARIES} Boost::log_setup Boost::log)

add_library(wsjt_fort STATIC ${wsjt_FSRCS})
set_target_properties(wsjt_fort PROPERTIES Fortran_MODULE_DIRECTORY "${CMAKE_BINARY_DIR}/fortran_modules")
target_include_directories(wsjt_fort PUBLIC "${CMAKE_BINARY_DIR}/fortran_modules")
target_link_libraries(wsjt_fort PUBLIC ${WSJT_FFTW_LIBRARIES})

add_executable(ft4sim ${WSJTX_REF_ROOT}/lib/ft4/ft4sim.f90)
target_link_libraries(ft4sim PRIVATE wsjt_fort wsjt_cxx)

add_executable(jt4sim ${WSJTX_REF_ROOT}/lib/jt4sim.f90)
target_link_libraries(jt4sim PRIVATE wsjt_fort wsjt_cxx)

add_executable(jt49sim ${WSJTX_REF_ROOT}/lib/jt49sim.f90)
target_link_libraries(jt49sim PRIVATE wsjt_fort wsjt_cxx)

add_executable(jt65sim ${WSJTX_REF_ROOT}/lib/jt65sim.f90)
target_link_libraries(jt65sim PRIVATE wsjt_fort wsjt_cxx)

add_executable(msk144sim ${WSJTX_REF_ROOT}/lib/msk144sim.f90)
target_link_libraries(msk144sim PRIVATE wsjt_fort wsjt_cxx)

add_executable(ft4code ${WSJTX_REF_ROOT}/lib/ft4/ft4code.f90)
target_link_libraries(ft4code PRIVATE wsjt_fort wsjt_cxx)

add_executable(jt4code ${WSJTX_REF_ROOT}/lib/jt4code.f90)
target_link_libraries(jt4code PRIVATE wsjt_fort wsjt_cxx)

add_executable(jt65code ${WSJTX_REF_ROOT}/lib/jt65code.f90)
target_link_libraries(jt65code PRIVATE wsjt_fort wsjt_cxx)

add_executable(jt9code ${WSJTX_REF_ROOT}/lib/jt9code.f90)
target_link_libraries(jt9code PRIVATE wsjt_fort wsjt_cxx)

add_executable(msk144code ${WSJTX_REF_ROOT}/lib/msk144code.f90)
target_link_libraries(msk144code PRIVATE wsjt_fort wsjt_cxx)

add_executable(wsprcode ${WSJTX_REF_ROOT}/lib/wsprcode/wsprcode.f90 ${WSJTX_REF_ROOT}/lib/wsprcode/nhash.c)
target_link_libraries(wsprcode PRIVATE wsjt_fort wsjt_cxx)

add_executable(wsprsim ${wsprsim_CSRCS})
target_link_libraries(wsprsim PRIVATE ${LIBM_LIBRARIES})

install(TARGETS
  ft4sim
  jt4sim
  jt49sim
  jt65sim
  msk144sim
  ft4code
  jt4code
  jt65code
  jt9code
  msk144code
  wsprcode
  wsprsim
  RUNTIME DESTINATION .
)
'@

$cmakeText = $cmakeTemplate.Replace('__WSJTX_REF_ROOT__', $wsjtxRootCmake).Replace('__SOURCE_LIST_BLOCK__', $sourceListBlock)

$cmakePath = Join-Path $generatedSourceRoot "CMakeLists.txt"
Set-Content -LiteralPath $cmakePath -Value $cmakeText -Encoding ascii

$env:Path = "$ucrtBin;$env:Path"
$env:CMAKE_PREFIX_PATH = "C:\msys64\ucrt64"

& (Join-Path $ucrtBin "cmake.exe") `
    -S $generatedSourceRoot `
    -B $buildRoot `
    -G Ninja `
    -DCMAKE_BUILD_TYPE=Release `
    -DCMAKE_PREFIX_PATH="C:\msys64\ucrt64" `
    "-DCMAKE_INSTALL_PREFIX=$installPrefix"
if ($LASTEXITCODE -ne 0) {
    throw "WSJT-X waveform tools CMake configure failed"
}

& (Join-Path $ucrtBin "cmake.exe") `
    --build $buildRoot `
    --target ft4sim jt4sim jt49sim jt65sim msk144sim ft4code jt4code jt65code jt9code msk144code wsprcode wsprsim
if ($LASTEXITCODE -ne 0) {
    throw "WSJT-X waveform tools build failed"
}

& (Join-Path $ucrtBin "cmake.exe") --install $buildRoot
if ($LASTEXITCODE -ne 0) {
    throw "WSJT-X waveform tools install failed"
}

$targets = @(
    "ft4sim.exe",
    "jt4sim.exe",
    "jt49sim.exe",
    "jt65sim.exe",
    "msk144sim.exe",
    "ft4code.exe",
    "jt4code.exe",
    "jt65code.exe",
    "jt9code.exe",
    "msk144code.exe",
    "wsprcode.exe",
    "wsprsim.exe"
)

$runtimeDlls = @(
    "libfftw3f-3.dll",
    "libfftw3f_threads-3.dll",
    "libgcc_s_seh-1.dll",
    "libgfortran-5.dll",
    "libquadmath-0.dll",
    "libstdc++-6.dll",
    "libwinpthread-1.dll"
)

foreach ($target in $targets) {
    $builtPath = Join-Path $installRoot $target
    if (-not (Test-Path $builtPath)) {
        throw "Expected utility not produced: $builtPath"
    }

    Copy-Item -LiteralPath $builtPath -Destination (Join-Path $OutputBin $target) -Force
}

foreach ($dll in $runtimeDlls) {
    $dllPath = Join-Path $ucrtBin $dll
    if (Test-Path $dllPath) {
        Copy-Item -LiteralPath $dllPath -Destination (Join-Path $OutputBin $dll) -Force
    }
}

Write-Host "WSJT-X waveform tools built and copied into $OutputBin"
