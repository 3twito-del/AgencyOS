<#
    Shared plumbing for the AgencyOS backup and restore scripts.

    Kept small and inspectable on purpose (M15 §75). Operational correctness that
    hides inside opaque automation is operational correctness nobody can audit at
    three in the morning.
#>

Set-StrictMode -Version Latest

<#
.SYNOPSIS
    Emits a structured operational event.

.DESCRIPTION
    Backup and restore are operations somebody must be able to monitor. Each step
    emits a line with a stable event name so an operator, a scheduler or a log
    scraper can tell start from success from failure without parsing prose.

    Deliberately never includes a password, a connection string or a document
    title.
#>
function Write-BackupEvent {
    param(
        [Parameter(Mandatory = $true)][string] $Event,
        [string] $Detail
    )

    $timestamp = ([DateTimeOffset]::UtcNow).ToString("o")

    if ($Detail) {
        Write-Host "[$timestamp] $Event $Detail"
    }
    else {
        Write-Host "[$timestamp] $Event"
    }
}

<#
.SYNOPSIS
    Parses the parts of a connection string the PostgreSQL tools need.

.DESCRIPTION
    Npgsql keywords rather than libpq ones, because the connection string an
    operator already has is the one configured for the application. Returned as a
    plain object so the password can be handed to a child process through the
    environment instead of the command line.
#>
function ConvertFrom-ConnectionString {
    param([Parameter(Mandatory = $true)][string] $ConnectionString)

    $parts = @{}

    foreach ($segment in $ConnectionString.Split(';')) {
        if (-not $segment.Contains('=')) { continue }

        $index = $segment.IndexOf('=')
        $key = $segment.Substring(0, $index).Trim().ToLowerInvariant()
        $value = $segment.Substring($index + 1).Trim()

        $parts[$key] = $value
    }

    function Get-Part {
        param([string[]] $Names, [string] $Fallback)

        foreach ($name in $Names) {
            if ($parts.ContainsKey($name) -and $parts[$name]) { return $parts[$name] }
        }

        return $Fallback
    }

    $database = Get-Part -Names @('database', 'initial catalog') -Fallback ''

    if (-not $database) {
        throw "The connection string names no database."
    }

    return [pscustomobject]@{
        Host     = Get-Part -Names @('host', 'server') -Fallback 'localhost'
        Port     = Get-Part -Names @('port') -Fallback '5432'
        Username = Get-Part -Names @('username', 'user id', 'userid', 'uid') -Fallback 'postgres'
        Password = Get-Part -Names @('password', 'pwd') -Fallback ''
        Database = $database
    }
}

<#
.SYNOPSIS
    Fails early, and with the actual reason, when a PostgreSQL tool is missing or too old.

.DESCRIPTION
    pg_dump refuses to dump a server newer than itself, and the message it gives
    when that happens is easy to mistake for a connection problem. AgencyOS pins
    PostgreSQL 18.6, so an operator on an older client meets this check instead -
    which names the version it found and the version it needs.
#>
function Assert-PostgresTooling {
    param(
        [Parameter(Mandatory = $true)][string] $Tool,
        [int] $MinimumMajorVersion = 18
    )

    $command = Get-Command $Tool -ErrorAction SilentlyContinue

    if (-not $command) {
        throw "$Tool is not on PATH. AgencyOS backup requires the PostgreSQL $MinimumMajorVersion client tools."
    }

    $reported = & $Tool --version 2>&1 | Out-String

    if ($reported -notmatch '(\d+)\.(\d+)') {
        throw "$Tool did not report a version. Got: $reported"
    }

    $major = [int]$Matches[1]

    if ($major -lt $MinimumMajorVersion) {
        throw ("$Tool is version $major, older than the PostgreSQL $MinimumMajorVersion server AgencyOS runs. " +
            "A client older than the server cannot dump it. Install postgresql-client-$MinimumMajorVersion.")
    }

    Write-BackupEvent -Event "tooling.ok" -Detail "$Tool major=$major"
}

<#
.SYNOPSIS
    Runs a PostgreSQL command-line tool, keeping the password out of the command line.
#>
function Invoke-PostgresTool {
    param(
        [Parameter(Mandatory = $true)][string] $Tool,
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [string] $Password
    )

    $previous = $env:PGPASSWORD

    try {
        if ($Password) { $env:PGPASSWORD = $Password }

        # Captured rather than streamed, so a failure can be reported with its
        # actual message instead of a bare exit code.
        $output = & $Tool @Arguments 2>&1
        $exit = $LASTEXITCODE

        if ($exit -ne 0) {
            throw "$Tool failed with exit code $exit. $($output | Out-String)"
        }

        return $output
    }
    finally {
        $env:PGPASSWORD = $previous
    }
}

<#
.SYNOPSIS
    Reads the applied schema version from the migrations history.

.DESCRIPTION
    Recorded in the backup manifest so a restore can refuse a dump whose schema the
    running binaries do not expect. The last applied migration identifier is the
    only version of the database that is true by construction rather than by
    somebody remembering to update it.
#>
function Get-SchemaVersion {
    param([Parameter(Mandatory = $true)] $Settings)

    $query = 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1'

    $arguments = @(
        "--host=$($Settings.Host)"
        "--port=$($Settings.Port)"
        "--username=$($Settings.Username)"
        "--dbname=$($Settings.Database)"
        "--tuples-only"
        "--no-align"
        "--command=$query"
    )

    $result = Invoke-PostgresTool -Tool "psql" -Arguments $arguments -Password $Settings.Password

    return ($result | Out-String).Trim()
}
