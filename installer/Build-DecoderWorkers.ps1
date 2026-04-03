param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$pythonRoot = Join-Path $repoRoot "src\ShackStack.DecoderHost\Python"
$toolsRoot = Join-Path $pythonRoot "Tools"
$distRoot = Join-Path $repoRoot "src\ShackStack.Desktop\DecoderWorkers"
$pyiRoot = Join-Path $pythonRoot ".pyinstaller"
$workRoot = Join-Path $pyiRoot "build"
$specRoot = Join-Path $pyiRoot "spec"

$workers = @(
    "cw_sidecar_worker",
    "rtty_sidecar_worker",
    "sstv_sidecar_worker",
    "wefax_sidecar_worker"
)

if (Test-Path $distRoot) {
    Remove-Item -Recurse -Force $distRoot
}

New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null
New-Item -ItemType Directory -Force -Path $specRoot | Out-Null

foreach ($worker in $workers) {
    $scriptPath = Join-Path $toolsRoot "$worker.py"
    if (-not (Test-Path $scriptPath)) {
        throw "Worker script not found: $scriptPath"
    }

    & pyinstaller `
        --noconfirm `
        --clean `
        --onedir `
        --name $worker `
        --distpath $distRoot `
        --workpath $workRoot `
        --specpath $specRoot `
        --paths $pythonRoot `
        --hidden-import scipy._cyutility `
        --exclude-module torch `
        --exclude-module matplotlib `
        --exclude-module pytest `
        --exclude-module pandas `
        --exclude-module jupyter_client `
        --exclude-module IPython `
        --exclude-module sympy `
        --exclude-module tkinter `
        --exclude-module pygame `
        $scriptPath
}

Write-Host "Decoder workers built into $distRoot"
