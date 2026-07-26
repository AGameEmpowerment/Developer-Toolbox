[CmdletBinding()]
param(
    [string]$EnvironmentFile = "devops/docker/systemuptimetracker.production.env"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$composeFile = Join-Path $PSScriptRoot "docker-compose.yml"
$localComposeFile = Join-Path $PSScriptRoot "docker-compose.local.yml"
$environmentHelper = Join-Path $PSScriptRoot "SystemUptimeTracker.Environment.ps1"
$resolvedEnvironmentFile = if ([IO.Path]::IsPathRooted($EnvironmentFile)) {
    $EnvironmentFile
}
else {
    Join-Path $repoRoot $EnvironmentFile
}

if (-not (Test-Path -LiteralPath $resolvedEnvironmentFile)) {
    throw "Local environment file '$resolvedEnvironmentFile' was not found. Create it from systemuptimetracker.production.env.example and supply SYSTEMUPTIMETRACKER_SQL_SA_PASSWORD."
}

. $environmentHelper
Assert-SystemUptimeTrackerSqlEnvironmentCanReuseVolume -EnvironmentFilePath $resolvedEnvironmentFile

# Compose evaluates variables for every service even when only SQL is selected.
# These non-secret placeholders suppress irrelevant backend warnings; the
# backend is not started by this command.
$env:SYSTEMUPTIMETRACKER_SQL_APP_USERNAME = "not-used-by-local-sql-only"
$env:SYSTEMUPTIMETRACKER_SQL_APP_PASSWORD = "not-used-by-local-sql-only"

& docker compose `
    --project-name systemuptimetracker `
    --env-file $resolvedEnvironmentFile `
    -f $composeFile `
    -f $localComposeFile `
    up -d systemuptimetracker-sql

if ($LASTEXITCODE -ne 0) {
    throw "Docker Compose failed to start systemuptimetracker-sql."
}

$deadline = [DateTimeOffset]::UtcNow.AddMinutes(3)
do {
    $health = & docker inspect systemuptimetracker-sql --format "{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}" 2>$null
    if ($health -eq "healthy") {
        Write-Host "systemuptimetracker-sql is healthy at 127.0.0.1:11433."
        return
    }

    Start-Sleep -Seconds 3
} while ([DateTimeOffset]::UtcNow -lt $deadline)

& docker logs --tail 50 systemuptimetracker-sql
throw "systemuptimetracker-sql did not become healthy within three minutes."
