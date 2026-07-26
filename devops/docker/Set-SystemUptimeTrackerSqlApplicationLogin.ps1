[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ComposeFile,

    [Parameter(Mandatory = $true)]
    [string]$EnvironmentFile,

    [Parameter(Mandatory = $true)]
    [ValidatePattern("^[A-Za-z][A-Za-z0-9_]{0,127}$")]
    [string]$ApplicationUsername,

    [Parameter(Mandatory = $true)]
    [string]$ApplicationPassword,

    [string]$ProjectName = "systemuptimetracker-production"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ApplicationPassword) -or $ApplicationPassword -match "^\$\(.+\)$") {
    throw "A valid SQL application password was not provided."
}

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
$escapedApplicationPassword = $ApplicationPassword.Replace("'", "''")

$serverQuery = @"
IF DB_ID(N'SystemUptimeTracker') IS NULL
BEGIN
    CREATE DATABASE [SystemUptimeTracker];
END;
ALTER DATABASE [SystemUptimeTracker] SET AUTO_CLOSE OFF;

IF SUSER_ID(N'$ApplicationUsername') IS NULL
BEGIN
    CREATE LOGIN [$ApplicationUsername] WITH PASSWORD=N'$escapedApplicationPassword', CHECK_POLICY=ON;
END
ELSE
BEGIN
    ALTER LOGIN [$ApplicationUsername] WITH PASSWORD=N'$escapedApplicationPassword';
END;
"@

$databaseQuery = @"
USE [SystemUptimeTracker];
IF USER_ID(N'$ApplicationUsername') IS NULL
BEGIN
    CREATE USER [$ApplicationUsername] FOR LOGIN [$ApplicationUsername];
END;
IF IS_ROLEMEMBER(N'db_owner', N'$ApplicationUsername') <> 1
BEGIN
    ALTER ROLE [db_owner] ADD MEMBER [$ApplicationUsername];
END;
"@

& docker exec $containerId `
    /opt/mssql-tools18/bin/sqlcmd `
    -S localhost `
    -U sa `
    -P $saPassword `
    -C `
    -b `
    -Q $serverQuery
if ($LASTEXITCODE -ne 0) {
    throw "Provisioning the SystemUptimeTracker database or SQL application login failed."
}

& docker exec $containerId `
    /opt/mssql-tools18/bin/sqlcmd `
    -S localhost `
    -U sa `
    -P $saPassword `
    -C `
    -b `
    -Q $databaseQuery
if ($LASTEXITCODE -ne 0) {
    throw "Provisioning the SystemUptimeTracker database user failed."
}

Write-Host "SQL application login '$ApplicationUsername' is provisioned for the SystemUptimeTracker database."
