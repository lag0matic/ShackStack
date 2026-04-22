param(
    [switch]$UseGgmorse
)

$project = "C:\Users\lag0m\Documents\ShackStack.Avalonia\src\ShackStack.Desktop\ShackStack.Desktop.csproj"

$env:MSBUILDDISABLENODEREUSE = "1"

if ($UseGgmorse) {
    $env:SHACKSTACK_CW_GGMORSE = "1"
} else {
    Remove-Item Env:SHACKSTACK_CW_GGMORSE -ErrorAction SilentlyContinue
}

dotnet run `
    --project $project `
    --no-launch-profile `
    --disable-build-servers
