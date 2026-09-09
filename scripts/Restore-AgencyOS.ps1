<#
.SYNOPSIS
    Restores an AgencyOS backup: the canonical database and the document bytes.

.DESCRIPTION
    Restore is the destructive half. It replaces the contents of a database that
    somebody may still be using, so it refuses to run without -Force, and it
    verifies the backup before it touches anything - a restore that destroys a
    working database and then discovers the dump was truncated is the worst
    possible ordering.

    This is an operator tool, not an endpoint. Nothing in the tenant-facing API can
    reach it, because a restore is a decision about a deployment rather than an
    action inside one (M15 §56).

.PARAMETER BackupPath
    Directory written by Backup-AgencyOS.ps1.

.PARAMETER ConnectionString
    Npgsql connection string for the target database. It is recreated.

.PARAMETER BlobRoot
    Where document bytes should be restored to. Omit if the backup contains none.

.PARAMETER Force
    Required. Without it the script reports what it would do and stops.

.PARAMETER ExpectedSchemaVersion
    Optional guard. When supplied, the restore refuses a backup whose schema
    version differs - for restoring into a deployment whose binaries expect a
    particular migration.

.EXAMPLE
    ./scripts/Restore-AgencyOS.ps1 -BackupPath D:\Backups\2026-09-09 `
        -ConnectionString $env:AGENCYOS_CONNECTION -BlobRoot D:\AgencyOS\blobs -Force
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $BackupPath,
    [Parameter(Mandatory = $true)][string] $ConnectionString,
    [string] $BlobRoot,
    [switch] $Force,
    [string] $ExpectedSchemaVersion
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "AgencyOS.Backup.Common.ps1")

Write-BackupEvent -Event "restore.start" -Detail "source=$BackupPath"

try {
    $settings = ConvertFrom-ConnectionString -ConnectionString $ConnectionString

    # ------------------------------------------------- verify before destroying
    $manifestPath = Join-Path $BackupPath "backup-manifest.json"

    if (-not (Test-Path $manifestPath)) {
        throw "No backup manifest at '$manifestPath'. This directory is not an AgencyOS backup."
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

    if ($manifest.formatVersion -ne 1) {
        throw "Backup manifest format version $($manifest.formatVersion) is not one this script understands."
    }

    $dumpPath = Join-Path $BackupPath $manifest.database.file

    if (-not (Test-Path $dumpPath)) {
        throw "The manifest names '$($manifest.database.file)' but it is not in the backup."
    }

    $actualHash = (Get-FileHash -LiteralPath $dumpPath -Algorithm SHA256).Hash.ToLowerInvariant()

    if ($actualHash -ne $manifest.database.sha256) {
        throw ("The dump does not match its manifest. Expected $($manifest.database.sha256), found $actualHash. " +
            "Nothing has been changed. This backup is damaged and must not be restored.")
    }

    Write-BackupEvent -Event "restore.verified" `
        -Detail "sha256=$actualHash schema=$($manifest.database.schemaVersion)"

    if ($ExpectedSchemaVersion -and $manifest.database.schemaVersion -ne $ExpectedSchemaVersion) {
        throw ("This backup is at schema '$($manifest.database.schemaVersion)' and the deployment expects " +
            "'$ExpectedSchemaVersion'. Restoring it would leave the binaries and the database disagreeing.")
    }

    if (-not $Force) {
        Write-BackupEvent -Event "restore.refused" -Detail "-Force was not supplied"

        Write-Host ""
        Write-Host "This would REPLACE the database '$($settings.Database)' on $($settings.Host):$($settings.Port)"

        if ($BlobRoot) {
            Write-Host "and restore $($manifest.blobs.files) document files into '$BlobRoot'."
        }

        Write-Host ""
        Write-Host "Nothing has been changed. Re-run with -Force to proceed."

        exit 2
    }

    Assert-PostgresTooling -Tool "pg_restore"

    # -------------------------------------------------------------- database
    Write-BackupEvent -Event "restore.database.start" -Detail "database=$($settings.Database)"

    $restoreStarted = [DateTimeOffset]::UtcNow

    $adminArguments = @(
        "--host=$($settings.Host)"
        "--port=$($settings.Port)"
        "--username=$($settings.Username)"
        "--dbname=postgres"
        "--tuples-only"
        "--no-align"
    )

    # Dropped and recreated rather than restored over. --clean leaves anything the
    # dump does not mention, so a restore would silently inherit rows from whatever
    # was there before and call the result recovered.
    Invoke-PostgresTool -Tool "psql" -Password $settings.Password -Arguments (
        $adminArguments + @("--command=DROP DATABASE IF EXISTS `"$($settings.Database)`" WITH (FORCE)")
    ) | Out-Null

    Invoke-PostgresTool -Tool "psql" -Password $settings.Password -Arguments (
        $adminArguments + @("--command=CREATE DATABASE `"$($settings.Database)`"")
    ) | Out-Null

    Invoke-PostgresTool -Tool "pg_restore" -Password $settings.Password -Arguments @(
        "--host=$($settings.Host)"
        "--port=$($settings.Port)"
        "--username=$($settings.Username)"
        "--dbname=$($settings.Database)"
        "--no-owner"
        "--no-privileges"
        "--exit-on-error"
        $dumpPath
    ) | Out-Null

    $restoreDuration = [DateTimeOffset]::UtcNow - $restoreStarted

    Write-BackupEvent -Event "restore.database.done" `
        -Detail "seconds=$([math]::Round($restoreDuration.TotalSeconds, 2))"

    # ------------------------------------------------------------------ blobs
    $restoredBlobs = 0

    if ($manifest.blobs.included -and $BlobRoot) {
        Write-BackupEvent -Event "restore.blobs.start" -Detail "root=$BlobRoot"

        $blobSource = Join-Path $BackupPath "blobs"

        if (-not (Test-Path $BlobRoot)) {
            New-Item -ItemType Directory -Force -Path $BlobRoot | Out-Null
        }

        Get-ChildItem -LiteralPath $blobSource -Recurse -File -Force | ForEach-Object {
            $relative = [System.IO.Path]::GetRelativePath($blobSource, $_.FullName)
            $target = Join-Path $BlobRoot $relative
            $targetDirectory = Split-Path -Parent $target

            if (-not (Test-Path $targetDirectory)) {
                New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
            }

            Copy-Item -LiteralPath $_.FullName -Destination $target -Force

            $script:restoredBlobs++
        }

        if ($restoredBlobs -ne $manifest.blobs.files) {
            throw ("The manifest recorded $($manifest.blobs.files) document files and $restoredBlobs were restored. " +
                "The backup is incomplete.")
        }

        Write-BackupEvent -Event "restore.blobs.done" -Detail "files=$restoredBlobs"
    }
    elseif ($manifest.blobs.included) {
        Write-BackupEvent -Event "restore.blobs.skipped" `
            -Detail "backup contains $($manifest.blobs.files) files but no -BlobRoot was given"
    }

    # ----------------------------------------------------------- validate
    $schemaVersion = Get-SchemaVersion -Settings $settings

    if ($schemaVersion -ne $manifest.database.schemaVersion) {
        throw ("After restore the schema is '$schemaVersion' and the manifest recorded " +
            "'$($manifest.database.schemaVersion)'. The restore did not produce the database that was backed up.")
    }

    Write-BackupEvent -Event "restore.success" `
        -Detail "schema=$schemaVersion blobs=$restoredBlobs"

    exit 0
}
catch {
    Write-BackupEvent -Event "restore.failure" -Detail $_.Exception.Message
    throw
}
