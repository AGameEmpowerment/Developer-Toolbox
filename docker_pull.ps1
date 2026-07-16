# Pull container images required by the shared development stack.
#
# Most services are declared as image-based services in
# containers/docker-compose-common.yml, so we let the selected runtime pull those
# directly. The local SQL Server service is built from containers/mssql, so we
# also pull its base image explicitly.

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet("auto", "docker", "podman")]
    [string]$ContainerRuntime = $(if ($env:CONTAINER_RUNTIME) { $env:CONTAINER_RUNTIME } else { "auto" })
)

if ($PSVersionTable.PSVersion -lt [version]"5.1") {
    Write-Error "Missing dependency: PowerShell 5.1 or later is required. Current version: $($PSVersionTable.PSVersion)."
    exit 1
}

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) { $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

$ComposeFile = Join-Path $ScriptDir "containers/docker-compose-common.yml"
$sqlBaseImage = "mcr.microsoft.com/mssql/server:latest"

function Resolve-ContainerRuntime {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("auto", "docker", "podman")]
        [string]$RequestedRuntime
    )

    if ($RequestedRuntime -eq "auto") {
        foreach ($runtime in @("docker", "podman")) {
            if (Get-Command $runtime -ErrorAction SilentlyContinue) {
                return $runtime
            }
        }

        throw @"
Missing dependency: no supported container runtime CLI was found.
Install Docker Desktop or Podman, make sure the CLI is available in PATH, then open a new terminal.
"@
    }

    if (Get-Command $RequestedRuntime -ErrorAction SilentlyContinue) {
        return $RequestedRuntime
    }

    throw @"
Missing dependency: '$RequestedRuntime' was not found in PATH.
Install $RequestedRuntime, make sure its CLI is available in PATH, then open a new terminal.
"@
}

function Assert-ContainerRuntimeReady {
    param(
        [Parameter(Mandatory)]
        [string]$ContainerCli,

        [Parameter(Mandatory)]
        [string]$RuntimeDisplayName
    )

    & $ContainerCli info *> $null
    if ($LASTEXITCODE -eq 0) {
        return
    }

    if ($ContainerCli -eq "docker") {
        throw @"
$RuntimeDisplayName CLI is installed, but the Docker engine is not reachable.
Start Docker Desktop, wait until it finishes starting, then rerun this script.
"@
    }

    throw @"
$RuntimeDisplayName CLI is installed, but the Podman engine is not reachable.
Start the Podman machine with 'podman machine start', then rerun this script.
"@
}

function Assert-ContainerComposeAvailable {
    param(
        [Parameter(Mandatory)]
        [string]$ContainerCli,

        [Parameter(Mandatory)]
        [string]$RuntimeDisplayName
    )

    & $ContainerCli compose version *> $null
    if ($LASTEXITCODE -ne 0) {
        throw @"
Missing dependency: $RuntimeDisplayName compose support is not available.
Install $RuntimeDisplayName with compose support, or choose another runtime with -ContainerRuntime.
"@
    }
}

$ContainerCli = Resolve-ContainerRuntime -RequestedRuntime $ContainerRuntime
$RuntimeDisplayName = (Get-Culture).TextInfo.ToTitleCase($ContainerCli)
Assert-ContainerRuntimeReady -ContainerCli $ContainerCli -RuntimeDisplayName $RuntimeDisplayName
Assert-ContainerComposeAvailable -ContainerCli $ContainerCli -RuntimeDisplayName $RuntimeDisplayName

Write-Host "$RuntimeDisplayName images and container setup started."

$registryMirrors = @()
if ($ContainerCli -eq "docker") {
    $containerInfo = & $ContainerCli info 2>$null
    $registryMirrors = @($containerInfo | Where-Object { $_ -match '^\s+https?://' } | ForEach-Object { $_.Trim() })
    if ($registryMirrors.Count -gt 0) {
        Write-Host "$RuntimeDisplayName registry mirrors detected:" -ForegroundColor Yellow
        $registryMirrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    }
}

Write-Host "Pulling compose-managed images from $ComposeFile..." -ForegroundColor Yellow
& $ContainerCli compose -f $ComposeFile pull
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to pull compose-managed images (exit code $LASTEXITCODE)." -ErrorAction Continue

    if ($ContainerCli -eq "docker" -and $registryMirrors.Count -gt 0) {
        Write-Host "Docker is configured to use registry mirror(s). If one is unavailable, pulls will fail before reaching the upstream registry." -ForegroundColor Yellow
        Write-Host "Configured mirror(s):" -ForegroundColor Yellow
        $registryMirrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
        Write-Host "Check Docker Desktop > Settings > Docker Engine, or %APPDATA%\Docker\daemon.json, to remove or fix the mirror." -ForegroundColor Yellow
    }

    exit $LASTEXITCODE
}

Write-Host "Pulling SQL Server base image for containers/mssql/Dockerfile: $sqlBaseImage..." -ForegroundColor Yellow
& $ContainerCli pull $sqlBaseImage
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to pull '$sqlBaseImage' (exit code $LASTEXITCODE)." -ErrorAction Continue

    if ($ContainerCli -eq "docker" -and $registryMirrors.Count -gt 0) {
        Write-Host "Docker is configured to use registry mirror(s). If one is unavailable, pulls will fail before reaching the upstream registry." -ForegroundColor Yellow
        Write-Host "Configured mirror(s):" -ForegroundColor Yellow
        $registryMirrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
        Write-Host "Check Docker Desktop > Settings > Docker Engine, or %APPDATA%\Docker\daemon.json, to remove or fix the mirror." -ForegroundColor Yellow
    }

    exit $LASTEXITCODE
}

Write-Host "$RuntimeDisplayName images and container setup completed."
