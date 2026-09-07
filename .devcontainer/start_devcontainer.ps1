<#
.SYNOPSIS
Starts this repository's devcontainer with Docker or Podman.

.DESCRIPTION
Selects the first reachable host container runtime, preferring Docker and falling
back to Podman, then runs the Dev Container CLI with that runtime through its
--docker-path option. The runtime starts the outer devcontainer. The
Docker-in-Docker feature still supplies the nested Docker daemon used by
start_developer_toolkit.ps1 inside the devcontainer.

.PARAMETER ContainerRuntime
Selects the host container runtime. Auto tries Docker first and then Podman.
Supported values are auto, docker, and podman. The default is auto, or the value
of the CONTAINER_RUNTIME environment variable.

.PARAMETER PodmanConnection
Selects a named Podman remote connection for this process. Use a rootful connection
when Docker-in-Docker cannot start through the default rootless connection.

.EXAMPLE
pwsh -File ./.devcontainer/start_devcontainer.ps1

Uses Docker when its engine is reachable, otherwise falls back to Podman.

.EXAMPLE
pwsh -File ./.devcontainer/start_devcontainer.ps1 -ContainerRuntime docker

.EXAMPLE
pwsh -File ./.devcontainer/start_devcontainer.ps1 -ContainerRuntime podman

.EXAMPLE
pwsh -File ./.devcontainer/start_devcontainer.ps1 `
    -ContainerRuntime podman `
    -PodmanConnection podman-machine-default-root

.EXAMPLE
pwsh -File ./.devcontainer/start_devcontainer.ps1 -ContainerRuntime docker -WhatIf

.OUTPUTS
The output produced by the Dev Container CLI.

.NOTES
Docker is the supported baseline for the Docker-in-Docker feature. Podman can run
the outer devcontainer through the Dev Container CLI, but upstream classifies other
host engines as untested for Docker-in-Docker. Use a running rootful Podman machine.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter()]
    [ValidateSet("auto", "docker", "podman")]
    [string]$ContainerRuntime = $(if ($env:CONTAINER_RUNTIME) { $env:CONTAINER_RUNTIME } else { "auto" }),

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$PodmanConnection
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

if ($PodmanConnection -and $ContainerRuntime -ne "podman") {
    throw "PodmanConnection requires -ContainerRuntime podman."
}

function Resolve-HostContainerRuntime {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("auto", "docker", "podman")]
        [string]$RequestedRuntime
    )

    $runtimeNames = if ($RequestedRuntime -eq "auto") {
        @("docker", "podman")
    } else {
        @($RequestedRuntime)
    }

    $installedRuntimes = @()
    foreach ($runtimeName in $runtimeNames) {
        $runtimeCommand = Get-Command $runtimeName -ErrorAction SilentlyContinue
        if (-not $runtimeCommand) {
            continue
        }

        $installedRuntimes += $runtimeName
        $runtimeIsReachable = $false
        try {
            & $runtimeCommand.Source info *> $null
            $runtimeIsReachable = ($LASTEXITCODE -eq 0)
        } catch {
            $runtimeIsReachable = $false
        }

        if ($runtimeIsReachable) {
            return [PSCustomObject]@{
                Name = $runtimeName
                Path = $runtimeCommand.Source
            }
        }
    }

    if ($RequestedRuntime -ne "auto") {
        if ($installedRuntimes.Count -eq 0) {
            throw "The '$RequestedRuntime' command was not found in PATH."
        }

        $recovery = if ($RequestedRuntime -eq "podman") {
            "Start the Podman machine, then retry."
        } else {
            "Start Docker Desktop or the Docker daemon, then retry."
        }
        throw "The '$RequestedRuntime' engine is not reachable. $recovery"
    }

    if ($installedRuntimes.Count -eq 0) {
        throw "Neither Docker nor Podman was found in PATH. Install Docker first, or install Podman as the fallback host engine."
    }

    throw "Neither Docker nor Podman has a reachable engine. Start Docker, or start Podman when Docker is unavailable."
}

$previousPodmanConnection = $env:CONTAINER_CONNECTION
try {
    if ($PodmanConnection) {
        $env:CONTAINER_CONNECTION = $PodmanConnection
    }

    $runtime = Resolve-HostContainerRuntime -RequestedRuntime $ContainerRuntime
    if (-not $PSCmdlet.ShouldProcess($repoRoot, "Start devcontainer with $($runtime.Name)")) {
        return
    }

    $runtimePath = $runtime.Path
    $devcontainerCommand = Get-Command devcontainer -ErrorAction SilentlyContinue
    if ($devcontainerCommand) {
        $commandPath = $devcontainerCommand.Source
        $commandArguments = @(
            "up",
            "--workspace-folder", $repoRoot,
            "--docker-path", $runtimePath
        )
    } else {
        $npxCommand = Get-Command npx.cmd -ErrorAction SilentlyContinue
        if (-not $npxCommand) {
            $npxCommand = Get-Command npx -ErrorAction SilentlyContinue
        }
        if (-not $npxCommand) {
            throw "Install the Dev Container CLI or Node.js with npx, then retry."
        }

        $commandPath = $npxCommand.Source
        $commandArguments = @(
            "--yes", "@devcontainers/cli",
            "up",
            "--workspace-folder", $repoRoot,
            "--docker-path", $runtimePath
        )
    }

    & $commandPath @commandArguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
} finally {
    if ($null -eq $previousPodmanConnection) {
        Remove-Item Env:CONTAINER_CONNECTION -ErrorAction SilentlyContinue
    } else {
        $env:CONTAINER_CONNECTION = $previousPodmanConnection
    }
}


