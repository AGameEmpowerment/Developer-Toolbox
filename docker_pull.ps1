# Pull Docker images required by the shared development stack.
#
# Most services are declared as image-based services in
# containers/docker-compose-common.yml, so we let docker compose pull those
# directly. The local SQL Server service is built from containers/mssql, so we
# also pull its base image explicitly.

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) { $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

$ComposeFile = Join-Path $ScriptDir "containers/docker-compose-common.yml"
$sqlBaseImage = "mcr.microsoft.com/mssql/server:latest"

if (Get-Command docker -ErrorAction SilentlyContinue) {
    Write-Host "Docker images and container setup started."

    $dockerInfo = docker info 2>$null
    $registryMirrors = @($dockerInfo | Where-Object { $_ -match '^\s+https?://' } | ForEach-Object { $_.Trim() })
    if ($registryMirrors.Count -gt 0) {
        Write-Host "Docker registry mirrors detected:" -ForegroundColor Yellow
        $registryMirrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    }

    Write-Host "Pulling compose-managed images from $ComposeFile..." -ForegroundColor Yellow
    docker compose -f $ComposeFile pull
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to pull compose-managed images (exit code $LASTEXITCODE)."

        if ($registryMirrors.Count -gt 0) {
            Write-Host "Docker is configured to use registry mirror(s). If one is unavailable, pulls will fail before reaching the upstream registry." -ForegroundColor Yellow
            Write-Host "Configured mirror(s):" -ForegroundColor Yellow
            $registryMirrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
            Write-Host "Check Docker Desktop > Settings > Docker Engine, or %APPDATA%\Docker\daemon.json, to remove or fix the mirror." -ForegroundColor Yellow
        }

        exit $LASTEXITCODE
    }

    Write-Host "Pulling SQL Server base image for containers/mssql/Dockerfile: $sqlBaseImage..." -ForegroundColor Yellow
    docker pull $sqlBaseImage
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to pull '$sqlBaseImage' (exit code $LASTEXITCODE)."

        if ($registryMirrors.Count -gt 0) {
            Write-Host "Docker is configured to use registry mirror(s). If one is unavailable, pulls will fail before reaching the upstream registry." -ForegroundColor Yellow
            Write-Host "Configured mirror(s):" -ForegroundColor Yellow
            $registryMirrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
            Write-Host "Check Docker Desktop > Settings > Docker Engine, or %APPDATA%\Docker\daemon.json, to remove or fix the mirror." -ForegroundColor Yellow
        }

        exit $LASTEXITCODE
    }
} else {
    Write-Error "Docker is not installed or not in PATH."
    exit 1
}

Write-Host "Docker images and container setup completed."
