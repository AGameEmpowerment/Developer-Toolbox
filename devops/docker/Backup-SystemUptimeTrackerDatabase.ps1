[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ComposeFile,

    [Parameter(Mandatory = $true)]
    [string]$EnvironmentFile,

    [string]$ProjectName = "systemuptimetracker-production"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

& docker compose version *> $null
$composeCommand = if ($LASTEXITCODE -eq 0) {
    @{ Executable = "docker"; Prefix = @("compose") }
}
elseif (Get-Command docker-compose -ErrorAction SilentlyContinue) {
    @{ Executable = "docker-compose"; Prefix = @() }
}
else {
    throw "Neither 'docker compose' nor 'docker-compose' is available in PATH."
}

$composeArguments = @(
    $composeCommand.Prefix
    "--project-name"
    $ProjectName
    "--env-file"
    $EnvironmentFile
    "-f"
    $ComposeFile
    "ps"
    "--quiet"
    "systemuptimetracker-sql"
)
$containerId = (& $composeCommand.Executable @composeArguments).Trim()

if ($LASTEXITCODE -ne 0) {
    throw "Unable to resolve the SystemUptimeTracker SQL container."
}

if ([string]::IsNullOrWhiteSpace($containerId)) {
    Write-Host "No running SystemUptimeTracker SQL container was found; there is no database to back up yet."
    return
}

$timestamp = [DateTime]::UtcNow.ToString("yyyyMMdd_HHmmss")
$backupPath = "/var/opt/mssql/backup/SystemUptimeTracker_$timestamp.bak"
$query = "IF DB_ID(N'SystemUptimeTracker') IS NOT NULL BACKUP DATABASE [SystemUptimeTracker] TO DISK=N'$backupPath' WITH COPY_ONLY, CHECKSUM, INIT"
$containerEnvironment = (& docker inspect --format "{{json .Config.Env}}" $containerId) | ConvertFrom-Json
$passwordEntry = $containerEnvironment |
    Where-Object { $_ -like "MSSQL_SA_PASSWORD=*" } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($passwordEntry)) {
    throw "The SQL container does not expose its configured SA password."
}
$sqlPassword = $passwordEntry.Substring("MSSQL_SA_PASSWORD=".Length)

& docker exec --user 0 $containerId sh -c "mkdir -p /var/opt/mssql/backup && chown -R mssql:root /var/opt/mssql/backup && chmod 770 /var/opt/mssql/backup"
if ($LASTEXITCODE -ne 0) {
    throw "Unable to prepare the SQL backup folder permissions."
}

& docker exec $containerId `
    /opt/mssql-tools18/bin/sqlcmd `
    -S localhost `
    -U sa `
    -P $sqlPassword `
    -C `
    -b `
    -Q $query
if ($LASTEXITCODE -ne 0) {
    throw "The pre-migration database backup failed."
}

Write-Host "Pre-migration database backup completed inside the persistent SQL volume."
