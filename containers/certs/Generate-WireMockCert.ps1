<#
.SYNOPSIS
    Generates a JKS keystore and exports the certificate for WireMock HTTPS support.

.DESCRIPTION
    This script creates a Java KeyStore (JKS) with a self-signed certificate for local
    WireMock HTTPS development. It also exports the public certificate (.crt) for client trust.

    Prerequisites:
    - Free OpenJDK must be installed and keytool must be available in PATH
    - Alternatively, set JAVA_HOME environment variable

.PARAMETER KeystorePassword
    Password for the keystore. Default: "changeit"

.PARAMETER ValidityDays
    Certificate validity in days. Default: 3650 (10 years)

.PARAMETER Force
    Overwrite existing keystore and certificate files without prompting.

.PARAMETER ShowPassword
    Display the keystore password in the summary output.

.EXAMPLE
    .\Generate-WireMockCert.ps1
    Creates keystore with default password "changeit"

.EXAMPLE
    .\Generate-WireMockCert.ps1 -KeystorePassword "mypassword" -Force
    Creates keystore with custom password, overwriting existing files

.EXAMPLE
    .\Generate-WireMockCert.ps1 -ShowPassword
    Creates keystore and prints the password in the summary output

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
    [switch]$Force,

    [Parameter()]
    [switch]$ShowPassword
)

if ($PSVersionTable.PSVersion -lt [version]"5.1") {
    Write-Error "Missing dependency: PowerShell 5.1 or later is required. Current version: $($PSVersionTable.PSVersion)."
    exit 1
}

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Get script directory
$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) {
    $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

# Output paths
$KeystorePath = Join-Path $ScriptDir "wiremock.jks"
$CertPath = Join-Path $ScriptDir "wiremock.crt"
$WireMockHttpsPort = "10443"

# Check for existing files
if (-not $Force) {
    if (Test-Path $KeystorePath) {
        $response = Read-Host "Keystore '$KeystorePath' already exists. Overwrite? (y/N)"
        if ($response -ne 'y' -and $response -ne 'Y') {
            Write-Host "Aborted. Use -Force to overwrite without prompting." -ForegroundColor Yellow
            exit 0
        }
    }
}

# Find keytool
$keytool = $null

# Check PATH first
$keytool = Get-Command keytool -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source

# Check JAVA_HOME if not in PATH
if (-not $keytool -and $env:JAVA_HOME) {
    $javaHomeTool = Join-Path $env:JAVA_HOME "bin\keytool.exe"
    if (Test-Path $javaHomeTool) {
        $keytool = $javaHomeTool
    }
}

# Check common installation paths on Windows
if (-not $keytool) {
    $commonPaths = @(
        "C:\Program Files\Java\*\bin\keytool.exe",
        "C:\Program Files\Eclipse Adoptium\*\bin\keytool.exe",
        "C:\Program Files\Microsoft\jdk-*\bin\keytool.exe",
        "C:\Program Files\Zulu\*\bin\keytool.exe"
    )

    foreach ($pattern in $commonPaths) {
        $found = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            $keytool = $found.FullName
            break
        }
    }
}

if (-not $keytool) {
    Write-Error @"
Missing dependency: Java JDK keytool was not found.
Please ensure free OpenJDK is installed and one of the following:
  1. Add Java bin directory to PATH
  2. Set JAVA_HOME environment variable
  3. Install free Microsoft OpenJDK with: winget install Microsoft.OpenJDK.25
"@
    exit 1
}

Write-Host "Using keytool: $keytool" -ForegroundColor Cyan

# Remove existing files if present
if (Test-Path $KeystorePath) {
    Remove-Item $KeystorePath -Force
    Write-Host "Removed existing keystore." -ForegroundColor Gray
}
if (Test-Path $CertPath) {
    Remove-Item $CertPath -Force
    Write-Host "Removed existing certificate." -ForegroundColor Gray
}

Write-Host ""
Write-Host "Generating JKS keystore with self-signed certificate..." -ForegroundColor Cyan

# Generate keystore with certificate including SANs for localhost
$genArgs = @(
    "-genkeypair",
    "-alias", "wiremock",
    "-keyalg", "RSA",
    "-keysize", "2048",
    "-validity", $ValidityDays.ToString(),
    "-keystore", $KeystorePath,
    "-storetype", "JKS",
    "-storepass", $KeystorePassword,
    "-keypass", $KeystorePassword,
    "-dname", "CN=localhost, OU=Development, O=Local, L=Local, ST=UT, C=US",
    "-ext", "SAN=dns:localhost,dns:wiremock,dns:host.docker.internal,ip:127.0.0.1"
)

& $keytool @genArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to generate keystore. Exit code: $LASTEXITCODE"
    exit 1
}

Write-Host "Keystore created: $KeystorePath" -ForegroundColor Green

# Export certificate for client trust
Write-Host ""
Write-Host "Exporting public certificate..." -ForegroundColor Cyan

$exportArgs = @(
    "-exportcert",
    "-alias", "wiremock",
    "-keystore", $KeystorePath,
    "-storepass", $KeystorePassword,
    "-rfc",
    "-file", $CertPath
)

& $keytool @exportArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to export certificate. Exit code: $LASTEXITCODE"
    exit 1
}

Write-Host "Certificate exported: $CertPath" -ForegroundColor Green

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  WireMock HTTPS Certificate Generated" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Files created:" -ForegroundColor White
Write-Host "  Keystore:    $KeystorePath" -ForegroundColor Gray
Write-Host "  Certificate: $CertPath" -ForegroundColor Gray
Write-Host ""
if ($ShowPassword) {
    Write-Host "Keystore password: $KeystorePassword" -ForegroundColor Yellow
} else {
    Write-Host "Keystore password: hidden (use -ShowPassword to display it)" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "Next steps:" -ForegroundColor White
Write-Host "  1. Add to your .env file:" -ForegroundColor Gray
if ($ShowPassword) {
    Write-Host "     WIREMOCK_KEYSTORE_PASSWORD=$KeystorePassword" -ForegroundColor DarkGray
} else {
    Write-Host "     WIREMOCK_KEYSTORE_PASSWORD=<your-keystore-password>" -ForegroundColor DarkGray
}
Write-Host ""
Write-Host "  2. Start WireMock:" -ForegroundColor Gray
Write-Host "     docker compose up wiremock" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  3. Test HTTPS endpoint:" -ForegroundColor Gray
Write-Host "     curl -k https://localhost:$WireMockHttpsPort/__admin/health" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Client trust options:" -ForegroundColor White
Write-Host "  - .NET: Import wiremock.crt or configure HttpClientHandler" -ForegroundColor Gray
Write-Host "  - Java: keytool -importcert -file wiremock.crt -keystore truststore.jks" -ForegroundColor Gray
Write-Host "  - curl: curl --cacert wiremock.crt https://localhost:$WireMockHttpsPort/..." -ForegroundColor Gray
Write-Host "  - Postman: Settings > Certificates > Add CA Certificate" -ForegroundColor Gray
Write-Host ""
