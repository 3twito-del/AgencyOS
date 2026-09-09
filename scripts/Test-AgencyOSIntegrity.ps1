<#
.SYNOPSIS
    Reconciles the canonical database against the document bytes on disk.

.DESCRIPTION
    AgencyOS keeps document metadata in PostgreSQL and document content in a
    content-addressed store outside it. Backup and restore handle both, but nothing
    else checks that they still agree - and the failure is silent by nature: a row
    whose bytes are gone looks exactly like a row whose bytes are fine, until
    somebody opens the contract.

    This reports four kinds of disagreement:

      missing   a blob_objects row whose file is not on disk
      orphan    a file on disk that no row references
      mismatch  a file whose contents no longer hash to what the row recorded
      foreign   a storage key that does not belong to the organization that owns it

    It reports and does not repair. Two of these have innocent explanations - an
    orphan is the ordinary result of a backup taken while an upload was in flight,
    and a missing file may be a restore still in progress - and a tool that deleted
    on sight would turn a diagnostic into an incident (M15 §10).

    Exit codes:
      0  everything agrees
      1  the check could not run
      3  disagreements were found and reported

.PARAMETER ConnectionString
    Npgsql connection string for the canonical database.

.PARAMETER BlobRoot
    The directory the blob store owns.

.PARAMETER SkipHashVerification
    Reports presence and ownership without re-reading every file. Useful on a large
    store where a full verification is an overnight job; the default is to verify,
    because presence without content is the reassurance that misleads.

.EXAMPLE
    ./scripts/Test-AgencyOSIntegrity.ps1 -ConnectionString $env:AGENCYOS_CONNECTION -BlobRoot D:\AgencyOS\blobs
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ConnectionString,
    [Parameter(Mandatory = $true)][string] $BlobRoot,
    [switch] $SkipHashVerification
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "AgencyOS.Backup.Common.ps1")

Write-BackupEvent -Event "integrity.start" -Detail "root=$BlobRoot"

try {
    $settings = ConvertFrom-ConnectionString -ConnectionString $ConnectionString

    if (-not (Test-Path $BlobRoot)) {
        throw "Blob root '$BlobRoot' does not exist. Nothing can be reconciled against it."
    }

    # organization_id, storage_key, content_hash, byte_length for every recorded object.
    $query = @'
SELECT organization_id::text, storage_key, content_hash, byte_length
FROM blob_objects
ORDER BY storage_key
'@

    $rows = Invoke-PostgresTool -Tool "psql" -Password $settings.Password -Arguments @(
        "--host=$($settings.Host)"
        "--port=$($settings.Port)"
        "--username=$($settings.Username)"
        "--dbname=$($settings.Database)"
        "--tuples-only"
        "--no-align"
        "--field-separator=|"
        "--command=$query"
    )

    $recorded = @{}
    $missing = @()
    $mismatch = @()
    $foreign = @()

    foreach ($line in ($rows | Out-String) -split "`n") {
        $line = $line.Trim()
        if (-not $line) { continue }

        $parts = $line -split '\|'
        if ($parts.Count -lt 4) { continue }

        $organization = $parts[0]
        $key = $parts[1]
        $hash = $parts[2]
        $length = [int64]$parts[3]

        $recorded[$key] = $true

        # The organization prefix is what keeps deduplication inside a tenant. A key
        # that does not carry its owner's identity is either corruption or a bug
        # that would let one tenant's bytes answer another's request.
        $expectedPrefix = ($organization -replace '-', '') + '/'

        if (-not $key.StartsWith($expectedPrefix)) {
            $foreign += "key '$key' is recorded against organization $organization"
            continue
        }

        $path = Join-Path $BlobRoot ($key -replace '/', [IO.Path]::DirectorySeparatorChar)

        if (-not (Test-Path -LiteralPath $path)) {
            $missing += "$key (organization $organization)"
            continue
        }

        if ($SkipHashVerification) { continue }

        $actualLength = (Get-Item -LiteralPath $path).Length

        if ($actualLength -ne $length) {
            $mismatch += "$key recorded $length bytes, found $actualLength"
            continue
        }

        $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()

        if ($actualHash -ne $hash) {
            $mismatch += "$key recorded $hash, found $actualHash"
        }
    }

    # The other direction. Staging is excluded: those are partial writes that are
    # not yet anybody's document, and reporting them would train an operator to
    # ignore this list.
    $orphans = @()

    Get-ChildItem -LiteralPath $BlobRoot -Recurse -File -Force |
        Where-Object { $_.FullName -notmatch '[\\/]\.staging[\\/]' } |
        ForEach-Object {
            $relative = [System.IO.Path]::GetRelativePath($BlobRoot, $_.FullName) -replace '\\', '/'

            if (-not $recorded.ContainsKey($relative)) {
                $script:orphans += $relative
            }
        }

    # ---------------------------------------------------------------- report
    $total = $recorded.Count
    $anomalies = $missing.Count + $mismatch.Count + $foreign.Count + $orphans.Count

    Write-BackupEvent -Event "integrity.summary" -Detail (
        "recorded=$total missing=$($missing.Count) mismatch=$($mismatch.Count) " +
        "foreign=$($foreign.Count) orphan=$($orphans.Count)")

    foreach ($item in $missing) { Write-BackupEvent -Event "integrity.missing" -Detail $item }
    foreach ($item in $mismatch) { Write-BackupEvent -Event "integrity.mismatch" -Detail $item }
    foreach ($item in $foreign) { Write-BackupEvent -Event "integrity.foreign" -Detail $item }
    foreach ($item in $orphans) { Write-BackupEvent -Event "integrity.orphan" -Detail $item }

    if ($anomalies -gt 0) {
        Write-Host ""
        Write-Host "Reported only. Nothing has been changed."
        Write-Host "An orphan is often a backup taken mid-upload; a missing file may be a restore still running."
        Write-Host "Investigate before removing anything."

        Write-BackupEvent -Event "integrity.anomalies" -Detail "count=$anomalies"

        exit 3
    }

    Write-BackupEvent -Event "integrity.ok" -Detail "objects=$total"

    exit 0
}
catch {
    Write-BackupEvent -Event "integrity.failure" -Detail $_.Exception.Message
    throw
}
