$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist"

if (Test-Path $dist) {
    Remove-Item -Recurse -Force $dist
}

Write-Host "Publishing single-file EXE to $dist"

& dotnet publish "$root\src\UsbWiredVirtualDesktop\UsbWiredVirtualDesktop.csproj" `
    -c Release `
    -r win-x64 `
    -p:PublishSingleFile=true `
    -p:SelfContained=true `
    -p:IncludeAllContentForSelfExtract=true `
    -o $dist

Write-Host "Done. Output: $dist\UsbWiredVirtualDesktop.exe"
