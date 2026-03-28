# Pull Docker images required by the shared development stack, including
# base images referenced by repo-local Dockerfiles.

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (Get-Command docker -ErrorAction SilentlyContinue) {
    Write-Host "Docker images and container setup started."

    $images = @(
        "datalust/seq:latest",
        "mcr.microsoft.com/azure-messaging/servicebus-emulator:latest",
        "mcr.microsoft.com/azure-sql-edge:latest",
        "mcr.microsoft.com/azure-storage/azurite",
        "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview",
        "mcr.microsoft.com/devcontainers/dotnet:1-10.0",
        "mcr.microsoft.com/mssql/server:latest",
        "redis:latest",
        "redis/redisinsight:latest",
        "rnwood/smtp4dev:latest",
        "wiremock/wiremock:latest"
    )

    $dockerInfo = docker info 2>$null
    $registryMirrors = @($dockerInfo | Where-Object { $_ -match '^\s+https?://' } | ForEach-Object { $_.Trim() })
    if ($registryMirrors.Count -gt 0) {
        Write-Host "Docker registry mirrors detected:" -ForegroundColor Yellow
        $registryMirrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    }

    foreach ($image in $images) {
        Write-Host "Pulling $image..." -ForegroundColor Yellow
        docker pull $image
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to pull '$image' (exit code $LASTEXITCODE)."

            if ($registryMirrors.Count -gt 0) {
                Write-Host "Docker is configured to use registry mirror(s). If one is unavailable, pulls will fail before reaching the upstream registry." -ForegroundColor Yellow
                Write-Host "Configured mirror(s):" -ForegroundColor Yellow
                $registryMirrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
                Write-Host "Check Docker Desktop > Settings > Docker Engine, or %APPDATA%\Docker\daemon.json, to remove or fix the mirror." -ForegroundColor Yellow
            }

            exit $LASTEXITCODE
        }
    }
} else {
    Write-Error "Docker is not installed or not in PATH."
    exit 1
}

Write-Host "Docker images and container setup completed."
