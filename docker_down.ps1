# Teardown Docker Services

[CmdletBinding()]
param(
    [Parameter()]
    [switch]$CleanCerts,

    [Parameter()]
    [switch]$CleanEnv,

    [Parameter()]
    [switch]$CleanAll
)

$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) { $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

$ContainersDir = Join-Path $ScriptDir "containers"
$CertsDir = Join-Path $ContainersDir "certs"

#region Docker Teardown
Write-Host "=== Docker Teardown ===" -ForegroundColor Cyan

if (Get-Command docker -ErrorAction SilentlyContinue) {
    Write-Host "Stopping and removing containers..." -ForegroundColor Yellow

    ## Teardown the vs multi-container
    docker compose -f (Join-Path $ContainersDir "docker-compose-common.yml") -p dev_common_shared down
    #docker compose -f (Join-Path $ContainersDir "docker-compose.yml") -p example down

    Write-Host "Containers removed." -ForegroundColor Green
} else {
    Write-Warning "Docker not found. Skipping container teardown."
}
#endregion

#region Cleanup Ephemeral Files
if ($CleanCerts -or $CleanAll) {
    Write-Host "`n=== Cleaning WireMock Certificates ===" -ForegroundColor Cyan

    # Remove certificate from Windows trusted root store
    Write-Host "Removing WireMock certificate from Windows trusted root store..." -ForegroundColor Yellow
    $trustedCerts = Get-ChildItem -Path Cert:\CurrentUser\Root | Where-Object { $_.Subject -like "*CN=localhost*OU=Development*" }
    if ($trustedCerts) {
        foreach ($cert in $trustedCerts) {
            try {
                Remove-Item -Path "Cert:\CurrentUser\Root\$($cert.Thumbprint)" -Force
                Write-Host "  Removed from trust store: $($cert.Thumbprint)" -ForegroundColor Gray
            } catch {
                Write-Warning "Failed to remove certificate from trust store: $_"
            }
        }
    } else {
        Write-Host "  No WireMock certificates found in trust store." -ForegroundColor Gray
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

if (-not $CleanCerts -and -not $CleanEnv -and -not $CleanAll) {
    Write-Host ""
    Write-Host "Tip: Use these flags to clean ephemeral files:" -ForegroundColor Yellow
    Write-Host "  -CleanCerts  : Remove WireMock certificates (*.pfx, *.crt, *.key, etc.)" -ForegroundColor Gray
    Write-Host "  -CleanEnv    : Remove .env file (will be regenerated on next setup)" -ForegroundColor Gray
    Write-Host "  -CleanAll    : Remove all ephemeral files (certs + .env)" -ForegroundColor Gray
}

