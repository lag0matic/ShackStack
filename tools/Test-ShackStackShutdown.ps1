param(
    [string]$AppPath = "",
    [int]$StartupSeconds = 5,
    [switch]$Launch
)

$ErrorActionPreference = "Stop"

function Get-ShackStackProcessSnapshot {
    $names = @(
        "ShackStack.Desktop",
        "ShackStack.DecoderHost.GplCodec2Freedv",
        "ShackStack.DecoderHost.GplFldigiPsk",
        "ShackStack.DecoderHost.GplFldigiRtty",
        "ShackStack.DecoderHost.GplWsjtx",
        "ShackStack.DecoderHost.Sstv",
        "ShackStack.DecoderHost.Sstv.Harness"
    )

    Get-CimInstance Win32_Process |
        Where-Object {
            $name = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
            $cmd = $_.CommandLine
            ($names -contains $name) -or
                ($cmd -and $cmd -match "ShackStack\.Desktop\.dll|ShackStack\.DecoderHost|DecoderWorkers|sstv_native_sidecar|wsjtx_gpl_sidecar|psk_sidecar_worker|rtty_gpl_sidecar|freedv_codec2_sidecar")
        } |
        Sort-Object Name, ProcessId |
        Select-Object ProcessId, ParentProcessId, Name, CommandLine
}

if ($Launch) {
    if ([string]::IsNullOrWhiteSpace($AppPath)) {
        $AppPath = Join-Path $PSScriptRoot "..\src\ShackStack.Desktop\bin\Release\net9.0\ShackStack.Desktop.exe"
    }

    $resolvedApp = Resolve-Path -LiteralPath $AppPath
    Write-Host "Launching $resolvedApp"
    $app = Start-Process -FilePath $resolvedApp -PassThru
    Start-Sleep -Seconds $StartupSeconds

    Write-Host ""
    Write-Host "ShackStack is running as PID $($app.Id). Open desks/start-stop RX as desired, then close the app."
    Read-Host "Press Enter after ShackStack is closed to inspect leftover processes"
}

$snapshot = @(Get-ShackStackProcessSnapshot)
if ($snapshot.Count -eq 0) {
    Write-Host "No ShackStack-related processes found."
    exit 0
}

$snapshot | Format-Table -AutoSize -Wrap
Write-Host ""
Write-Host "Found $($snapshot.Count) ShackStack-related process(es). If the app is closed, anything above is suspicious."
