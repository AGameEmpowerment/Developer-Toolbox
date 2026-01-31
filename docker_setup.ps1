# Setup Docker Services

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) { $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

$ContainersDir = Join-Path $ScriptDir "containers"
$CertsDir = Join-Path $ContainersDir "certs"
$EnvFile = Join-Path $ContainersDir ".env"
$EnvExampleFile = Join-Path $ContainersDir ".env.example"

#region Environment Setup
Write-Host "=== Environment Setup ===" -ForegroundColor Cyan

# Create .env from .env.example if it doesn't exist
if (-not (Test-Path $EnvFile)) {
    if (Test-Path $EnvExampleFile) {
        Write-Host "Creating .env from .env.example..." -ForegroundColor Yellow
        Copy-Item $EnvExampleFile $EnvFile
        Write-Host ".env file created." -ForegroundColor Green
    } else {
        Write-Error ".env.example not found. Cannot create .env file."
        exit 1
    }
}
#endregion

#region WireMock Certificate Setup
Write-Host "`n=== WireMock Certificate Setup ===" -ForegroundColor Cyan

$WireMockKeystore = Join-Path $CertsDir "wiremock.jks"
$GenerateCertScript = Join-Path $CertsDir "Generate-WireMockCert.ps1"

if (-not (Test-Path $WireMockKeystore)) {
    if (Test-Path $GenerateCertScript) {
        Write-Host "Generating WireMock TLS certificate..." -ForegroundColor Yellow

        # Generate a random password for the keystore
        $KeystorePassword = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 24 | ForEach-Object { [char]$_ })

        # Run the certificate generation script
        & $GenerateCertScript -KeystorePassword $KeystorePassword -Force

        if ($LASTEXITCODE -eq 0 -or (Test-Path $WireMockKeystore)) {
            # Update .env with the generated password
            $envContent = Get-Content $EnvFile -Raw
            if ($envContent -match 'WIREMOCK_KEYSTORE_PASSWORD=') {
                $envContent = $envContent -replace 'WIREMOCK_KEYSTORE_PASSWORD="[^"]*"', "WIREMOCK_KEYSTORE_PASSWORD=`"$KeystorePassword`""
            } else {
                $envContent += "`nWIREMOCK_KEYSTORE_PASSWORD=`"$KeystorePassword`"`n"
            }
            Set-Content $EnvFile $envContent -NoNewline
            Write-Host "WireMock certificate generated and .env updated." -ForegroundColor Green
        } else {
            Write-Warning "WireMock certificate generation failed. HTTPS may not work."
            Write-Warning "You can manually run: $GenerateCertScript"
        }
    } else {
        Write-Warning "WireMock certificate script not found at: $GenerateCertScript"
    }
} else {
    Write-Host "WireMock keystore already exists. Skipping certificate generation." -ForegroundColor Gray
}

# Import certificate to Windows trusted root store for automatic trust
$WireMockCert = Join-Path $CertsDir "wiremock.crt"
if (Test-Path $WireMockCert) {
    Write-Host "Importing WireMock certificate to Windows trusted root store..." -ForegroundColor Yellow

    # Check if already imported
    $existingCert = Get-ChildItem -Path Cert:\CurrentUser\Root | Where-Object { $_.Subject -like "*CN=localhost*OU=Development*" }

    if ($existingCert) {
        Write-Host "Certificate already trusted in CurrentUser\Root store." -ForegroundColor Gray
    } else {
        try {
            $cert = Import-Certificate -FilePath $WireMockCert -CertStoreLocation Cert:\CurrentUser\Root
            Write-Host "Certificate imported to CurrentUser\Root store." -ForegroundColor Green
            Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
        } catch {
            Write-Warning "Failed to import certificate to trusted store: $_"
            Write-Warning "You may need to run as Administrator or manually trust the certificate."
            Write-Host "  Manual import: Import-Certificate -FilePath '$WireMockCert' -CertStoreLocation Cert:\CurrentUser\Root" -ForegroundColor Gray
        }
    }
} else {
    Write-Warning "WireMock certificate not found. HTTPS clients may not trust the server."
}
#endregion

#region Docker Services
Write-Host "`n=== Docker Services ===" -ForegroundColor Cyan

if (Get-Command docker -ErrorAction SilentlyContinue) {
    Write-Host "Starting Docker containers..." -ForegroundColor Yellow

    ## Start the vs multi-container
    docker compose -f "./containers/docker-compose-common.yml" -p dev_common_shared up -d
    #docker compose -f "./containers/docker-compose.yml" -p example up -d

    Write-Host "Docker containers started." -ForegroundColor Green
} else {
    Write-Error "Docker is not installed or not in PATH."
    exit 1
}
#endregion

Write-Host "`n=== Setup Complete ===" -ForegroundColor Green
Write-Host "Services available:" -ForegroundColor White
Write-Host "  SQL Server:    localhost:10433" -ForegroundColor Gray
Write-Host "  CosmosDB:      localhost:10081" -ForegroundColor Gray
Write-Host "  Redis:         localhost:10120" -ForegroundColor Gray
Write-Host "  SMTP4Dev:      http://localhost:10140" -ForegroundColor Gray
Write-Host "  Seq (OTEL):    http://localhost:10150" -ForegroundColor Gray
Write-Host "  WireMock HTTP: http://localhost:10080" -ForegroundColor Gray
Write-Host "  WireMock HTTPS: https://localhost:10443" -ForegroundColor Gray
Write-Host "  Azurite:       localhost:10000-10002" -ForegroundColor Gray
Write-Host "  Service Bus:   localhost:10170" -ForegroundColor Gray
Write-Host ""
Write-Host "Head back to README.md for deployment of the database and other services..."

