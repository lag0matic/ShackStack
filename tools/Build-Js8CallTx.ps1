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
$gfortran = Join-Path $ucrtBin "gfortran.exe"
$gxx = Join-Path $ucrtBin "g++.exe"

foreach ($tool in @($gfortran, $gxx)) {
    if (-not (Test-Path $tool)) {
        throw "Required MSYS2 UCRT tool missing: $tool"
    }
}

$buildRoot = Join-Path $repoRoot ".tmp-js8call-tx-build"
$distRoot = Join-Path $repoRoot "src\ShackStack.Desktop\js8call-tools\runtime\bin"
Remove-Item -Recurse -Force $buildRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $buildRoot, $distRoot | Out-Null

$env:PATH = "$ucrtBin;$env:PATH"

$source = @'
program shackstack_js8tones
  implicit none
  integer :: i
  integer :: nargs
  integer :: i3bit
  integer :: icos
  integer :: msgbits(87)
  integer :: itone(79)
  character(len=22) :: msg
  character(len=22) :: msgsent
  character(len=64) :: arg

  nargs = iargc()
  if (nargs .lt. 1) then
    print *, 'Usage: js8tones MESSAGE12 [I3BIT] [ICOS]'
    call exit(2)
  endif

  msg = '                      '
  call getarg(1, arg)
  msg(1:min(12,len_trim(arg))) = arg(1:min(12,len_trim(arg)))

  i3bit = 0
  if (nargs .ge. 2) then
    call getarg(2, arg)
    read(arg,*) i3bit
  endif

  icos = 1
  if (nargs .ge. 3) then
    call getarg(3, arg)
    read(arg,*) icos
  endif

  call genjs8(msg, icos, i3bit, msgsent, msgbits, itone)
  do i = 1, 79
    write(*,'(I0)', advance='no') itone(i)
    if (i .lt. 79) write(*,'(A)', advance='no') ' '
  end do
  write(*,*)
end program shackstack_js8tones
'@

Push-Location $buildRoot
try {
    $toneSource = Join-Path $buildRoot "shackstack_js8tones.f90"
    Set-Content -Path $toneSource -Encoding ASCII -Value $source

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

    & $gfortran -o js8tones.exe .\crc.o .\crc12.o .\encode174.o .\genjs8.o .\shackstack_js8tones.o -lstdc++
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to link js8tones.exe."
    }

    Copy-Item -Force (Join-Path $buildRoot "js8tones.exe") (Join-Path $distRoot "js8tones.exe")
}
finally {
    Pop-Location
}

& (Join-Path $distRoot "js8tones.exe") W8STRTEST123 0 1
Write-Host "JS8Call TX tone generator built into $distRoot"
