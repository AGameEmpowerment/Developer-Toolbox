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

# Track if .env was just created to detect potential keystore password mismatch
$EnvFileJustCreated = $false

# Create .env from .env.example if it doesn't exist
if (-not (Test-Path $EnvFile)) {
    if (Test-Path $EnvExampleFile) {
        Write-Host "Creating .env from .env.example..." -ForegroundColor Yellow
        Copy-Item $EnvExampleFile $EnvFile
        Write-Host ".env file created." -ForegroundColor Green
        $EnvFileJustCreated = $true
    } else {
        Write-Error ".env.example not found. Cannot create .env file."
        exit 1
    }
}

# Load .env values for display
$EnvValues = @{}
if (Test-Path $EnvFile) {
    Get-Content $EnvFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith('#') -and $line -match '^\s*([^=]+)=(.*)\s*$') {
            $key = $matches[1].Trim()
            $val = $matches[2].Trim()
            if ($val.StartsWith('"') -and $val.EndsWith('"')) {
                $val = $val.Substring(1, $val.Length - 2)
            }
            $EnvValues[$key] = $val
        }
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
            # Remove any existing WIREMOCK_KEYSTORE_PASSWORD entries, regardless of format
            $envLines = Get-Content $EnvFile
            $envLines = $envLines | Where-Object { $_ -notmatch '^\s*WIREMOCK_KEYSTORE_PASSWORD\s*=' }
            # Append a normalized WIREMOCK_KEYSTORE_PASSWORD line
            $envLines += "WIREMOCK_KEYSTORE_PASSWORD=`"$KeystorePassword`""
            $envContent = ($envLines -join [Environment]::NewLine) + [Environment]::NewLine
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
    # Keystore exists - check for potential password mismatch
    if ($EnvFileJustCreated) {
        Write-Host "WireMock keystore already exists, but .env was just created from .env.example." -ForegroundColor Yellow
        Write-Host "This may cause a password mismatch. Regenerating keystore to match .env password..." -ForegroundColor Yellow
        
        if (Test-Path $GenerateCertScript) {
            # Read the password from the newly created .env file
            $EnvPassword = $EnvValues['WIREMOCK_KEYSTORE_PASSWORD']
            if (-not $EnvPassword) {
                $EnvPassword = "changeit"  # Default from .env.example
            }

            # Regenerate keystore with the .env password
            & $GenerateCertScript -KeystorePassword $EnvPassword -Force

            if ($LASTEXITCODE -eq 0 -or (Test-Path $WireMockKeystore)) {
                Write-Host "WireMock keystore regenerated to match .env password." -ForegroundColor Green
            } else {
                Write-Warning "Failed to regenerate WireMock keystore. HTTPS may not work."
                Write-Warning "You can manually run: $GenerateCertScript -KeystorePassword `"$EnvPassword`" -Force"
            }
        } else {
            Write-Warning "WireMock certificate script not found. Cannot regenerate keystore."
            Write-Warning "Manual action needed: Either regenerate .env or regenerate the keystore."
        }
    } else {
        Write-Host "WireMock keystore already exists. Skipping certificate generation." -ForegroundColor Gray
    }
}

# Import certificate to Windows trusted root store for automatic trust
$WireMockCert = Join-Path $CertsDir "wiremock.crt"
if (Test-Path $WireMockCert) {
    Write-Host "Importing WireMock certificate to Windows trusted root store..." -ForegroundColor Yellow

    try {
        # Load the certificate file so we can compare its thumbprint with any existing trusted certs
        $fileCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $WireMockCert
    } catch {
        Write-Warning "Failed to load WireMock certificate from '$WireMockCert': $_"
        Write-Warning "Skipping trusted root import check. You may need to manually trust the certificate."
        $fileCert = $null
    }

    # Find any existing WireMock certificates in the CurrentUser\Root store by subject
    $existingCerts = Get-ChildItem -Path Cert:\CurrentUser\Root | Where-Object { $_.Subject -like "*CN=localhost*OU=Development*" }

    if ($fileCert -and $existingCerts) {
        # Check if any existing certificate has the same thumbprint as the file
        $matchingCert = $existingCerts | Where-Object { $_.Thumbprint -eq $fileCert.Thumbprint }
    } else {
        $matchingCert = $null
    }

    if ($matchingCert) {
        Write-Host "WireMock certificate already trusted in CurrentUser\Root store." -ForegroundColor Gray
        Write-Host "  Thumbprint: $($fileCert.Thumbprint)" -ForegroundColor Gray
    } else {
        if ($existingCerts) {
            Write-Host "Existing WireMock certificate(s) with subject 'CN=localhost, OU=Development' found with different thumbprint. Removing stale certificate(s)..." -ForegroundColor Yellow
            foreach ($old in $existingCerts) {
                try {
                    Remove-Item -Path "Cert:\CurrentUser\Root\$($old.Thumbprint)" -Force
                    Write-Host "  Removed old WireMock certificate with thumbprint $($old.Thumbprint)" -ForegroundColor Gray
                } catch {
                    Write-Warning "  Failed to remove old WireMock certificate with thumbprint $($old.Thumbprint): $_"
                }
            }
        }

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
    docker compose --env-file "$EnvFile" -f "$ContainersDir/docker-compose-common.yml" -p dev_common_shared up -d
    #docker compose --env-file "$EnvFile" -f "$ContainersDir/docker-compose.yml" -p example up -d

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Docker compose failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
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
$smtpWebPort = if ($EnvValues.ContainsKey('SMTP4DEV_WEB_PORT') -and $EnvValues['SMTP4DEV_WEB_PORT']) { $EnvValues['SMTP4DEV_WEB_PORT'] } else { '10140' }
$seqPort = if ($EnvValues.ContainsKey('SEQ_HTTP_PORT') -and $EnvValues['SEQ_HTTP_PORT']) { $EnvValues['SEQ_HTTP_PORT'] } else { '10150' }
Write-Host "  SMTP4Dev:      http://localhost:$smtpWebPort" -ForegroundColor Gray
Write-Host "  Seq (OTEL):    http://localhost:$seqPort" -ForegroundColor Gray
Write-Host "  WireMock HTTP: http://localhost:10080" -ForegroundColor Gray
Write-Host "  WireMock HTTPS: https://localhost:10443" -ForegroundColor Gray
Write-Host "  Azurite:       localhost:10000-10002" -ForegroundColor Gray
$serviceBusPort = if ($EnvValues.ContainsKey('SERVICEBUS_AMQP_PORT') -and $EnvValues['SERVICEBUS_AMQP_PORT']) { $EnvValues['SERVICEBUS_AMQP_PORT'] } else { '10170' }
Write-Host "  Service Bus:   localhost:$serviceBusPort" -ForegroundColor Gray
Write-Host ""
Write-Host "Head back to README.md for deployment of the database and other services..."

