<#
.SYNOPSIS
    Generates a self-signed certificate and PKCS12 keystore for WireMock HTTPS support.

.DESCRIPTION
    This script creates a self-signed certificate and PKCS12 keystore for local
    WireMock HTTPS development using OpenSSL. It also exports the public certificate (.crt)
    for client trust.

    Prerequisites:
    - OpenSSL must be installed and available in PATH
    - For Windows: Install via Chocolatey (choco install openssl) or download from https://slproweb.com/products/Win32OpenSSL.html
    - For Git Bash users: OpenSSL typically comes bundled with Git for Windows

.PARAMETER KeystorePassword
    Password for the keystore. Default: "changeit"

.PARAMETER ValidityDays
    Certificate validity in days. Default: 3650 (10 years)

.PARAMETER Force
    Overwrite existing keystore and certificate files without prompting.

.EXAMPLE
    .\Generate-WireMockCert.ps1
    Creates keystore with default password "changeit"

.EXAMPLE
    .\Generate-WireMockCert.ps1 -KeystorePassword "mypassword" -Force
    Creates keystore with custom password, overwriting existing files

.NOTES
    After generation, update your .env file with:
    WIREMOCK_KEYSTORE_PASSWORD=<your-password>

    For .NET client trust, you may need to import wiremock.crt into the Windows certificate store
    or configure HttpClientHandler to trust the certificate.
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$KeystorePassword = "changeit",

    [Parameter()]
    [int]$ValidityDays = 3650,

    [Parameter()]
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Get script directory
$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) {
    $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

# Output paths
$KeystorePath = Join-Path $ScriptDir "wiremock.pfx"
$PrivateKeyPath = Join-Path $ScriptDir "wiremock.key"
$CertPath = Join-Path $ScriptDir "wiremock.crt"
$ConfigPath = Join-Path $ScriptDir "wiremock.conf"

# Check for existing files
if (-not $Force) {
    if (Test-Path $KeystorePath) {
        $response = Read-Host "Certificate files already exist in '$ScriptDir'. Overwrite? (y/N)"
        if ($response -ne 'y' -and $response -ne 'Y') {
            Write-Host "Aborted. Use -Force to overwrite without prompting." -ForegroundColor Yellow
            exit 0
        }
    }
}

# Find openssl
$openssl = Get-Command openssl -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source

if (-not $openssl) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  OpenSSL Not Found" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "OpenSSL is required but not installed on this system." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Installation instructions:" -ForegroundColor White
    Write-Host ""
    Write-Host "Windows (Chocolatey - Recommended):" -ForegroundColor Cyan
    Write-Host "  1. Install Chocolatey if not already installed:" -ForegroundColor Gray
    Write-Host "     powershell -Command `"Set-ExecutionPolicy Bypass -Scope Process -Force; [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072; iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))`"" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  2. Install OpenSSL:" -ForegroundColor Gray
    Write-Host "     choco install openssl -y" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  3. Restart PowerShell" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Windows (Direct Download):" -ForegroundColor Cyan
    Write-Host "  1. Download from: https://slproweb.com/products/Win32OpenSSL.html" -ForegroundColor Gray
    Write-Host "     (Choose 'Win64 OpenSSL Light' for most users)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  2. Run the installer and add OpenSSL to PATH during installation" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  3. Restart PowerShell" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Git Bash Users:" -ForegroundColor Cyan
    Write-Host "  OpenSSL comes bundled with Git for Windows. Run this script from Git Bash:" -ForegroundColor Gray
    Write-Host "     bash ./Generate-WireMockCert.sh" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    exit 1
}

Write-Host "Using OpenSSL: $openssl" -ForegroundColor Cyan

# Remove existing files if present
if (Test-Path $PrivateKeyPath) {
    Remove-Item $PrivateKeyPath -Force
    Write-Host "Removed existing private key." -ForegroundColor Gray
}
if (Test-Path $KeystorePath) {
    Remove-Item $KeystorePath -Force
    Write-Host "Removed existing keystore." -ForegroundColor Gray
}
if (Test-Path $CertPath) {
    Remove-Item $CertPath -Force
    Write-Host "Removed existing certificate." -ForegroundColor Gray
}
if (Test-Path $ConfigPath) {
    Remove-Item $ConfigPath -Force
}

Write-Host ""
Write-Host "Generating self-signed certificate with OpenSSL..." -ForegroundColor Cyan

# Create OpenSSL config for SAN (Subject Alternative Names)
$opensslConfig = @"
[req]
default_bits = 2048
prompt = no
default_md = sha256
distinguished_name = dn
req_extensions = v3_req

[dn]
CN=localhost
OU=Development
O=Local
L=Local
ST=UT
C=US

[v3_req]
subjectAltName = DNS:localhost,DNS:wiremock,DNS:host.docker.internal,IP:127.0.0.1
"@

Set-Content -Path $ConfigPath -Value $opensslConfig -Encoding UTF8

# Generate private key and self-signed certificate in one step
$genArgs = @(
    "req",
    "-new",
    "-x509",
    "-newkey", "rsa:2048",
    "-keyout", $PrivateKeyPath,
    "-out", $CertPath,
    "-days", $ValidityDays.ToString(),
    "-nodes",
    "-config", $ConfigPath,
    "-extensions", "v3_req"
)

& $openssl @genArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to generate certificate. Exit code: $LASTEXITCODE"
    exit 1
}

Write-Host "Certificate created: $CertPath" -ForegroundColor Green
Write-Host "Private key created: $PrivateKeyPath" -ForegroundColor Green

# Create PKCS12 keystore from certificate and key
Write-Host ""
Write-Host "Creating PKCS12 keystore..." -ForegroundColor Cyan

$pkcsArgs = @(
    "pkcs12",
    "-export",
    "-in", $CertPath,
    "-inkey", $PrivateKeyPath,
    "-out", $KeystorePath,
    "-name", "wiremock",
    "-passout", "pass:$KeystorePassword"
)

& $openssl @pkcsArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create PKCS12 keystore. Exit code: $LASTEXITCODE"
    exit 1
}

# Convert PKCS12 to JKS for better compatibility with WireMock
# Check if keytool is available (usually with Java or in Docker containers)
$jksPath = Join-Path $CertsDir "wiremock.jks"
$keytool = Get-Command keytool -ErrorAction SilentlyContinue

if ($keytool) {
    Write-Host "Converting PKCS12 to JKS format for WireMock compatibility..."
    $jksArgs = @(
        "-importkeystore",
        "-srckeystore", $KeystorePath,
        "-srcstoretype", "PKCS12",
        "-srcstorepass", $KeystorePassword,
        "-destkeystore", $jksPath,
        "-deststoretype", "JKS",
        "-deststorepass", $KeystorePassword,
        "-noprompt"
    )
    & keytool @jksArgs 2>&1 | Out-Null

    if ($LASTEXITCODE -eq 0) {
        Write-Host "JKS keystore created: $jksPath" -ForegroundColor Green
    } else {
        Write-Host "Note: keytool conversion to JKS skipped (keytool not available or failed)" -ForegroundColor Yellow
    }
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  WireMock HTTPS Certificate Generated" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Files created:" -ForegroundColor White
Write-Host "  Certificate: $CertPath" -ForegroundColor Gray
Write-Host "  Private key: $PrivateKeyPath" -ForegroundColor Gray
Write-Host "  Keystore:    $KeystorePath" -ForegroundColor Gray
if (Test-Path $jksPath) {
    Write-Host "  JKS Keystore: $jksPath" -ForegroundColor Gray
}
Write-Host ""
Write-Host "Keystore password: $KeystorePassword" -ForegroundColor Yellow
Write-Host ""
Write-Host "Next steps:" -ForegroundColor White
Write-Host "  1. Add to your .env file:" -ForegroundColor Gray
Write-Host "     WIREMOCK_KEYSTORE_PASSWORD=$KeystorePassword" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  2. Start WireMock:" -ForegroundColor Gray
Write-Host "     docker compose up wiremock" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  3. Test HTTPS endpoint:" -ForegroundColor Gray
Write-Host "     curl -k https://localhost:10443/__admin/health" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Client trust options:" -ForegroundColor White
Write-Host "  - .NET: Import wiremock.crt or configure HttpClientHandler" -ForegroundColor Gray
Write-Host "  - Java: keytool -importcert -file wiremock.crt -keystore truststore.jks" -ForegroundColor Gray
Write-Host "  - curl: curl --cacert wiremock.crt https://localhost:10443/..." -ForegroundColor Gray
Write-Host "  - Postman: Settings > Certificates > Add CA Certificate" -ForegroundColor Gray
Write-Host ""
