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
    [ValidateSet("doctor","build","test","test-unit","test-integration","verify","verify-fast","version","contract","ci","nightly")]
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

    # A non-zero exit here is an answer, not an error. PowerShell 7.4+ would
    # otherwise turn it into a terminating error under ErrorActionPreference Stop.
    $PSNativeCommandUseErrorActionPreference = $false
    git rev-parse --is-inside-work-tree *> $null
    return ($LASTEXITCODE -eq 0)
}

# Integration tests require a real PostgreSQL server
# (docs/11_TESTING_AND_FORMAL_METHODS.md). They fail rather than skip when none
# is reachable, so the doctor reports the situation before a run does.
function Test-DockerRunning {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { return $false }

    $PSNativeCommandUseErrorActionPreference = $false
    docker info *> $null
    return ($LASTEXITCODE -eq 0)
}

function Write-PostgresStatus {
    if ($env:AGENCYOS_TEST_POSTGRES) {
        Write-Host "[OK] AGENCYOS_TEST_POSTGRES is set; integration tests will use it"
        return
    }

    if (Test-DockerRunning) {
        Write-Host "[OK] Docker is running; integration tests will use Testcontainers (postgres:18.6, ALPHA baseline)"
        return
    }

    Write-Host "[WARN] No PostgreSQL test target. Integration tests will FAIL, not skip."
    Write-Host "       Set AGENCYOS_TEST_POSTGRES to a server the suite may create databases on,"
    Write-Host "       or start Docker, which runs the suite against postgres:18.6 - the ALPHA"
    Write-Host "       baseline in config/version-policy.yaml, and the only evidence that counts"
    Write-Host "       for ALPHA promotion."
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

    if (Test-Path ".config/dotnet-tools.json")  { Write-Host "[OK] .config/dotnet-tools.json (pinned dotnet-ef)" }

    if (Test-GitRepository) {
        Write-Host "[OK] git repository initialized"
    } else {
        Write-Host "[WARN] Not a git repository. Build metadata will report an unknown commit."
    }

    Write-PostgresStatus
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

# Runs only the unit suite. Needs no database, so CI can run it on any agent.
function Invoke-TestUnit {
    $project = Join-Path $root "tests/AgencyOS.Tests.Unit/AgencyOS.Tests.Unit.csproj"
    $config = Get-Configuration "Debug"
    Write-Section "Unit Tests ($config)"

    dotnet test $project --nologo -c $config @(Get-MetadataArgs)
    if ($LASTEXITCODE -ne 0) { throw "Unit tests failed." }
}

# Runs only the integration suite. Builds just this project's dependency graph,
# which excludes the WinUI client, so it runs on a non-Windows agent too.
function Invoke-TestIntegration {
    $project = Join-Path $root "tests/AgencyOS.Tests.Integration/AgencyOS.Tests.Integration.csproj"
    $config = Get-Configuration "Debug"
    Write-Section "Integration Tests ($config)"

    dotnet test $project --nologo -c $config @(Get-MetadataArgs)
    if ($LASTEXITCODE -ne 0) { throw "Integration tests failed." }
}

# Generates the machine-readable API contract and verifies it.
#
# Generation constructs the application host. The bootstrap endpoint is mapped
# only when a token is configured, so a throwaway token is supplied for the
# duration of generation to keep the published contract complete. It is restored
# afterwards and never persisted.
function Invoke-Contract {
    Write-Section "API Contract (OpenAPI)"

    $api = Join-Path $root "src/AgencyOS.Api/AgencyOS.Api.csproj"
    $outputDirectory = Join-Path $root "artifacts/openapi"
    $document = Join-Path $outputDirectory "AgencyOS.Api.json"

    if (Test-Path $outputDirectory) { Remove-Item $outputDirectory -Recurse -Force }

    $previousToken = $env:AGENCYOS_BOOTSTRAP_TOKEN
    $env:AGENCYOS_BOOTSTRAP_TOKEN = "contract-generation-only-" + [Guid]::NewGuid().ToString("N")

    try {
        dotnet build $api --nologo -p:OpenApiGenerateDocumentsOnBuild=true
        if ($LASTEXITCODE -ne 0) { throw "OpenAPI document generation failed." }
    }
    finally {
        $env:AGENCYOS_BOOTSTRAP_TOKEN = $previousToken
    }

    if (-not (Test-Path $document)) {
        throw "No OpenAPI document was produced at $document."
    }

    $contract = Get-Content $document -Raw | ConvertFrom-Json

    if (-not $contract.openapi.StartsWith("3.1")) {
        throw "Expected an OpenAPI 3.1 document; the generator emitted $($contract.openapi)."
    }

    # The contract is only useful if it actually describes the surface. A silently
    # truncated document would still be valid OpenAPI.
    $required = @(
        "/version",
        "/api/v1/release/handshake",
        "/api/v1/system/status",
        "/api/v1/system/bootstrap",
        "/api/v1/organizations",
        "/api/v1/organizations/{id}",
        "/api/v1/organizations/{id}/memberships",
        "/api/v1/audit",
        "/api/v1/organizations/{organizationId}/people",
        "/api/v1/organizations/{organizationId}/companies",
        "/api/v1/organizations/{organizationId}/tasks",
        "/api/v1/organizations/{organizationId}/search",
        "/api/v1/organizations/{organizationId}/saved-views",
        "/api/v1/organizations/{organizationId}/saved-views/{savedViewId}/results",
        "/api/v1/organizations/{organizationId}/sync/changes"
    )

    $paths = @($contract.paths.PSObject.Properties.Name)

    foreach ($path in $required) {
        if ($paths -notcontains $path) {
            throw "The OpenAPI document is missing the required path '$path'."
        }
    }

    Write-Host ""
    Write-Host "[OK] OpenAPI $($contract.openapi); $($paths.Count) paths; $($contract.components.schemas.PSObject.Properties.Name.Count) schemas"
    Write-Host "     $document"
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
    Invoke-Contract

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
    Invoke-Contract
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

    # Unit tests only. The database suite gates this build in its own CI job
    # (.github/workflows/nightly.yml), because PostgreSQL is provisioned by a
    # Linux service container that a Windows runner cannot host. Running it here
    # as well would either duplicate the gate or, worse, silently skip when no
    # database happened to be reachable.
    Write-Section "Nightly: Unit Tests"
    $unitProject = Join-Path $root "tests/AgencyOS.Tests.Unit/AgencyOS.Tests.Unit.csproj"
    dotnet test $unitProject --nologo -c $config --no-build @stamp
    if ($LASTEXITCODE -ne 0) { throw "Nightly unit tests failed." }

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
        "test-unit"        { Invoke-TestUnit }
        "test-integration" { Invoke-TestIntegration }
        "contract"         { Invoke-Contract }
        "ci"          { Invoke-Ci }
        "nightly"     { Invoke-Nightly }
    }
}
finally {
    Pop-Location
}
