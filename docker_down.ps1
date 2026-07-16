# Teardown Container Services

[CmdletBinding()]
param(
    [Parameter()]
    [switch]$CleanCerts,

    [Parameter()]
    [switch]$CleanEnv,

    [Parameter()]
    [switch]$CleanVolumes,

    [Parameter()]
    [switch]$CleanAll,

    [Parameter()]
    [switch]$Force,

    [Parameter()]
    [ValidateSet("auto", "docker", "podman")]
    [string]$ContainerRuntime = $(if ($env:CONTAINER_RUNTIME) { $env:CONTAINER_RUNTIME } else { "auto" })
)

if ($PSVersionTable.PSVersion -lt [version]"5.1") {
    Write-Error "Missing dependency: PowerShell 5.1 or later is required. Current version: $($PSVersionTable.PSVersion)."
    exit 1
}

$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) { $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

$ContainersDir = Join-Path $ScriptDir "containers"
$CertsDir = Join-Path $ContainersDir "certs"
$isWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows
)

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

        return $null
    }

    if (Get-Command $RequestedRuntime -ErrorAction SilentlyContinue) {
        return $RequestedRuntime
    }

    Write-Error "Missing dependency: '$RequestedRuntime' was not found in PATH. Install $RequestedRuntime, make sure its CLI is available in PATH, then open a new terminal."
    exit 1
}

function Test-ContainerRuntimeReady {
    param(
        [Parameter(Mandatory)]
        [string]$ContainerCli
    )

    & $ContainerCli info *> $null
    return ($LASTEXITCODE -eq 0)
}

function Get-ContainerRuntimeUnavailableMessage {
    param(
        [Parameter(Mandatory)]
        [string]$ContainerCli,

        [Parameter(Mandatory)]
        [string]$RuntimeDisplayName
    )

    if ($ContainerCli -eq "docker") {
        return "$RuntimeDisplayName CLI is installed, but the Docker engine is not reachable. Start Docker Desktop, wait until it finishes starting, then rerun this script."
    }

    return "$RuntimeDisplayName CLI is installed, but the Podman engine is not reachable. Start the Podman machine with 'podman machine start', then rerun this script."
}

function Test-ContainerComposeAvailable {
    param(
        [Parameter(Mandatory)]
        [string]$ContainerCli
    )

    & $ContainerCli compose version *> $null
    return ($LASTEXITCODE -eq 0)
}

if (($CleanVolumes -or $CleanAll) -and -not $Force) {
    Write-Error "Destructive cleanup requires -Force. Re-run with -CleanVolumes -Force or -CleanAll -Force to remove container named volumes."
    exit 1
}

#region Container Teardown
$ContainerCli = Resolve-ContainerRuntime -RequestedRuntime $ContainerRuntime
$RuntimeDisplayName = if ($ContainerCli) { (Get-Culture).TextInfo.ToTitleCase($ContainerCli) } else { "Container" }

if ($ContainerCli -and -not (Test-ContainerRuntimeReady -ContainerCli $ContainerCli)) {
    $runtimeUnavailableMessage = Get-ContainerRuntimeUnavailableMessage -ContainerCli $ContainerCli -RuntimeDisplayName $RuntimeDisplayName

    if ($CleanVolumes -or $CleanAll) {
        Write-Error $runtimeUnavailableMessage
        exit 1
    }

    Write-Warning "$runtimeUnavailableMessage Skipping container teardown."
    $ContainerCli = $null
    $RuntimeDisplayName = "Container"
}

if (($CleanVolumes -or $CleanAll) -and -not $ContainerCli) {
    Write-Error "Missing dependency: Docker or Podman must be installed and running to remove container named volumes."
    exit 1
}

Write-Host "=== $RuntimeDisplayName Teardown ===" -ForegroundColor Cyan

if ($ContainerCli) {
    Write-Host "Stopping and removing containers..." -ForegroundColor Yellow

    ## Teardown the shared development collection.
    if (Test-ContainerComposeAvailable -ContainerCli $ContainerCli) {
        & $ContainerCli compose `
            -f (Join-Path $ContainersDir "docker-compose-common.yml") `
            -p dev_common_shared `
            down --remove-orphans
        $composeExitCode = $LASTEXITCODE
        if ($composeExitCode -ne 0) {
            Write-Warning "$RuntimeDisplayName compose down failed with exit code $composeExitCode. Continuing with leftover resource cleanup."
        }
    } else {
        Write-Warning "Missing dependency: $RuntimeDisplayName compose support is not available. Skipping compose down and continuing with leftover resource cleanup."
    }

    ## Safety net: remove any leftover resources still labeled with this compose project.
    $projectNames = @("dev_common_shared")
    $composeProjectLabelNames = @("com.docker.compose.project", "io.podman.compose.project")

    foreach ($projectName in $projectNames) {
        $containerIds = @()
        foreach ($labelName in $composeProjectLabelNames) {
            try {
                $containerIds += @(& $ContainerCli ps -aq --filter "label=$labelName=$projectName")
            } catch {
                Write-Warning "Failed to query containers for project '$projectName' using label '$labelName': $($_.Exception.Message)"
            }
        }
        $containerIds = @($containerIds | Where-Object { $_ } | Select-Object -Unique)

        if ($containerIds -and $containerIds.Count -gt 0) {
            Write-Host "Removing leftover containers for project '$projectName'..." -ForegroundColor Yellow
            & $ContainerCli rm -f $containerIds | Out-Null
        }

        $networkIds = @()
        foreach ($labelName in $composeProjectLabelNames) {
            try {
                $networkIds += @(& $ContainerCli network ls -q --filter "label=$labelName=$projectName")
            } catch {
                Write-Warning "Failed to query networks for project '$projectName' using label '$labelName': $($_.Exception.Message)"
            }
        }
        $networkIds = @($networkIds | Where-Object { $_ } | Select-Object -Unique)

        if ($networkIds -and $networkIds.Count -gt 0) {
            Write-Host "Removing leftover networks for project '$projectName'..." -ForegroundColor Yellow
            & $ContainerCli network rm $networkIds | Out-Null
        }

        if ($CleanVolumes -or $CleanAll) {
            $volumeIds = @()
            foreach ($labelName in $composeProjectLabelNames) {
                try {
                    $volumeIds += @(& $ContainerCli volume ls -q --filter "label=$labelName=$projectName")
                } catch {
                    Write-Warning "Failed to query volumes for project '$projectName' using label '$labelName': $($_.Exception.Message)"
                }
            }
            $volumeIds = @($volumeIds | Where-Object { $_ } | Select-Object -Unique)

            if ($volumeIds -and $volumeIds.Count -gt 0) {
                Write-Host "Removing leftover volumes for project '$projectName'..." -ForegroundColor Yellow
                & $ContainerCli volume rm $volumeIds | Out-Null
            }
        }
    }

    Write-Host "Containers removed." -ForegroundColor Green
} else {
    Write-Warning "Docker or Podman not found. Skipping container teardown."
}
#endregion

#region Cleanup Ephemeral Files
if ($CleanCerts -or $CleanAll) {
    Write-Host "`n=== Cleaning WireMock Certificates ===" -ForegroundColor Cyan

    # Remove certificate from Windows trusted root store
    if ($isWindowsPlatform) {
        Write-Host "Removing WireMock certificate from Windows trusted root store..." -ForegroundColor Yellow
        try {
            $trustedCerts = @(Get-ChildItem -Path Cert:\CurrentUser\Root | Where-Object { $_.Subject -like "*CN=localhost*OU=Development*" })
            if ($trustedCerts.Count -gt 0) {
                foreach ($cert in $trustedCerts) {
                    $removed = $false
                    try {
                        Remove-Item -Path "Cert:\CurrentUser\Root\$($cert.Thumbprint)" -Force
                        $removed = $true
                    } catch {
                        if (Get-Command certutil.exe -ErrorAction SilentlyContinue) {
                            & certutil.exe -user -delstore Root $cert.Thumbprint *> $null
                            $removed = ($LASTEXITCODE -eq 0)
                        }

                        if (-not $removed) {
                            Write-Warning "  Failed to remove WireMock certificate with thumbprint $($cert.Thumbprint): $($_.Exception.Message)"
                        }
                    }

                    if ($removed) {
                        Write-Host "  Removed from trust store: $($cert.Thumbprint)" -ForegroundColor Gray
                    }
                }
            } else {
                Write-Host "  No WireMock certificates found in trust store." -ForegroundColor Gray
            }
        } catch {
            Write-Warning "Failed to remove certificate from trust store: $($_.Exception.Message)"
        }
    } else {
        Write-Host "  Skipping trust store cleanup (Windows only)." -ForegroundColor Gray
    }

    # Remove certificate files
    $certFiles = @(
        (Join-Path $CertsDir "wiremock.pfx"),
        (Join-Path $CertsDir "wiremock.crt"),
        (Join-Path $CertsDir "wiremock.key"),
        (Join-Path $CertsDir "wiremock.conf"),
        (Join-Path $CertsDir "wiremock.jks"),
        (Join-Path $CertsDir "truststore.jks")
    )

    foreach ($file in $certFiles) {
        if (Test-Path $file) {
            Remove-Item $file -Force
            Write-Host "  Removed: $(Split-Path $file -Leaf)" -ForegroundColor Gray
        }
    }
    Write-Host "Certificate files cleaned." -ForegroundColor Green
}

if ($CleanEnv -or $CleanAll) {
    Write-Host "`n=== Cleaning Environment File ===" -ForegroundColor Cyan

    $envFile = Join-Path $ContainersDir ".env"
    if (Test-Path $envFile) {
        Remove-Item $envFile -Force
        Write-Host "  Removed: .env" -ForegroundColor Gray
    }
    Write-Host "Environment file cleaned." -ForegroundColor Green
}
#endregion

Write-Host "`n=== Teardown Complete ===" -ForegroundColor Green

if (-not $CleanCerts -and -not $CleanEnv -and -not $CleanVolumes -and -not $CleanAll) {
    Write-Host ""
    Write-Host "Tip: Use these flags to clean ephemeral files:" -ForegroundColor Yellow
    Write-Host "  -CleanCerts  : Remove WireMock certificates (*.pfx, *.crt, *.key, etc.)" -ForegroundColor Gray
    Write-Host "  -CleanEnv    : Remove .env file (will be regenerated on next setup)" -ForegroundColor Gray
    Write-Host "  -CleanVolumes -Force: Remove container named volumes for the compose project" -ForegroundColor Gray
    Write-Host "  -CleanAll -Force    : Remove all ephemeral files and container named volumes" -ForegroundColor Gray
    Write-Host "  -ContainerRuntime podman: Use Podman instead of Docker" -ForegroundColor Gray
}

