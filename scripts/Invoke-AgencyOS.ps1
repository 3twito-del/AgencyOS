<#
.SYNOPSIS
    Canonical local and CI engineering entrypoint for AgencyOS.

.DESCRIPTION
    Every build, test and release action goes through this script so VS Code
    tasks, Claude Code and GitHub Actions run identical commands
    (CLAUDE.md section 8).

.PARAMETER Target
    The action to run.

.PARAMETER Channel
    Release ring to stamp into build metadata (docs/05_RELEASE_ENGINEERING.md).
    Defaults to the value in build/Version.props, which is 'forge' - the only
    ring that forbids real data outright.

.PARAMETER BuildId
    Build identifier to stamp. Defaults to a UTC timestamp.

.PARAMETER Configuration
    MSBuild configuration. Defaults to Debug, except 'nightly' which uses Release.
#>
param(
    [Parameter(Position=0)]
    [ValidateSet("doctor","build","test","verify","verify-fast","version","ci","nightly")]
    [string]$Target = "doctor",

    [ValidateSet("forge","lab","nightly","alpha","beta","rc","stable")]
    [string]$Channel,

    [string]$BuildId,

    [ValidateSet("Debug","Release")]
    [string]$Configuration
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

function Write-Section([string]$Title) {
    Write-Host ""
    Write-Host "=== $Title ==="
}

function Find-Solution {
    $candidate = Join-Path $root "AgencyOS.sln"
    if (Test-Path $candidate) { return $candidate }

    $sln = Get-ChildItem -Path $root -Filter "*.sln" -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($sln) { return $sln.FullName }
    return $null
}

function Get-Solution {
    $solution = Find-Solution
    if (-not $solution) {
        throw "No AgencyOS solution exists yet. Run M0 first using prompts/001_M0_REPOSITORY.md."
    }
    return $solution
}

# Build metadata overrides are passed to MSBuild only when explicitly supplied,
# so build/Version.props remains the single source of default values.
function Get-MetadataArgs {
    $metadataArgs = @()
    if ($Channel) { $metadataArgs += "-p:AgencyOSChannel=$Channel" }
    if ($BuildId) { $metadataArgs += "-p:AgencyOSBuildId=$BuildId" }
    return $metadataArgs
}

function Get-Configuration([string]$Default) {
    if ($Configuration) { return $Configuration }
    return $Default
}

function Test-GitRepository {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { return $false }
    git rev-parse --is-inside-work-tree *> $null
    return ($LASTEXITCODE -eq 0)
}

function Invoke-Doctor {
    Write-Section "AgencyOS Doctor"
    Write-Host "Root: $root"

    foreach ($cmd in @("git","dotnet","pwsh")) {
        $found = Get-Command $cmd -ErrorAction SilentlyContinue
        if ($found) { Write-Host "[OK] $cmd -> $($found.Source)" }
        else { Write-Host "[MISSING] $cmd" }
    }

    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        Write-Host ""
        dotnet --version
    }

    $solution = Find-Solution
    if ($solution) {
        Write-Host "[OK] Solution: $solution"
    } else {
        Write-Host "[INFO] No .sln yet. This is expected before M0 implementation."
    }

    if (Test-Path "global.json")              { Write-Host "[OK] global.json (pinned SDK)" }
    if (Test-Path "Directory.Build.props")    { Write-Host "[OK] Directory.Build.props" }
    if (Test-Path "Directory.Packages.props") { Write-Host "[OK] Directory.Packages.props (central package management)" }
    if (Test-Path ".claude/settings.json")    { Write-Host "[OK] .claude/settings.json" }
    if (Test-Path "CLAUDE.md")                { Write-Host "[OK] CLAUDE.md" }

    if (Test-GitRepository) {
        Write-Host "[OK] git repository initialized"
    } else {
        Write-Host "[WARN] Not a git repository. Build metadata will report an unknown commit."
    }
}

function Invoke-Version {
    Write-Section "Version Metadata"
    $contracts = Join-Path $root "src/AgencyOS.Contracts/AgencyOS.Contracts.csproj"

    dotnet build $contracts --nologo -t:AgencyOSWriteBuildMetadata @(Get-MetadataArgs)
    if ($LASTEXITCODE -ne 0) { throw "Version metadata generation failed." }

    $metadataPath = Join-Path $root "artifacts/build-metadata.json"
    if (Test-Path $metadataPath) {
        Write-Host ""
        Get-Content $metadataPath | Write-Host
    }
}

function Invoke-Build {
    $solution = Get-Solution
    $config = Get-Configuration "Debug"
    Write-Section "Build ($config)"

    dotnet build $solution --nologo -c $config @(Get-MetadataArgs)
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

function Invoke-Test {
    $solution = Get-Solution
    $config = Get-Configuration "Debug"
    Write-Section "Tests ($config)"

    dotnet test $solution --nologo -c $config @(Get-MetadataArgs)
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

function Invoke-VerifyFast {
    Invoke-Build

    $solution = Get-Solution
    $config = Get-Configuration "Debug"
    Write-Section "Fast Tests ($config)"

    dotnet test $solution --nologo -c $config --no-build @(Get-MetadataArgs)
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

function Invoke-Verify {
    Invoke-Doctor
    Invoke-Build
    Invoke-Test

    Write-Section "Git Status"
    if (Test-GitRepository) {
        git status --short
    } else {
        Write-Host "[SKIP] Not a git repository."
    }
}

function Invoke-Ci {
    Invoke-Doctor
    Invoke-Build
    Invoke-Test
    Invoke-Version
}

# Produces the NIGHTLY ring artifact required by the M0 exit criteria.
# The channel is stamped into build metadata so the artifact cannot misreport
# which ring it belongs to (docs/06_FORCED_UPDATE_PROTOCOL.md).
function Invoke-Nightly {
    $nightlyChannel = if ($Channel) { $Channel } else { "nightly" }
    $nightlyBuildId = if ($BuildId) { $BuildId } else { [DateTime]::UtcNow.ToString("yyyyMMdd.HHmm") }
    $config = Get-Configuration "Release"

    $stamp = @("-p:AgencyOSChannel=$nightlyChannel", "-p:AgencyOSBuildId=$nightlyBuildId")

    $solution = Get-Solution
    $output = Join-Path $root "artifacts/nightly"

    Write-Section "Nightly ($nightlyChannel / $nightlyBuildId / $config)"

    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $output | Out-Null

    Write-Section "Nightly: Build"
    dotnet build $solution --nologo -c $config @stamp
    if ($LASTEXITCODE -ne 0) { throw "Nightly build failed." }

    Write-Section "Nightly: Tests"
    dotnet test $solution --nologo -c $config --no-build @stamp
    if ($LASTEXITCODE -ne 0) { throw "Nightly tests failed." }

    Write-Section "Nightly: Publish API"
    dotnet publish (Join-Path $root "src/AgencyOS.Api/AgencyOS.Api.csproj") --nologo -c $config --no-build -o (Join-Path $output "api") @stamp
    if ($LASTEXITCODE -ne 0) { throw "Nightly API publish failed." }

    Write-Section "Nightly: Publish Windows client"
    dotnet publish (Join-Path $root "src/AgencyOS.Windows/AgencyOS.Windows.csproj") --nologo -c $config -r win-x64 --self-contained false -o (Join-Path $output "windows") @stamp
    if ($LASTEXITCODE -ne 0) { throw "Nightly Windows publish failed." }

    Write-Section "Nightly: Metadata"
    $metadataPath = Join-Path $output "build-metadata.json"
    dotnet build (Join-Path $root "src/AgencyOS.Contracts/AgencyOS.Contracts.csproj") --nologo -c $config -t:AgencyOSWriteBuildMetadata "-p:AgencyOSBuildMetadataPath=$metadataPath" @stamp
    if ($LASTEXITCODE -ne 0) { throw "Nightly metadata generation failed." }

    Write-Section "Nightly: Artifact"
    Get-Content $metadataPath | Write-Host
    Write-Host ""
    Write-Host "Artifact root: $output"
}

try {
    switch ($Target) {
        "doctor"      { Invoke-Doctor }
        "build"       { Invoke-Build }
        "test"        { Invoke-Test }
        "verify-fast" { Invoke-VerifyFast }
        "verify"      { Invoke-Verify }
        "version"     { Invoke-Version }
        "ci"          { Invoke-Ci }
        "nightly"     { Invoke-Nightly }
    }
}
finally {
    Pop-Location
}
