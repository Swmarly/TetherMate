[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^v?\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$")]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSCommandPath
$cleanVersion = $Version.TrimStart("v")
$tag = "v$cleanVersion"

Push-Location $repoRoot
try {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw "Git is required."
    }

    $changes = & git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect the Git working tree." }
    if ($changes) {
        throw "Commit or stash local changes before creating a release."
    }

    $existingTag = & git tag --list $tag
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect existing tags." }
    if ($existingTag) {
        throw "Tag $tag already exists."
    }

    & (Join-Path $repoRoot "build.ps1") -Version $cleanVersion
    if ($LASTEXITCODE -ne 0) { throw "Release build failed." }

    & git tag -a $tag -m "TetherMate $cleanVersion"
    if ($LASTEXITCODE -ne 0) { throw "Could not create $tag." }

    & git push origin $tag
    if ($LASTEXITCODE -ne 0) { throw "Could not push $tag." }

    Write-Host "Pushed $tag. GitHub Actions will build and publish the release."
}
finally {
    Pop-Location
}
