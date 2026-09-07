#Requires -Version 5.1
<#
.SYNOPSIS
Gets the Developer Toolbox and starts its shared local service stack.

.DESCRIPTION
Clones or updates AGameEmpowerment/Developer-Toolbox beside the consuming repository,
then runs docker_setup.ps1 from that checkout. Set DEVELOPER_TOOLBOX_ROOT or pass
-ToolboxPath to reuse a checkout elsewhere.

.PARAMETER ToolboxPath
Where to find or clone the Toolbox. Defaults to DEVELOPER_TOOLBOX_ROOT, then an
Developer-Toolbox directory beside the consuming repository.

.PARAMETER Branch
The Toolbox branch to clone or fast-forward. Defaults to main.

.PARAMETER ContainerRuntime
The container runtime passed to docker_setup.ps1. Auto prefers a reachable Docker
engine and falls back to Podman.

.PARAMETER SkipSetup
Clone or update the Toolbox without starting its service stack.

.PARAMETER NoUpdate
Use an existing Toolbox checkout without fetching updates.

.EXAMPLE
./setup/_toolbox.ps1

.EXAMPLE
./setup/_toolbox.ps1 -ContainerRuntime podman

.OUTPUTS
None.
#>
[CmdletBinding()]
param(
    [Parameter()]
    [string]$ToolboxPath,

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$Branch = "main",

    [Parameter()]
    [ValidateSet("auto", "docker", "podman")]
    [string]$ContainerRuntime = "auto",

    [Parameter()]
    [switch]$SkipSetup,

    [Parameter()]
    [switch]$NoUpdate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$toolboxRepository = "AGameEmpowerment/Developer-Toolbox"
$toolboxHttpsUrl = "https://github.com/$toolboxRepository.git"
$consumerRepositoryRoot = Split-Path -Parent $PSScriptRoot

function Test-CommandAvailable {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Test-GitHubCliAuthenticated {
    if (-not (Test-CommandAvailable -Name "gh")) {
        return $false
    }

    try {
        & gh auth status --hostname github.com *> $null
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

function Test-ToolboxOriginUrl {
    param(
        [Parameter(Mandatory)]
        [string]$RemoteUrl
    )

    $normalizedUrl = $RemoteUrl.Trim().TrimEnd("/")
    return $normalizedUrl -match '^(https://github\.com/|git@github\.com:|ssh://git@github\.com/)AGameEmpowerment/Developer-Toolbox(?:\.git)?$'
}

if (-not (Test-CommandAvailable -Name "git")) {
    throw "Git is required but was not found on PATH."
}

if (-not $ToolboxPath) {
    if ($env:DEVELOPER_TOOLBOX_ROOT) {
        $ToolboxPath = $env:DEVELOPER_TOOLBOX_ROOT
    } else {
        $ToolboxPath = Join-Path (Split-Path -Parent $consumerRepositoryRoot) "Developer-Toolbox"
    }
}
$ToolboxPath = [System.IO.Path]::GetFullPath($ToolboxPath)

$gitHubCliAuthenticated = Test-GitHubCliAuthenticated
$gitAuthenticationArguments = @()
if ($gitHubCliAuthenticated) {
    $gitAuthenticationArguments = @(
        "-c", "credential.helper=",
        "-c", "credential.helper=!gh auth git-credential"
    )
}

$existingToolboxRoot = $null
if (Test-Path -LiteralPath $ToolboxPath -PathType Container) {
    try {
        $existingToolboxRoot = (& git -C $ToolboxPath rev-parse --show-toplevel 2>$null | Out-String).Trim()
    } catch {
        $existingToolboxRoot = $null
    }
}

if ($existingToolboxRoot) {
    if ([System.IO.Path]::GetFullPath($existingToolboxRoot) -ne $ToolboxPath) {
        throw "Toolbox path '$ToolboxPath' is not the root of its Git repository."
    }

    $originUrl = (& git -C $ToolboxPath remote get-url origin 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $originUrl) {
        throw "Toolbox checkout '$ToolboxPath' does not have an origin remote."
    }
    if (-not (Test-ToolboxOriginUrl -RemoteUrl $originUrl)) {
        throw "Toolbox checkout '$ToolboxPath' has unexpected origin '$originUrl'. Expected $toolboxHttpsUrl."
    }

    Write-Output "Using existing Toolbox checkout at $ToolboxPath"
    if (-not $NoUpdate) {
        & git @gitAuthenticationArguments -C $ToolboxPath fetch origin --prune
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Could not fetch Toolbox updates. Continuing with the existing checkout."
        } else {
            $currentBranch = (& git -C $ToolboxPath rev-parse --abbrev-ref HEAD | Out-String).Trim()
            if ($currentBranch -eq $Branch) {
                & git -C $ToolboxPath merge --ff-only "origin/$Branch"
                if ($LASTEXITCODE -ne 0) {
                    Write-Warning "Could not fast-forward '$Branch'. Continuing with the existing checkout."
                }
            } else {
                Write-Warning "Toolbox is on '$currentBranch', not '$Branch'. Continuing without changing branches."
            }
        }
    }
} elseif (Test-Path -LiteralPath $ToolboxPath) {
    throw "Toolbox path '$ToolboxPath' exists but is not a Git repository."
} else {
    Write-Output "Cloning $toolboxRepository ($Branch) to $ToolboxPath..."
    $cloneSucceeded = $false
    if ($gitHubCliAuthenticated) {
        & gh repo clone $toolboxRepository $ToolboxPath -- --branch $Branch
        $cloneSucceeded = ($LASTEXITCODE -eq 0)
    }

    if (-not $cloneSucceeded) {
        & git @gitAuthenticationArguments clone --branch $Branch $toolboxHttpsUrl $ToolboxPath
        if ($LASTEXITCODE -ne 0) {
            throw "Toolbox clone failed. Verify GitHub connectivity and authentication, then retry."
        }
    }
}

if ($SkipSetup) {
    Write-Output "Toolbox is ready at $ToolboxPath. Service startup was skipped."
    return
}

$setupScript = Join-Path $ToolboxPath "docker_setup.ps1"
if (-not (Test-Path -LiteralPath $setupScript -PathType Leaf)) {
    throw "Toolbox setup script '$setupScript' was not found."
}

Write-Output "Starting the Toolbox service stack with runtime '$ContainerRuntime'..."
Push-Location $ToolboxPath
try {
    & $setupScript -ContainerRuntime $ContainerRuntime
    if (-not $?) {
        throw "Toolbox setup failed."
    }
} finally {
    Pop-Location
}

Write-Output "Toolbox service stack is running."
Write-Output "Stop it with: pwsh -File '$ToolboxPath/docker_down.ps1' -ContainerRuntime $ContainerRuntime"

