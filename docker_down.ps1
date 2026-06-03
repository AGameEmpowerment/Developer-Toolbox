# Teardown Docker Services

[CmdletBinding()]
param(
    [Parameter()]
    [switch]$CleanCerts,

    [Parameter()]
    [switch]$CleanEnv,

    [Parameter()]
    [switch]$CleanVolumes,

    [Parameter()]
    [switch]$CleanAll
)

$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) { $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

$ContainersDir = Join-Path $ScriptDir "containers"
$CertsDir = Join-Path $ContainersDir "certs"
$isWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows
)

#region Docker Teardown
Write-Host "=== Docker Teardown ===" -ForegroundColor Cyan

if (Get-Command docker -ErrorAction SilentlyContinue) {
    Write-Host "Stopping and removing containers..." -ForegroundColor Yellow

    ## Teardown the shared development collection.
    docker compose `
        -f (Join-Path $ContainersDir "docker-compose-common.yml") `
        -p dev_common_shared `
        down --remove-orphans

    ## Safety net: remove any leftover resources still labeled with this compose project.
    $projectNames = @("dev_common_shared")

    foreach ($projectName in $projectNames) {
        $containerIds = @()
        try {
            $containerIds = @(docker ps -aq --filter "label=com.docker.compose.project=$projectName")
        } catch {
            Write-Warning "Failed to query containers for project '$projectName': $($_.Exception.Message)"
        }

        if ($containerIds -and $containerIds.Count -gt 0) {
            Write-Host "Removing leftover containers for project '$projectName'..." -ForegroundColor Yellow
            docker rm -f $containerIds | Out-Null
        }

        $networkIds = @()
        try {
            $networkIds = @(docker network ls -q --filter "label=com.docker.compose.project=$projectName")
        } catch {
            Write-Warning "Failed to query networks for project '$projectName': $($_.Exception.Message)"
        }

        if ($networkIds -and $networkIds.Count -gt 0) {
            Write-Host "Removing leftover networks for project '$projectName'..." -ForegroundColor Yellow
            docker network rm $networkIds | Out-Null
        }

        if ($CleanVolumes) {
            $volumeIds = @()
            try {
                $volumeIds = @(docker volume ls -q --filter "label=com.docker.compose.project=$projectName")
            } catch {
                Write-Warning "Failed to query volumes for project '$projectName': $($_.Exception.Message)"
            }

            if ($volumeIds -and $volumeIds.Count -gt 0) {
                Write-Host "Removing leftover volumes for project '$projectName'..." -ForegroundColor Yellow
                docker volume rm $volumeIds | Out-Null
            }
        }
    }

    Write-Host "Containers removed." -ForegroundColor Green
} else {
    Write-Warning "Docker not found. Skipping container teardown."
}
#endregion

#region Cleanup Ephemeral Files
if ($CleanCerts -or $CleanAll) {
    Write-Host "`n=== Cleaning WireMock Certificates ===" -ForegroundColor Cyan

    # Remove certificate from Windows trusted root store
    if ($isWindowsPlatform) {
        Write-Host "Removing WireMock certificate from Windows trusted root store..." -ForegroundColor Yellow
        try {
            $store = [System.Security.Cryptography.X509Certificates.X509Store]::new("Root", "CurrentUser")
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)

            $trustedCerts = $store.Certificates | Where-Object { $_.Subject -like "*CN=localhost*OU=Development*" }
            if ($trustedCerts -and $trustedCerts.Count -gt 0) {
                foreach ($cert in $trustedCerts) {
                    $store.Remove($cert)
                    Write-Host "  Removed from trust store: $($cert.Thumbprint)" -ForegroundColor Gray
                }
            } else {
                Write-Host "  No WireMock certificates found in trust store." -ForegroundColor Gray
            }
        } catch {
            Write-Warning "Failed to remove certificate from trust store: $($_.Exception.Message)"
        } finally {
            if ($store) {
                $store.Close()
            }
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

if ($CleanAll -and -not $CleanVolumes) {
    Write-Host "Persistent Docker named volumes were preserved. Use -CleanVolumes if you need to remove stored emulator data." -ForegroundColor Yellow
}

if (-not $CleanCerts -and -not $CleanEnv -and -not $CleanVolumes -and -not $CleanAll) {
    Write-Host ""
    Write-Host "Tip: Use these flags to clean ephemeral files:" -ForegroundColor Yellow
    Write-Host "  -CleanCerts  : Remove WireMock certificates (*.pfx, *.crt, *.key, etc.)" -ForegroundColor Gray
    Write-Host "  -CleanEnv    : Remove .env file (will be regenerated on next setup)" -ForegroundColor Gray
    Write-Host "  -CleanVolumes: Remove Docker named volumes for the compose project" -ForegroundColor Gray
    Write-Host "  -CleanAll    : Remove ephemeral files while preserving Docker named volumes" -ForegroundColor Gray
}

