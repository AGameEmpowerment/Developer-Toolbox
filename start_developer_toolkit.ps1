#Requires -Version 5.1
<#
.SYNOPSIS
Starts the local emulator stack from inside this repository's devcontainer.

.DESCRIPTION
Repository-root convenience entry point. The devcontainer launcher validates
that the command is running inside a devcontainer or GitHub Codespace before it
starts any containers.

.PARAMETER ContainerRuntime
Selects Docker, Podman, or automatic runtime detection.

.EXAMPLE
./start_developer_toolkit.ps1

.EXAMPLE
./start_developer_toolkit.ps1 -ContainerRuntime podman

.EXAMPLE
./start_developer_toolkit.ps1 -WhatIf

.OUTPUTS
None.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter()]
    [ValidateSet("auto", "docker", "podman")]
    [string]$ContainerRuntime = $(if ($env:CONTAINER_RUNTIME) { $env:CONTAINER_RUNTIME } else { "auto" })
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$launcher = Join-Path $PSScriptRoot ".devcontainer/start_developer_toolkit.ps1"
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw "Developer toolkit launcher was not found at '$launcher'."
}

if (-not $PSCmdlet.ShouldProcess("local emulator stack", "Start with $ContainerRuntime container runtime")) {
    return
}

& $launcher -ContainerRuntime $ContainerRuntime -Confirm:$false
if (-not $?) {
    throw "Developer toolkit launcher failed."
}


