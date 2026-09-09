<#
.SYNOPSIS
    Takes a verifiable backup of AgencyOS: the canonical database and the document bytes.

.DESCRIPTION
    PostgreSQL is canonical business truth, but it is not all of the truth. M10 put
    document content in a content-addressed blob store outside the relational rows,
    so a database backup alone restores every document's metadata, hash and version
    history and none of its content - and the system would look healthy while every
    file was gone.

    This backs up both, in that order, and writes a manifest binding them together.

    Order matters and is deliberate. The database is dumped first and the blobs
    copied second, because blobs are immutable and content-addressed: the only skew
    this order can produce is a blob newer than the dump, which reconciliation
    reports as an orphan. The reverse order could produce metadata referring to
    bytes that were never copied, which is data loss wearing the appearance of
    health.

    A backup is not proven by an exit code. Restore-AgencyOS.ps1 verifies the
    manifest before it touches anything, and the M15 restore drill proves the whole
    round trip against postgres:18.6 in CI.

.PARAMETER ConnectionString
    Npgsql connection string for the database to back up. The password is passed to
    pg_dump through PGPASSWORD rather than the command line, so it does not reach
    the process table or a shell history.

.PARAMETER BlobRoot
    The directory the blob store owns. Omit only if this deployment has none.

.PARAMETER Destination
    Directory to write the backup into. Created if absent; must be empty otherwise.

.EXAMPLE
    ./scripts/Backup-AgencyOS.ps1 -ConnectionString $env:AGENCYOS_CONNECTION `
        -BlobRoot D:\AgencyOS\blobs -Destination D:\Backups\2026-09-09
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ConnectionString,
    [string] $BlobRoot,
    [Parameter(Mandatory = $true)][string] $Destination
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "AgencyOS.Backup.Common.ps1")

$started = [DateTimeOffset]::UtcNow
Write-BackupEvent -Event "backup.start" -Detail "destination=$Destination"

try {
    $settings = ConvertFrom-ConnectionString -ConnectionString $ConnectionString

    # An empty directory, so a backup can never be half of one backup and half of
    # another. Refusing is safer than merging.
    if (Test-Path $Destination) {
        if (@(Get-ChildItem -LiteralPath $Destination -Force).Count -gt 0) {
            throw "Destination '$Destination' is not empty. Choose a new directory: merging two backups produces neither."
        }
    }
    else {
        New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    }

    Assert-PostgresTooling -Tool "pg_dump"

    # ---------------------------------------------------------------- database
    $dumpPath = Join-Path $Destination "database.dump"

    Write-BackupEvent -Event "backup.database.start" -Detail "database=$($settings.Database)"

    $dumpStarted = [DateTimeOffset]::UtcNow

    # Custom format: compressed, and restorable selectively by pg_restore.
    $arguments = @(
        "--host=$($settings.Host)"
        "--port=$($settings.Port)"
        "--username=$($settings.Username)"
        "--dbname=$($settings.Database)"
        "--format=custom"
        "--no-owner"
        "--no-privileges"
        "--file=$dumpPath"
    )

    Invoke-PostgresTool -Tool "pg_dump" -Arguments $arguments -Password $settings.Password

    $dumpDuration = [DateTimeOffset]::UtcNow - $dumpStarted
    $dumpHash = (Get-FileHash -LiteralPath $dumpPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $dumpBytes = (Get-Item -LiteralPath $dumpPath).Length

    Write-BackupEvent -Event "backup.database.done" `
        -Detail "bytes=$dumpBytes sha256=$dumpHash seconds=$([math]::Round($dumpDuration.TotalSeconds, 2))"

    # ------------------------------------------------------------------ blobs
    $blobCount = 0
    $blobBytes = 0L
    $blobsCopied = $false

    if ($BlobRoot -and (Test-Path $BlobRoot)) {
        Write-BackupEvent -Event "backup.blobs.start" -Detail "root=$BlobRoot"

        $blobStarted = [DateTimeOffset]::UtcNow
        $blobDestination = Join-Path $Destination "blobs"

        New-Item -ItemType Directory -Force -Path $blobDestination | Out-Null

        # Staging holds partial writes that are not yet anybody's document. Copying
        # them would restore bytes no row references.
        Get-ChildItem -LiteralPath $BlobRoot -Recurse -File -Force |
            Where-Object { $_.FullName -notmatch '[\\/]\.staging[\\/]' } |
            ForEach-Object {
                $relative = [System.IO.Path]::GetRelativePath($BlobRoot, $_.FullName)
                $target = Join-Path $blobDestination $relative
                $targetDirectory = Split-Path -Parent $target

                if (-not (Test-Path $targetDirectory)) {
                    New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
                }

                Copy-Item -LiteralPath $_.FullName -Destination $target -Force

                $script:blobCount++
                $script:blobBytes += $_.Length
            }

        $blobDuration = [DateTimeOffset]::UtcNow - $blobStarted
        $blobsCopied = $true

        Write-BackupEvent -Event "backup.blobs.done" `
            -Detail "files=$blobCount bytes=$blobBytes seconds=$([math]::Round($blobDuration.TotalSeconds, 2))"
    }
    else {
        Write-BackupEvent -Event "backup.blobs.skipped" -Detail "no blob root supplied or present"
    }

    # --------------------------------------------------------------- manifest
    $schemaVersion = Get-SchemaVersion -Settings $settings

    $manifest = [ordered]@{
        formatVersion  = 1
        createdAt      = $started.ToString("o")
        completedAt    = ([DateTimeOffset]::UtcNow).ToString("o")
        database       = [ordered]@{
            name          = $settings.Database
            file          = "database.dump"
            sha256        = $dumpHash
            bytes         = $dumpBytes
            schemaVersion = $schemaVersion
            seconds       = [math]::Round($dumpDuration.TotalSeconds, 2)
        }
        blobs          = [ordered]@{
            included = $blobsCopied
            files    = $blobCount
            bytes    = $blobBytes
        }
    }

    $manifestPath = Join-Path $Destination "backup-manifest.json"
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8

    Write-BackupEvent -Event "backup.success" `
        -Detail "manifest=$manifestPath schema=$schemaVersion"

    exit 0
}
catch {
    # Failure is an event too. A backup that failed silently is worse than none,
    # because somebody will believe it ran.
    Write-BackupEvent -Event "backup.failure" -Detail $_.Exception.Message
    throw
}
