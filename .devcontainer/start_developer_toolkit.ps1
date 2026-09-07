<#
.SYNOPSIS
Starts the local emulator stack inside the devcontainer.

.DESCRIPTION
Runs the repository's docker_setup.ps1 script when invoked from the Toolbox
checkout. In a repository installed with Install-ToolboxSetup.ps1 or
install_toolbox_setup.sh, delegates to setup_toolbox.ps1 instead. The
devcontainer installs Docker-in-Docker by default, so Docker is normally
selected. Podman can be selected when a Podman runtime with Compose support is
available instead.

The delegated setup validates the selected runtime and its Compose support, creates containers/.env
from containers/.env.example when needed, prepares the WireMock development
certificate, and starts the services in containers/docker-compose-common.yml.

This script does not install Docker, start during container creation, import external
resources, or delete existing container data. Port forwarding remains controlled by
.devcontainer/devcontainer.json.

.EXAMPLE
pwsh -File ./.devcontainer/start_developer_toolkit.ps1

.EXAMPLE
pwsh -File ./.devcontainer/start_developer_toolkit.ps1 -WhatIf

Shows the startup action without creating or starting containers.

.EXAMPLE
pwsh -File ./.devcontainer/start_developer_toolkit.ps1 -ContainerRuntime podman

Starts the toolkit with Podman when Podman is installed inside the devcontainer.

.PARAMETER ContainerRuntime
Selects Docker, Podman, or the first installed and reachable runtime. The default is
auto, or the value of the CONTAINER_RUNTIME environment variable.

.OUTPUTS
None.

.NOTES
Run this command from a terminal attached to the devcontainer. In an installed
consumer repository, the Toolbox launcher prints the matching stop command.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter()]
    [ValidateSet("auto", "docker", "podman")]
    [string]$ContainerRuntime = $(if ($env:CONTAINER_RUNTIME) { $env:CONTAINER_RUNTIME } else { "auto" })
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-DevContainerEnvironment {
    foreach ($variableName in @("DEVCONTAINER", "REMOTE_CONTAINERS", "CODESPACES")) {
        $value = [Environment]::GetEnvironmentVariable($variableName)
        if ($value -match '(?i)^(true|1)$') {
            return $true
        }
    }

    return $false
}

if (-not (Test-DevContainerEnvironment)) {
    throw "This command must be run from inside a devcontainer or GitHub Codespace. Open the repository in its devcontainer, then retry."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolboxLauncher = Join-Path $repoRoot "setup_toolbox.ps1"
$setupScript = Join-Path $repoRoot "docker_setup.ps1"

if (-not (Test-Path -LiteralPath $setupScript -PathType Leaf) -and
    -not (Test-Path -LiteralPath $toolboxLauncher -PathType Leaf)) {
    throw "Neither '$setupScript' nor '$toolboxLauncher' was found."
}

if (-not $PSCmdlet.ShouldProcess("local emulator stack", "Start with $ContainerRuntime container runtime")) {
    return
}

Push-Location $repoRoot
try {
    if (Test-Path -LiteralPath $toolboxLauncher -PathType Leaf) {
        & $toolboxLauncher -ContainerRuntime $ContainerRuntime
    } else {
        & $setupScript -ContainerRuntime $ContainerRuntime
    }
    if (-not $?) {
        throw "The local emulator stack setup script failed."
    }
} finally {
    Pop-Location
}


