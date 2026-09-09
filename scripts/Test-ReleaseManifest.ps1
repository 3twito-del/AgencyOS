<#
.SYNOPSIS
    Checks that a release manifest describes the artifacts sitting beside it.

.DESCRIPTION
    A manifest is a claim. This verifies it: every artifact it names exists, every
    recorded SHA-256 matches the file on disk, and nothing is present that the
    manifest does not account for.

    The last check is the one worth having. A manifest that lists what it expects
    would still pass if an extra file had been dropped into the release directory,
    and "the artifact set is exactly this" is the claim a deployment actually
    depends on (M15 §60, ADR-0039).

    Filenames are not evidence. This is what makes the hashes evidence.

.PARAMETER ReleasePath
    Directory produced by `Invoke-AgencyOS.ps1 release`.

.EXAMPLE
    ./scripts/Test-ReleaseManifest.ps1 -ReleasePath artifacts/release
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ReleasePath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$manifestPath = Join-Path $ReleasePath "release-manifest.json"

if (-not (Test-Path $manifestPath)) {
    throw "No release manifest at '$manifestPath'."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

if ($manifest.formatVersion -ne 1) {
    throw "Release manifest format version $($manifest.formatVersion) is not one this script understands."
}

# The bindings that make an artifact traceable. A manifest missing any of these
# describes a build nobody can locate again.
foreach ($field in @("version", "channel", "buildId", "gitCommit", "apiContractVersion", "expectedSchema")) {
    if (-not $manifest.PSObject.Properties[$field] -or -not "$($manifest.$field)") {
        throw "The release manifest does not record '$field'. An artifact that cannot be traced to a build is not a release."
    }
}

$mismatched = @()
$absent = @()
$claimed = @{}

foreach ($artifact in $manifest.artifacts) {
    $claimed[$artifact.path] = $true

    $file = Join-Path $ReleasePath ($artifact.path -replace '/', [IO.Path]::DirectorySeparatorChar)

    if (-not (Test-Path -LiteralPath $file)) {
        $absent += $artifact.path
        continue
    }

    $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()

    if ($actual -ne $artifact.sha256) {
        $mismatched += "$($artifact.path): manifest $($artifact.sha256), found $actual"
    }
}

# Anything present but unaccounted for. The manifest itself is expected.
$unaccounted = @()

Get-ChildItem -LiteralPath $ReleasePath -Recurse -File | ForEach-Object {
    $relative = [System.IO.Path]::GetRelativePath($ReleasePath, $_.FullName) -replace '\\', '/'

    if ($relative -eq "release-manifest.json") { return }

    if (-not $claimed.ContainsKey($relative)) {
        $script:unaccounted += $relative
    }
}

$problems = $absent.Count + $mismatched.Count + $unaccounted.Count

if ($problems -gt 0) {
    foreach ($item in $absent) { Write-Host "MISSING      $item" }
    foreach ($item in $mismatched) { Write-Host "HASH         $item" }
    foreach ($item in $unaccounted) { Write-Host "UNACCOUNTED  $item" }

    throw "The release manifest does not describe this directory: $problems discrepancies."
}

Write-Host "[OK] Release manifest verified"
Write-Host "     version $($manifest.version) channel $($manifest.channel) build $($manifest.buildId)"
Write-Host "     commit $($manifest.gitCommit)"
Write-Host "     contract $($manifest.apiContractVersion) schema $($manifest.expectedSchema)"
Write-Host "     signing $($manifest.signing)"
Write-Host "     $($manifest.artifacts.Count) artifacts, every hash verified"
