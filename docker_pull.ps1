# Pull container images required by the shared development stack.
#
# Image-based services, including PostgreSQL and DynamoDB Local, are declared in
# containers/docker-compose-{common,azure,aws}.yml, so the selected runtime pulls them
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

$ComposeFiles = @("common", "azure", "aws" | ForEach-Object {
    Join-Path $ScriptDir "containers/docker-compose-$_.yml"
})
$EnvFile = Join-Path $ScriptDir "containers/.env"
$EnvExampleFile = Join-Path $ScriptDir "containers/.env.example"

function Resolve-ContainerRuntime {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("auto", "docker", "podman")]
        [string]$RequestedRuntime
    )

    if ($RequestedRuntime -eq "auto") {
        $installedRuntimes = @()
        foreach ($runtime in @("docker", "podman")) {
            if (Get-Command $runtime -ErrorAction SilentlyContinue) {
                $installedRuntimes += $runtime
                $runtimeIsReachable = $false
                try {
                    & $runtime info *> $null
                    $runtimeIsReachable = ($LASTEXITCODE -eq 0)
                } catch {
                    $runtimeIsReachable = $false
                }
                if (-not $runtimeIsReachable) {
                    continue
                }

                $composeIsAvailable = $false
                try {
                    & $runtime compose version *> $null
                    $composeIsAvailable = ($LASTEXITCODE -eq 0)
                } catch {
                    $composeIsAvailable = $false
                }
                if ($composeIsAvailable) {
                    return $runtime
                }
            }
        }

        if ($installedRuntimes.Count -gt 0) {
            throw @"
No installed container runtime is reachable with Compose support.
Start Docker or Podman, or select one explicitly with -ContainerRuntime.
"@
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

    $runtimeIsReachable = $false
    try {
        & $ContainerCli info *> $null
        $runtimeIsReachable = ($LASTEXITCODE -eq 0)
    } catch {
        $runtimeIsReachable = $false
    }
    if ($runtimeIsReachable) {
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

    $composeIsAvailable = $false
    try {
        & $ContainerCli compose version *> $null
        $composeIsAvailable = ($LASTEXITCODE -eq 0)
    } catch {
        $composeIsAvailable = $false
    }
    if (-not $composeIsAvailable) {
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

Write-Host "$RuntimeDisplayName missing-image preparation started."

# Match setup's additive environment preparation before Compose parses any services.
# Pulling images does not initialize function keys, certificates, or containers.
if (-not (Test-Path -LiteralPath $EnvExampleFile -PathType Leaf)) {
    throw ".env.example not found. Cannot prepare the image-pull environment."
}
if (-not (Test-Path -LiteralPath $EnvFile)) {
    Copy-Item -LiteralPath $EnvExampleFile -Destination $EnvFile
    Write-Host "Created containers/.env from .env.example." -ForegroundColor Green
}
$envLines = @(Get-Content -LiteralPath $EnvFile)
$missingAssignments = @()
foreach ($templateLine in Get-Content -LiteralPath $EnvExampleFile) {
    if ($templateLine -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=') {
        $key = $matches[1]
        if (-not ($envLines -cmatch "^\s*(?:export\s+)?$([regex]::Escape($key))\s*=")) {
            $missingAssignments += $templateLine
            $envLines += $templateLine
        }
    }
}
if ($missingAssignments.Count -gt 0) {
    # Preserve raw quoting/interpolation and handle existing files without a final newline.
    Add-Content -LiteralPath $EnvFile -Value ([Environment]::NewLine + ($missingAssignments -join [Environment]::NewLine))
    Write-Host "Added $($missingAssignments.Count) missing settings to containers/.env." -ForegroundColor Green
}

. (Join-Path $PSScriptRoot '.devcontainer/ensure_image.ps1')
$images = @($ComposeFiles | ForEach-Object {
    Select-String -LiteralPath $_ -Pattern '^\s+image:\s*(\S+)' | ForEach-Object { $_.Matches[0].Groups[1].Value }
}) + @('mcr.microsoft.com/mssql/server:latest', 'docker.io/library/python:latest')
foreach ($image in @($images | Sort-Object -Unique)) {
    Ensure-ToolboxImage -Runtime $ContainerCli -Image $image
}
Write-Host "$RuntimeDisplayName missing-image preparation completed."