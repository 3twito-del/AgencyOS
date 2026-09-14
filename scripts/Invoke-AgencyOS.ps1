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
    [ValidateSet("doctor","build","test","test-unit","test-windows","test-reviewer","test-integration","verify","verify-fast","version","contract","formal","ci","nightly","release","release-gate")]
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

# Runs only the Windows suite. Needs a Windows agent and nothing else: no GPU,
# no NPU, no model file and no network. Every Windows-specific decision under
# test sits behind an abstraction with a deterministic fake, which is what keeps
# the local-AI work in M13 verifiable on hardware that cannot run a local model
# (ADR-0033).
function Invoke-TestWindows {
    $project = Join-Path $root "tests/AgencyOS.Tests.Windows/AgencyOS.Tests.Windows.csproj"
    $config = Get-Configuration "Debug"
    Write-Section "Windows Tests ($config)"

    if (-not $IsWindows) {
        Write-Host "[SKIP] Windows tests need a Windows agent."
        return
    }

    dotnet test $project --nologo -c $config @(Get-MetadataArgs)
    if ($LASTEXITCODE -ne 0) { throw "Windows tests failed." }
}

# Runs only the deterministic half of the review harness.
#
# The reviewer reads source, builds the surface map, checks reachability and
# validates the findings ledger. None of that needs a desktop, a database or a
# network, so it belongs in the same gate as the unit suite.
#
# What it deliberately does not run is the interactive pass: launching the WinUI
# client, driving it through UI Automation and capturing screenshots. That needs
# a real session, and a headless substitute would produce artefacts that look
# like visual QA and are not. Those stay LAB evidence.
#
# Audit 001 built this harness and Repair Wave 001 committed it, but CI only ever
# compiled it. A control system nothing executes is a control system nobody can
# rely on, which is the same defect class the harness was built to find.
function Invoke-TestReviewer {
    $project = Join-Path $root "tests/AgencyOS.Tests.Reviewer/AgencyOS.Tests.Reviewer.csproj"
    $config = Get-Configuration "Debug"
    Write-Section "Reviewer Tests ($config)"

    if (-not $IsWindows) {
        Write-Host "[SKIP] Reviewer tests target a Windows TFM and need a Windows agent."
        return
    }

    dotnet test $project --nologo -c $config @(Get-MetadataArgs)
    if ($LASTEXITCODE -ne 0) { throw "Reviewer tests failed." }
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

# Model-checks the formal specifications with TLC.
#
# AgencyOS uses TLA+ selectively, for protocols where a wrong answer is expensive
# and testing cannot enumerate the interleavings: the M3 offline write queue, and
# the M10 outbound send protocol whose failure mode is sending a client the same
# commercial email twice (ADR-0028).
#
# tla2tools.jar is fetched once and pinned by SHA-256. It is not committed - an
# 8 MB binary in a source repository is its own problem - and it is not fetched
# silently either: a checksum mismatch or an unreachable release fails loudly,
# because a formal check that quietly skips itself is worse than none.
function Invoke-Formal {
    Write-Section "Formal (TLC)"

    $java = Get-Command java -ErrorAction SilentlyContinue
    if (-not $java) {
        throw "java is required to run TLC. Install a JDK, or run the other targets."
    }

    $toolsDir = Join-Path $root "artifacts/tools"
    $jar = Join-Path $toolsDir "tla2tools.jar"

    # Pinned exactly, never "latest". A model checker that changed under us would
    # make a green run mean something different from one to the next.
    #
    # Pinned to v1.7.4, and the version choice is the point.
    #
    # v1.8.0 is not a fixed release. Upstream re-publishes that tag from current
    # master: it moved on 2026-09-04, 2026-09-08 and 2026-09-09, twice within a
    # week of M13. The pin caught every move, which is the mechanism working -
    # but a tag that rolls cannot be pinned, and each move cost a full archive
    # comparison before CI could go green again.
    #
    # v1.7.4 was published 2024-08-05 and has not been touched since. Its
    # manifest carries X-Git-Tag v1.7.4 and revision 5a47802b, so it is built
    # from the tag rather than from whatever master happened to be. That is the
    # property this pin needed all along (M14, ADR-0036).
    #
    # Verified before switching: all four specifications check green with
    # identical state counts to the v1.8.0 builds - OfflineWriteQueue 2853/1024,
    # OutboundSend 83/48, AiApproval 755/236, LocalInferenceLease 796/160.
    #
    # One difference was found, and it is why counts alone are no longer the
    # acceptance evidence: the 2026-09-04 build reported an OutboundSend search
    # depth of 17, while the 2026-09-09 build and v1.7.4 both report 14, with
    # the same 83/48 state space. No property went unchecked and no run reported
    # an error, but the checker's search behaviour did move between two builds
    # wearing the same version number, and M13's acceptance compared only state
    # counts and therefore missed it. The 09-04 artifact is no longer obtainable
    # upstream, so that discrepancy can no longer be examined - which is the
    # concrete cost of pinning a mutable tag.
    #
    # Depth is part of the recorded evidence from here on.
    $verified = @{
        "936a262061c914694dfd669a543be24573c45d5aa0ff20a8b96b23d01e050e88" =
            "v1.7.4, published 2024-08-05 and unchanged since; built from tag " +
            "v1.7.4 at revision 5a47802b; all four specs verified green at " +
            "2853/1024 d15, 83/48 d14, 755/236 d9, 796/160 d9"
    }

    $release = "https://github.com/tlaplus/tlaplus/releases/download/v1.7.4/tla2tools.jar"

    if (-not (Test-Path $jar)) {
        New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null

        Write-Host "Fetching tla2tools.jar (pinned) ..."
        try {
            Invoke-WebRequest -Uri $release -OutFile $jar -UseBasicParsing
        }
        catch {
            throw "Could not fetch tla2tools.jar from $release. $($_.Exception.Message)"
        }
    }

    $actual = (Get-FileHash -Path $jar -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not $verified.ContainsKey($actual)) {
        Remove-Item $jar -Force

        $known = ($verified.Keys | Sort-Object) -join ", "
        throw "tla2tools.jar checksum $actual is not one this repository has " +
            "verified. Known good: $known. Do not add a hash without comparing " +
            "the archive against a known build and recording what differed."
    }

    Write-Host "tla2tools.jar verified: $($verified[$actual])"

    $specs = @("OfflineWriteQueue", "OutboundSend", "AiApproval", "LocalInferenceLease")
    $specsDir = Join-Path $root "specs"
    $statesDir = Join-Path $specsDir "states"

    foreach ($spec in $specs) {
        Write-Host ""
        Write-Host "TLC: $spec"

        Push-Location $specsDir
        try {
            # -deadlock turns off deadlock detection. A terminal state is the
            # point of both these protocols, not a bug in them: a message that has
            # been sent, failed or cancelled has nowhere further to go, and TLC
            # would otherwise report each of them as a deadlock.
            java -XX:+UseParallelGC -cp $jar tlc2.TLC `
                -config "$spec.cfg" `
                -workers auto `
                -metadir $statesDir `
                -deadlock `
                -cleanup `
                "$spec.tla"

            if ($LASTEXITCODE -ne 0) {
                throw "TLC found a counterexample in $spec. See the trace above."
            }
        }
        finally {
            Pop-Location
        }
    }

    Write-Host ""
    Write-Host "[OK] TLC: $($specs -join ', ')"
}

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
        "/api/v1/organizations/{organizationId}/sync/changes",
        "/api/v1/organizations/{organizationId}/talent",
        "/api/v1/organizations/{organizationId}/talent/{personId}",
        "/api/v1/organizations/{organizationId}/talent/{personId}/overview",
        "/api/v1/organizations/{organizationId}/talent/{personId}/history",
        "/api/v1/organizations/{organizationId}/talent-profiles/{talentProfileId}",
        "/api/v1/organizations/{organizationId}/prospects",
        "/api/v1/organizations/{organizationId}/prospects/{prospectId}/convert",
        "/api/v1/organizations/{organizationId}/representations",
        "/api/v1/organizations/{organizationId}/representations/{representationId}",
        "/api/v1/organizations/{organizationId}/representations/{representationId}/transition",
        "/api/v1/organizations/{organizationId}/credits",
        "/api/v1/organizations/{organizationId}/materials",
        "/api/v1/organizations/{organizationId}/projects",
        "/api/v1/organizations/{organizationId}/projects/{projectId}",
        "/api/v1/organizations/{organizationId}/projects/{projectId}/status",
        "/api/v1/organizations/{organizationId}/projects/{projectId}/stage",
        "/api/v1/organizations/{organizationId}/projects/{projectId}/roles",
        "/api/v1/organizations/{organizationId}/projects/{projectId}/history",
        "/api/v1/organizations/{organizationId}/source-properties",
        "/api/v1/organizations/{organizationId}/packages",
        "/api/v1/organizations/{organizationId}/packages/{packageId}",
        "/api/v1/organizations/{organizationId}/project-command-center",
        "/api/v1/organizations/{organizationId}/opportunities",
        "/api/v1/organizations/{organizationId}/opportunities/{opportunityId}",
        "/api/v1/organizations/{organizationId}/opportunities/{opportunityId}/history",
        "/api/v1/organizations/{organizationId}/opportunities/{opportunityId}/status",
        "/api/v1/organizations/{organizationId}/opportunities/{opportunityId}/subjects",
        "/api/v1/organizations/{organizationId}/opportunities/{opportunityId}/targets",
        "/api/v1/organizations/{organizationId}/opportunity-targets/{targetId}",
        "/api/v1/organizations/{organizationId}/opportunity-targets/{targetId}/stage",
        "/api/v1/organizations/{organizationId}/opportunity-targets/{targetId}/responses",
        "/api/v1/organizations/{organizationId}/opportunity-targets/{targetId}/submissions",
        "/api/v1/organizations/{organizationId}/opportunity-targets/{targetId}/pitches",
        "/api/v1/organizations/{organizationId}/submissions",
        "/api/v1/organizations/{organizationId}/submissions/{submissionId}",
        "/api/v1/organizations/{organizationId}/pitches",
        "/api/v1/organizations/{organizationId}/pipeline",
        "/api/v1/organizations/{organizationId}/opportunity-command-center",
        "/api/v1/deal-terms",
        "/api/v1/organizations/{organizationId}/deals",
        "/api/v1/organizations/{organizationId}/deals/{dealId}",
        "/api/v1/organizations/{organizationId}/deals/{dealId}/history",
        "/api/v1/organizations/{organizationId}/deals/{dealId}/close",
        "/api/v1/organizations/{organizationId}/deals/{dealId}/reopen",
        "/api/v1/organizations/{organizationId}/deals/{dealId}/offers",
        "/api/v1/organizations/{organizationId}/deals/{dealId}/draft-offers",
        "/api/v1/organizations/{organizationId}/deals/{dealId}/current-offer",
        "/api/v1/organizations/{organizationId}/deals/{dealId}/comparison",
        "/api/v1/organizations/{organizationId}/offers",
        "/api/v1/organizations/{organizationId}/offers/{offerId}",
        "/api/v1/organizations/{organizationId}/offers/{offerId}/terms",
        "/api/v1/organizations/{organizationId}/offers/{offerId}/record",
        "/api/v1/organizations/{organizationId}/offers/{offerId}/answer",
        "/api/v1/organizations/{organizationId}/deal-pipeline",
        "/api/v1/organizations/{organizationId}/deal-command-center",
        "/api/v1/contract-terms/catalog",
        "/api/v1/organizations/{organizationId}/contracts",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/history",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/status",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/effective-date",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/parties",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/signatures",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/relationships",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/versions",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/rights-grants",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/options",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/obligations",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/notice-requirements",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/notices",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/tasks",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/versions/{versionId}/reconciliation",
        "/api/v1/organizations/{organizationId}/contract-versions/{versionId}",
        "/api/v1/organizations/{organizationId}/contract-versions/{versionId}/terms",
        "/api/v1/organizations/{organizationId}/contract-versions/{versionId}/record",
        "/api/v1/organizations/{organizationId}/rights-grants",
        "/api/v1/organizations/{organizationId}/rights-grants/{grantId}/end",
        "/api/v1/organizations/{organizationId}/contract-options",
        "/api/v1/organizations/{organizationId}/contract-options/{optionId}/resolve",
        "/api/v1/organizations/{organizationId}/obligations",
        "/api/v1/organizations/{organizationId}/obligations/{obligationId}/resolve",
        "/api/v1/organizations/{organizationId}/legal/deadlines",
        "/api/v1/organizations/{organizationId}/legal/command-center",
        "/api/v1/organizations/{organizationId}/monetary-obligations",
        "/api/v1/organizations/{organizationId}/monetary-obligations/{obligationId}",
        "/api/v1/organizations/{organizationId}/monetary-obligations/{obligationId}/quantify",
        "/api/v1/organizations/{organizationId}/monetary-obligations/{obligationId}/release",
        "/api/v1/organizations/{organizationId}/monetary-obligations/{obligationId}/receivables",
        "/api/v1/organizations/{organizationId}/monetary-obligations/{obligationId}/commission",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/monetary-obligations",
        "/api/v1/organizations/{organizationId}/contracts/{contractId}/invoices",
        "/api/v1/organizations/{organizationId}/receivables",
        "/api/v1/organizations/{organizationId}/receivables/{receivableId}",
        "/api/v1/organizations/{organizationId}/receivables/{receivableId}/write-off",
        "/api/v1/organizations/{organizationId}/receivables/{receivableId}/cancel",
        "/api/v1/organizations/{organizationId}/receivables/{receivableId}/adjustments",
        "/api/v1/organizations/{organizationId}/receivables/{receivableId}/reconciliation",
        "/api/v1/organizations/{organizationId}/invoices",
        "/api/v1/organizations/{organizationId}/invoices/{invoiceId}",
        "/api/v1/organizations/{organizationId}/invoices/{invoiceId}/issue",
        "/api/v1/organizations/{organizationId}/invoices/{invoiceId}/void",
        "/api/v1/organizations/{organizationId}/payments",
        "/api/v1/organizations/{organizationId}/payments/{paymentId}",
        "/api/v1/organizations/{organizationId}/payments/{paymentId}/allocations",
        "/api/v1/organizations/{organizationId}/payments/{paymentId}/allocations/reverse",
        "/api/v1/organizations/{organizationId}/payments/{paymentId}/reverse",
        "/api/v1/organizations/{organizationId}/commission-rules",
        "/api/v1/organizations/{organizationId}/commission-rules/{ruleId}/end",
        "/api/v1/organizations/{organizationId}/commissions",
        "/api/v1/organizations/{organizationId}/commissions/{commissionId}",
        "/api/v1/organizations/{organizationId}/commissions/{commissionId}/adjustments",
        "/api/v1/organizations/{organizationId}/ledger/accounts",
        "/api/v1/organizations/{organizationId}/ledger/balances",
        "/api/v1/organizations/{organizationId}/ledger/entries",
        "/api/v1/organizations/{organizationId}/ledger/entries/{entryId}",
        "/api/v1/organizations/{organizationId}/ledger/entries/{entryId}/reverse",
        "/api/v1/organizations/{organizationId}/finance/history",
        "/api/v1/organizations/{organizationId}/finance/command-center",
        "/api/v1/organizations/{organizationId}/documents",
        "/api/v1/organizations/{organizationId}/documents/{documentId}",
        "/api/v1/organizations/{organizationId}/documents/{documentId}/versions",
        "/api/v1/organizations/{organizationId}/documents/{documentId}/update",
        "/api/v1/organizations/{organizationId}/documents/{documentId}/links",
        "/api/v1/organizations/{organizationId}/documents/{documentId}/links/{linkId}",
        "/api/v1/organizations/{organizationId}/documents/{documentId}/archive",
        "/api/v1/organizations/{organizationId}/documents/{documentId}/restore",
        "/api/v1/organizations/{organizationId}/document-versions/{versionId}/content",
        "/api/v1/organizations/{organizationId}/communication-providers",
        "/api/v1/organizations/{organizationId}/communication-accounts",
        "/api/v1/organizations/{organizationId}/communication-accounts/{accountId}/disconnect",
        "/api/v1/organizations/{organizationId}/communication-accounts/{accountId}/visibility",
        "/api/v1/organizations/{organizationId}/messages",
        "/api/v1/organizations/{organizationId}/messages/{messageId}",
        "/api/v1/organizations/{organizationId}/messages/{messageId}/links",
        "/api/v1/organizations/{organizationId}/messages/{messageId}/links/{linkId}",
        "/api/v1/organizations/{organizationId}/messages/{messageId}/participants/{participantId}",
        "/api/v1/organizations/{organizationId}/participant-suggestions",
        "/api/v1/organizations/{organizationId}/message-attachments/{attachmentId}/ingest",
        "/api/v1/organizations/{organizationId}/outbound-messages",
        "/api/v1/organizations/{organizationId}/outbound-messages/{dispatchId}",
        "/api/v1/organizations/{organizationId}/outbound-messages/{dispatchId}/queue",
        "/api/v1/organizations/{organizationId}/outbound-messages/{dispatchId}/cancel",
        "/api/v1/organizations/{organizationId}/communications/history",
        "/api/v1/organizations/{organizationId}/communications/command-center",
        "/api/v1/organizations/{organizationId}/intelligence/sources",
        "/api/v1/organizations/{organizationId}/intelligence/sources/{sourceId}",
        "/api/v1/organizations/{organizationId}/intelligence/sources/{sourceId}/reliability",
        "/api/v1/organizations/{organizationId}/intelligence/signals",
        "/api/v1/organizations/{organizationId}/intelligence/signals/{signalId}",
        "/api/v1/organizations/{organizationId}/intelligence/signals/{signalId}/verification",
        "/api/v1/organizations/{organizationId}/intelligence/signals/{signalId}/evidence",
        "/api/v1/organizations/{organizationId}/intelligence/signals/{signalId}/evidence/{evidenceId}",
        "/api/v1/organizations/{organizationId}/intelligence/signals/{signalId}/subjects",
        "/api/v1/organizations/{organizationId}/intelligence/signals/{signalId}/subjects/{subjectRowId}",
        "/api/v1/organizations/{organizationId}/intelligence/theses",
        "/api/v1/organizations/{organizationId}/intelligence/theses/{thesisId}",
        "/api/v1/organizations/{organizationId}/intelligence/theses/{thesisId}/activate",
        "/api/v1/organizations/{organizationId}/intelligence/theses/{thesisId}/revisions",
        "/api/v1/organizations/{organizationId}/intelligence/theses/{thesisId}/close",
        "/api/v1/organizations/{organizationId}/intelligence/theses/{thesisId}/evidence",
        "/api/v1/organizations/{organizationId}/intelligence/theses/{thesisId}/evidence/{evidenceId}",
        "/api/v1/organizations/{organizationId}/intelligence/theses/{thesisId}/subjects",
        "/api/v1/organizations/{organizationId}/intelligence/predictions",
        "/api/v1/organizations/{organizationId}/intelligence/predictions/calibration",
        "/api/v1/organizations/{organizationId}/intelligence/predictions/{predictionId}",
        "/api/v1/organizations/{organizationId}/intelligence/predictions/{predictionId}/revisions",
        "/api/v1/organizations/{organizationId}/intelligence/predictions/{predictionId}/resolve",
        "/api/v1/organizations/{organizationId}/intelligence/predictions/{predictionId}/cancel",
        "/api/v1/organizations/{organizationId}/intelligence/predictions/{predictionId}/subjects",
        "/api/v1/organizations/{organizationId}/intelligence/watchlists",
        "/api/v1/organizations/{organizationId}/intelligence/watchlists/{watchlistId}",
        "/api/v1/organizations/{organizationId}/intelligence/watchlists/{watchlistId}/activity",
        "/api/v1/organizations/{organizationId}/intelligence/watchlists/{watchlistId}/entries",
        "/api/v1/organizations/{organizationId}/intelligence/watchlists/{watchlistId}/entries/{entryId}",
        "/api/v1/organizations/{organizationId}/intelligence/watchlists/{watchlistId}/review",
        "/api/v1/organizations/{organizationId}/intelligence/watchlists/{watchlistId}/archive",
        "/api/v1/organizations/{organizationId}/intelligence/radar",
        "/api/v1/organizations/{organizationId}/intelligence/radar/{entryId}",
        "/api/v1/organizations/{organizationId}/intelligence/radar/{entryId}/status",
        "/api/v1/organizations/{organizationId}/intelligence/radar/{entryId}/review",
        "/api/v1/organizations/{organizationId}/intelligence/radar/{entryId}/dismiss",
        "/api/v1/organizations/{organizationId}/intelligence/radar/{entryId}/convert",
        "/api/v1/organizations/{organizationId}/intelligence/research-cases",
        "/api/v1/organizations/{organizationId}/intelligence/research-cases/{researchCaseId}",
        "/api/v1/organizations/{organizationId}/intelligence/research-cases/{researchCaseId}/status",
        "/api/v1/organizations/{organizationId}/intelligence/research-cases/{researchCaseId}/links",
        "/api/v1/organizations/{organizationId}/intelligence/research-cases/{researchCaseId}/links/{linkId}",
        "/api/v1/organizations/{organizationId}/intelligence/research-cases/{researchCaseId}/subjects",
        "/api/v1/organizations/{organizationId}/intelligence/relationships/{kind}/{subjectId}",
        "/api/v1/organizations/{organizationId}/intelligence/command-center",
        "/api/v1/organizations/{organizationId}/ai/runs",
        "/api/v1/organizations/{organizationId}/ai/runs/{runId}",
        "/api/v1/organizations/{organizationId}/ai/runs/{runId}/cancel",
        "/api/v1/organizations/{organizationId}/ai/approvals",
        "/api/v1/organizations/{organizationId}/ai/approvals/{approvalId}",
        "/api/v1/organizations/{organizationId}/ai/approvals/{approvalId}/decision",
        "/api/v1/organizations/{organizationId}/ai/tool-requests/{toolRequestId}/execute",
        "/api/v1/organizations/{organizationId}/ai/agents",
        "/api/v1/organizations/{organizationId}/ai/agents/{kind}/tools",
        "/api/v1/organizations/{organizationId}/ai/models",
        "/api/v1/organizations/{organizationId}/ai/policies",
        "/api/v1/organizations/{organizationId}/ai/policies/{providerKey}",
        "/api/v1/organizations/{organizationId}/ai/runs/{runId}/local-lease",
        "/api/v1/organizations/{organizationId}/ai/runs/{runId}/local-result",
        "/api/v1/organizations/{organizationId}/ai/execution-targets"
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
    Invoke-Formal

    Write-Section "Git Status"
    if (Test-GitRepository) {
        git status --short
    } else {
        Write-Host "[SKIP] Not a git repository."
    }
}

function Invoke-ReleaseGate {
    # One command that runs every gate a promotion depends on, and reports each
    # one by name. It orchestrates the existing targets rather than duplicating
    # them, so there is no second definition of "green" to drift (M15 SS59).
    #
    # There is deliberately no switch to skip a gate. A gate that can be turned
    # off is a gate somebody will turn off on the night it matters. What the
    # command does instead is tell the truth about what could not run: any gate
    # that was unavailable makes the whole result PARTIAL, and PARTIAL is not a
    # promotion.
    Write-Section "Release Gate"

    $results = [System.Collections.Generic.List[object]]::new()

    function Invoke-Gate {
        param(
            [Parameter(Mandatory = $true)][string] $Name,
            [Parameter(Mandatory = $true)][scriptblock] $Action,
            [scriptblock] $Available
        )

        if ($Available -and -not (& $Available)) {
            Write-Host ""
            Write-Host "[UNAVAILABLE] $Name"
            $results.Add([pscustomobject]@{ Name = $Name; Status = "UNAVAILABLE" })
            return
        }

        try {
            & $Action
            $results.Add([pscustomobject]@{ Name = $Name; Status = "PASS" })
        }
        catch {
            $results.Add([pscustomobject]@{ Name = $Name; Status = "FAIL"; Detail = $_.Exception.Message })
        }
    }

    Invoke-Gate -Name "Build"              -Action { Invoke-Build }
    Invoke-Gate -Name "Unit tests"         -Action { Invoke-TestUnit }
    Invoke-Gate -Name "Windows tests"      -Action { Invoke-TestWindows } -Available { $IsWindows }
    Invoke-Gate -Name "OpenAPI contract"   -Action { Invoke-Contract }
    Invoke-Gate -Name "Formal (TLC)"       -Action { Invoke-Formal }
    Invoke-Gate -Name "Release artifact"   -Action { Invoke-Release }
    Invoke-Gate -Name "Release manifest"   -Action {
        & (Join-Path $PSScriptRoot "Test-ReleaseManifest.ps1") -ReleasePath (Join-Path $root "artifacts/release")
        if ($LASTEXITCODE -ne 0) { throw "Release manifest verification failed." }
    }

    # The database gates, including the backup and restore drill, live in the
    # integration suite. They need a real PostgreSQL 18.6; where none is reachable
    # this reports UNAVAILABLE rather than passing quietly.
    Invoke-Gate -Name "Integration + restore drill" -Action { Invoke-TestIntegration } -Available {
        [bool]$env:AGENCYOS_TEST_POSTGRES -or (Test-DockerRunning)
    }

    Write-Section "Release Gate Summary"

    foreach ($result in $results) {
        $line = "{0,-32} {1}" -f $result.Name, $result.Status
        Write-Host $line
        if ($result.Detail) { Write-Host "    $($result.Detail)" }
    }

    $failed = @($results | Where-Object { $_.Status -eq "FAIL" })
    $unavailable = @($results | Where-Object { $_.Status -eq "UNAVAILABLE" })

    Write-Host ""

    if ($failed.Count -gt 0) {
        throw "Release gate FAILED: $($failed.Count) of $($results.Count) gates failed."
    }

    if ($unavailable.Count -gt 0) {
        Write-Host "[PARTIAL] $($results.Count - $unavailable.Count) of $($results.Count) gates ran and passed."
        Write-Host "          $($unavailable.Count) could not run in this environment:"
        foreach ($gate in $unavailable) { Write-Host "            - $($gate.Name)" }
        Write-Host ""
        Write-Host "          PARTIAL is not a promotion. The missing gates must run somewhere"
        Write-Host "          before this build is promoted - authoritative CI is where."
        exit 4
    }

    Write-Host "[OK] Release gate: all $($results.Count) gates passed."
}

function Invoke-Ci {
    Invoke-Doctor
    Invoke-Build
    Invoke-Test
    Invoke-Contract
    Invoke-Formal
    Invoke-Version
}

# Produces the NIGHTLY ring artifact required by the M0 exit criteria.
# The channel is stamped into build metadata so the artifact cannot misreport
# which ring it belongs to (docs/06_FORCED_UPDATE_PROTOCOL.md).
function Invoke-Release {
    # Produces the artifact set a deployment actually needs, and binds it together
    # so that what is installed can be traced back to what was built. Filenames are
    # not evidence: the manifest carries a SHA-256 for every artifact, the commit,
    # the CI run and the schema the database must be at (M15 SS25/SS60, ADR-0039).
    $releaseChannel = if ($Channel) { $Channel } else { "alpha" }
    $releaseBuildId = if ($BuildId) { $BuildId } else { [DateTime]::UtcNow.ToString("yyyyMMdd.HHmm") }
    $config = Get-Configuration "Release"

    $stamp = @("-p:AgencyOSChannel=$releaseChannel", "-p:AgencyOSBuildId=$releaseBuildId")

    $solution = Get-Solution
    $output = Join-Path $root "artifacts/release"

    Write-Section "Release ($releaseChannel / $releaseBuildId / $config)"

    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $output | Out-Null

    Write-Section "Release: Build"
    dotnet build $solution --nologo -c $config @stamp
    if ($LASTEXITCODE -ne 0) { throw "Release build failed." }

    Write-Section "Release: Publish API"
    dotnet publish (Join-Path $root "src/AgencyOS.Api/AgencyOS.Api.csproj") --nologo -c $config --no-build -o (Join-Path $output "api") @stamp
    if ($LASTEXITCODE -ne 0) { throw "Release API publish failed." }

    Write-Section "Release: Publish Windows client"
    dotnet publish (Join-Path $root "src/AgencyOS.Windows/AgencyOS.Windows.csproj") --nologo -c $config -r win-x64 --self-contained false -o (Join-Path $output "windows") @stamp
    if ($LASTEXITCODE -ne 0) { throw "Release Windows publish failed." }

    # A production host has the runtime, not the SDK, and certainly not the source
    # tree. Without this an operator cannot migrate a database at all - which was
    # true until M15 and is the kind of gap nobody notices until an upgrade night.
    Write-Section "Release: Migration script"
    $migrationScript = Join-Path $output "migrate.sql"

    # Infrastructure is both project and startup project: it owns the
    # DesignTimeDbContextFactory, and the API deliberately carries no design-time
    # dependency at all.
    dotnet ef migrations script --idempotent `
        --project (Join-Path $root "src/AgencyOS.Infrastructure/AgencyOS.Infrastructure.csproj") `
        --startup-project (Join-Path $root "src/AgencyOS.Infrastructure/AgencyOS.Infrastructure.csproj") `
        --configuration $config `
        --output $migrationScript
    if ($LASTEXITCODE -ne 0) { throw "Migration script generation failed." }

    Write-Section "Release: SBOM"
    $sbomDirectory = Join-Path $output "sbom"
    New-Item -ItemType Directory -Force -Path $sbomDirectory | Out-Null
    dotnet CycloneDX $solution --output $sbomDirectory --filename sbom.json --json --exclude-dev
    if ($LASTEXITCODE -ne 0) { throw "SBOM generation failed." }

    Write-Section "Release: Manifest"

    # The last migration in the script is the schema a deployment of this build
    # expects, and it is read from the generated script rather than typed.
    $expectedSchema = ""
    $migrationIds = Select-String -Path $migrationScript -Pattern "INSERT INTO ""__EFMigrationsHistory""" -Context 0, 1
    if ($migrationIds) {
        $last = ($migrationIds | Select-Object -Last 1).Context.PostContext -join " "
        if ($last -match "'([0-9]{14}_[A-Za-z0-9]+)'") { $expectedSchema = $Matches[1] }
    }

    $artifacts = @()
    foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -File) {
        $artifacts += [ordered]@{
            path   = ([System.IO.Path]::GetRelativePath($output, $file.FullName)) -replace "\\", "/"
            bytes  = $file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }

    $manifest = [ordered]@{
        formatVersion      = 1
        version            = (Get-BuildProperty "VersionPrefix")
        channel            = $releaseChannel
        buildId            = $releaseBuildId
        gitCommit          = (git -C $root rev-parse HEAD).Trim()
        gitBranch          = (git -C $root rev-parse --abbrev-ref HEAD).Trim()
        apiContractVersion = [int](Get-BuildProperty "AgencyOSApiContractVersion")
        expectedSchema     = $expectedSchema
        ciRun              = $env:GITHUB_RUN_ID
        signing            = "unsigned"
        createdAt          = ([DateTimeOffset]::UtcNow).ToString("o")
        artifacts          = $artifacts
    }

    $manifestPath = Join-Path $output "release-manifest.json"
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8

    Write-Host ""
    Write-Host "[OK] Release: $($artifacts.Count) artifacts, contract $($manifest.apiContractVersion), schema $expectedSchema"
    Write-Host "     $manifestPath"
}

function Get-BuildProperty {
    param([Parameter(Mandatory = $true)][string] $Name)

    $value = dotnet msbuild (Join-Path $root "src/AgencyOS.Contracts/AgencyOS.Contracts.csproj") `
        -getProperty:$Name -nologo 2>$null

    return ($value | Out-String).Trim()
}

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
        "test-windows"     { Invoke-TestWindows }
        "test-reviewer"    { Invoke-TestReviewer }
        "test-integration" { Invoke-TestIntegration }
        "contract"         { Invoke-Contract }
        "formal"           { Invoke-Formal }
        "ci"          { Invoke-Ci }
        "nightly"     { Invoke-Nightly }
        "release"     { Invoke-Release }
        "release-gate" { Invoke-ReleaseGate }
    }
}
finally {
    Pop-Location
}
