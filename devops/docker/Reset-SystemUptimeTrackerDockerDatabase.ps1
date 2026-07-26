[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [Parameter(Mandatory = $true)]
    [string]$ComposeFile,

    [Parameter(Mandatory = $true)]
    [string]$EnvironmentFile,

    [Parameter(Mandatory = $true)]
    [ValidateSet("SystemUptimeTracker")]
    [string]$ConfirmDatabaseName,

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

$composeBaseArguments = @(
    $composeCommand.Prefix
    "--project-name"
    $ProjectName
    "--env-file"
    $EnvironmentFile
    "-f"
    $ComposeFile
)

& $composeCommand.Executable @composeBaseArguments stop systemuptimetracker-frontend systemuptimetracker-backend
if ($LASTEXITCODE -ne 0) {
    throw "Unable to stop the Docker application services before resetting the database."
}

$containerId = (& $composeCommand.Executable @composeBaseArguments ps --quiet systemuptimetracker-sql).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
    throw "Unable to resolve the running SystemUptimeTracker SQL container."
}

$containerEnvironment = (& docker inspect --format "{{json .Config.Env}}" $containerId) | ConvertFrom-Json
$passwordEntry = $containerEnvironment |
    Where-Object { $_ -like "MSSQL_SA_PASSWORD=*" } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($passwordEntry)) {
    throw "The SQL container does not expose its configured SA password."
}
$saPassword = $passwordEntry.Substring("MSSQL_SA_PASSWORD=".Length)

$query = @"
IF DB_ID(N'SystemUptimeTracker') IS NOT NULL
BEGIN
    ALTER DATABASE [SystemUptimeTracker] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [SystemUptimeTracker];
END;
CREATE DATABASE [SystemUptimeTracker];
ALTER DATABASE [SystemUptimeTracker] SET AUTO_CLOSE OFF;
"@

if ($PSCmdlet.ShouldProcess(
        "Docker SQL container $containerId / database SystemUptimeTracker",
        "Drop and recreate only the SystemUptimeTracker database"
    )) {
    & docker exec $containerId `
        /opt/mssql-tools18/bin/sqlcmd `
        -S localhost `
        -U sa `
        -P $saPassword `
        -C `
        -b `
        -Q $query
    if ($LASTEXITCODE -ne 0) {
        throw "Resetting the Docker SystemUptimeTracker database failed."
    }
}

Write-Host "Docker database 'SystemUptimeTracker' was dropped and recreated. Other databases and the SQL data volume were not modified."
