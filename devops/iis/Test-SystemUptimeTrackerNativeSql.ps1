[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ServerInstance,

    [Parameter(Mandatory = $true)]
    [ValidateSet("SystemUptimeTracker")]
    [string]$ConfirmDatabaseName,

    [Parameter(Mandatory = $true)]
    [ValidatePattern("^[A-Za-z][A-Za-z0-9_]{0,127}$")]
    [string]$ApplicationUsername,

    [Parameter(Mandatory = $true)]
    [string]$ApplicationPassword,

    [string]$ExpectedServerName = "REPLACE_WITH_SQL_SERVER"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$sqlcmd = Get-Command sqlcmd.exe -ErrorAction SilentlyContinue
if (-not $sqlcmd) {
    $sqlcmd = Get-ChildItem `
        -Path "C:\Program Files\Microsoft SQL Server" `
        -Filter sqlcmd.exe `
        -Recurse `
        -ErrorAction SilentlyContinue |
        Select-Object -First 1
}
if (-not $sqlcmd) {
    throw "sqlcmd.exe is not installed on this deployment host."
}
$sqlcmdPath = if ($sqlcmd.Source) { $sqlcmd.Source } else { $sqlcmd.FullName }

$escapedExpectedServerName = $ExpectedServerName.Replace("'", "''")
$query = @"
SET NOCOUNT ON;
IF UPPER(CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName'))) <> UPPER(N'$escapedExpectedServerName')
    THROW 51000, 'The connection does not target the expected SQL Server.', 1;
IF DB_NAME() <> N'SystemUptimeTracker'
    THROW 51001, 'The connection does not target the SystemUptimeTracker database.', 1;
IF IS_ROLEMEMBER(N'db_owner') <> 1
   AND HAS_PERMS_BY_NAME(N'database', N'database', N'CONTROL') <> 1
    THROW 51002, 'The application login cannot apply SystemUptimeTracker database migrations.', 1;

SELECT
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerName,
    DB_NAME() AS DatabaseName,
    USER_NAME() AS DatabaseUser,
    CASE
        WHEN OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL THEN 0
        ELSE (SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory])
    END AS AppliedMigrationCount;
"@

& $sqlcmdPath `
    -S $ServerInstance `
    -d $ConfirmDatabaseName `
    -U $ApplicationUsername `
    -P $ApplicationPassword `
    -C `
    -b `
    -W `
    -Q $query
if ($LASTEXITCODE -ne 0) {
    throw "The configured SystemUptimeTracker SQL preflight failed."
}

Write-Host "Configured SystemUptimeTracker SQL preflight passed."
