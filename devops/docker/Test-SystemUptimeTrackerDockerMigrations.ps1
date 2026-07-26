[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ComposeFile,

    [Parameter(Mandatory = $true)]
    [string]$EnvironmentFile,

    [Parameter(Mandatory = $true)]
    [string]$MigrationSourceRoot,

    [Parameter(Mandatory = $true)]
    [ValidatePattern("^[A-Za-z][A-Za-z0-9_]{0,127}$")]
    [string]$ApplicationUsername,

    [Parameter(Mandatory = $true)]
    [string]$ApplicationPassword,

    [string]$ProjectName = "systemuptimetracker-production",

    [ValidateRange(1, 60)]
    [int]$PollSeconds = 5,

    [ValidateRange(1, 600)]
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$migrationFiles = Get-ChildItem -LiteralPath $MigrationSourceRoot -Filter "*.cs" -Recurse
$expectedMigrations = @(
    $migrationFiles |
        Select-String -Pattern '\[Migration\("(?<id>[^"]+)"\)\]' |
        ForEach-Object { $_.Matches[0].Groups["id"].Value } |
        Sort-Object -Unique
)
if ($expectedMigrations.Count -eq 0) {
    throw "No EF migration identifiers were found under '$MigrationSourceRoot'."
}

$invalidMigration = $expectedMigrations | Where-Object { $_ -notmatch "^[A-Za-z0-9_]+$" } | Select-Object -First 1
if ($null -ne $invalidMigration) {
    throw "Migration identifier '$invalidMigration' contains characters that cannot be safely verified."
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

$expectedRows = ($expectedMigrations | ForEach-Object { "(N'$_')" }) -join ",`n    "
$query = @"
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51000, 'The EF migrations history table does not exist.', 1;
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NULL
    THROW 51002, 'The identity schema was not created.', 1;

DECLARE @ExpectedMigrations TABLE (
    [MigrationId] nvarchar(150) NOT NULL PRIMARY KEY
);
INSERT INTO @ExpectedMigrations ([MigrationId])
VALUES
    $expectedRows;

IF EXISTS (
    SELECT [MigrationId] FROM @ExpectedMigrations
    EXCEPT
    SELECT [MigrationId] FROM [dbo].[__EFMigrationsHistory]
)
    THROW 51003, 'One or more expected EF migrations have not been applied.', 1;

IF EXISTS (
    SELECT [MigrationId] FROM [dbo].[__EFMigrationsHistory]
    EXCEPT
    SELECT [MigrationId] FROM @ExpectedMigrations
)
    THROW 51004, 'The database contains an unexpected EF migration.', 1;
"@

$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$lastQueryExitCode = 0

do {
    $null = @(
        & docker exec $containerId `
            /opt/mssql-tools18/bin/sqlcmd `
            -S localhost `
            -d SystemUptimeTracker `
            -U $ApplicationUsername `
            -P $ApplicationPassword `
            -C `
            -b `
            -Q $query 2>&1
    )
    $lastQueryExitCode = $LASTEXITCODE

    if ($lastQueryExitCode -eq 0) {
        break
    }

    if ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds $PollSeconds
    }
} while ([DateTime]::UtcNow -lt $deadline)

if ($lastQueryExitCode -ne 0) {
    throw "Docker migration verification did not converge to the exact expected set within $TimeoutSeconds seconds."
}

Write-Host "Docker migration verification passed with exactly $($expectedMigrations.Count) applied migrations."
