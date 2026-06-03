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

$ContainersDir = Join-Path $ScriptDir "containers"
$ComposeFile = Join-Path $ContainersDir "docker-compose-common.yml"
$sqlBaseImage = "mcr.microsoft.com/mssql/server:latest"

function Sync-DockerClientEnvironment {
    if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        return
    }

    $persistedDockerHost = [Environment]::GetEnvironmentVariable('DOCKER_HOST', 'User')
    if (-not $persistedDockerHost) {
        $persistedDockerHost = [Environment]::GetEnvironmentVariable('DOCKER_HOST', 'Machine')
    }

    if (-not $env:DOCKER_HOST -and $persistedDockerHost) {
        $env:DOCKER_HOST = $persistedDockerHost.Trim()
    }

    if ($env:DOCKER_CONTEXT -and [string]::IsNullOrWhiteSpace($env:DOCKER_CONTEXT)) {
        Remove-Item Env:DOCKER_CONTEXT -ErrorAction SilentlyContinue
    }
}

function Assert-LinuxContainerEngine {
    $dockerOsType = docker info --format '{{.OSType}}' 2>$null
    $dockerOperatingSystem = docker info --format '{{.OperatingSystem}}' 2>$null

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Unable to determine the Docker daemon container OS type."
        exit 1
    }

    $dockerOsType = $dockerOsType.Trim()
    $dockerOperatingSystem = $dockerOperatingSystem.Trim()

    if ($dockerOsType -ne 'linux') {
        Write-Error "This development stack requires Docker to run Linux containers. Current Docker daemon OSType: '$dockerOsType' ($dockerOperatingSystem)."

        if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
            Write-Host "If you are using Docker Desktop, switch to Linux containers and rerun this script." -ForegroundColor Yellow
            Write-Host "Typical fix: Docker Desktop tray icon > Switch to Linux containers..." -ForegroundColor Gray
        }

        exit 1
    }
}

if (Get-Command docker -ErrorAction SilentlyContinue) {
    Write-Host "Docker images and container setup started."

    Sync-DockerClientEnvironment

    docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Cannot access the Docker daemon. The current user likely does not have permission to the Docker API named pipe or the daemon is not running."

        $dockerService = Get-Service -Name "docker" -ErrorAction SilentlyContinue
        if ($dockerService) {
            Write-Host "Docker service status: $($dockerService.Status)" -ForegroundColor Yellow
            if ($dockerService.Status -ne 'Running') {
                Write-Host "Start it with: Start-Service docker" -ForegroundColor Gray
            }
        }

        Write-Host "Try these fixes:" -ForegroundColor Yellow
        Write-Host "  1. Start an elevated PowerShell and run this script again." -ForegroundColor Gray
        Write-Host "  2. Ensure the Docker daemon service is running (Get-Service docker)." -ForegroundColor Gray
        Write-Host "  3. If using a non-Desktop Docker Engine, configure daemon access for your non-admin user." -ForegroundColor Gray
        exit 1
    }

    Assert-LinuxContainerEngine

    $dockerInfo = docker info 2>$null
    $registryMirrors = @($dockerInfo | Where-Object { $_ -match '^\s+https?://' } | ForEach-Object { $_.Trim() })
    if ($registryMirrors.Count -gt 0) {
        Write-Host "Docker registry mirrors detected:" -ForegroundColor Yellow
        $registryMirrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    }

    $useDockerComposePlugin = $false
    docker compose version *> $null
    if ($LASTEXITCODE -eq 0) {
        $useDockerComposePlugin = $true
    }

    $useDockerComposeStandalone = $false
    if (-not $useDockerComposePlugin -and (Get-Command docker-compose -ErrorAction SilentlyContinue)) {
        $useDockerComposeStandalone = $true
    }

    if (-not $useDockerComposePlugin -and -not $useDockerComposeStandalone) {
        Write-Error "Neither 'docker compose' nor 'docker-compose' is available. Install Docker Compose and try again."
        exit 1
    }

    Write-Host "Pulling compose-managed images from $ComposeFile..." -ForegroundColor Yellow

    Push-Location $ContainersDir
    try {
        # Run from containers/ so compose automatically loads containers/.env across compose variants.
        if ($useDockerComposePlugin) {
            docker compose -f "docker-compose-common.yml" pull
        } else {
            docker-compose -f "docker-compose-common.yml" pull
        }
    } finally {
        Pop-Location
    }

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
