[CmdletBinding()]
param(
    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64",

    [ValidatePattern("^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$")]
    [string]$Version = "1.0.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSCommandPath
$solution = Join-Path $repoRoot "TetherMate.sln"
$project = Join-Path $repoRoot "src\TetherMate\TetherMate.csproj"
$tests = Join-Path $repoRoot "tests\TetherMate.Core.Tests\TetherMate.Core.Tests.csproj"
$dist = Join-Path $repoRoot "dist"
$publishDirectory = Join-Path $dist "publish"
$packageName = "TetherMate-$Version-$RuntimeIdentifier"
$packageDirectory = Join-Path $dist $packageName
$zipPath = Join-Path $dist "$packageName.zip"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0"
}

if (Test-Path -LiteralPath $dist) {
    Remove-Item -LiteralPath $dist -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

Write-Host "Restoring and building TetherMate..."
& dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

& dotnet restore $project -r $RuntimeIdentifier
if ($LASTEXITCODE -ne 0) { throw "runtime restore failed." }

& dotnet build $solution -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

Write-Host "Running core tests..."
& dotnet run --project $tests -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

Write-Host "Publishing the self-contained Windows executable..."
& dotnet publish $project `
    -c Release `
    -r $RuntimeIdentifier `
    --self-contained true `
    --no-restore `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$publishedExe = Join-Path $publishDirectory "TetherMate.exe"
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "Publish completed without producing TetherMate.exe."
}

Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $packageDirectory "TetherMate.exe")
Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $dist "TetherMate.exe")
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $packageDirectory "LICENSE.txt")
Copy-Item -LiteralPath (Join-Path $repoRoot "NOTICE.txt") -Destination (Join-Path $packageDirectory "THIRD-PARTY-NOTICES.txt")
Copy-Item -LiteralPath (Join-Path $repoRoot "licenses\gnirehtet-LICENSE.txt") -Destination (Join-Path $packageDirectory "gnirehtet-LICENSE.txt")
Copy-Item -LiteralPath (Join-Path $repoRoot "packaging\README.txt") -Destination (Join-Path $packageDirectory "README.txt")

Compress-Archive -Path (Join-Path $packageDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $dist "SHA256SUMS.txt"
"$hash  $([System.IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath $checksumPath -Encoding ascii

Remove-Item -LiteralPath $publishDirectory -Recurse -Force

Write-Host ""
Write-Host "Build complete:"
Write-Host "  EXE: $dist\TetherMate.exe"
Write-Host "  ZIP: $zipPath"
Write-Host "  SHA: $checksumPath"
