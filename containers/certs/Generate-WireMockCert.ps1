<#
.SYNOPSIS
    Generates a JKS keystore and exports the certificate for WireMock HTTPS support.

.DESCRIPTION
    This script creates a Java KeyStore (JKS) with a self-signed certificate for local
    WireMock HTTPS development. It also exports the public certificate (.crt) for client trust.

    Uses host OpenJDK keytool when available; otherwise runs keytool from the
    existing WireMock image using Docker or Podman. No host Java installation is required.

.PARAMETER KeystorePassword
    Password for the keystore. Defaults to TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD
    when set, otherwise "changeit". Setup uses the environment to avoid native process arguments.

.PARAMETER ValidityDays
    Certificate validity in days. Default: 3650 (10 years)

.PARAMETER Force
    Overwrite existing keystore and certificate files without prompting.

.PARAMETER ShowPassword
    Display the keystore password in the summary output.

.PARAMETER ContainerRuntime
    Container engine used when host keytool is unavailable. Setup passes its selected engine.

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
    [string]$KeystorePassword = $(if ($env:TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD) { $env:TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD } else { "changeit" }),

    [Parameter()]
    [int]$ValidityDays = 3650,

    [Parameter()]
    [switch]$Force,

    [Parameter()]
    [switch]$ShowPassword,

    [Parameter()]
    [ValidateSet("auto", "docker", "podman")]
    [string]$ContainerRuntime = $(if ($env:CONTAINER_RUNTIME) { $env:CONTAINER_RUNTIME } else { "auto" })
)

if ($PSVersionTable.PSVersion -lt [version]"5.1") {
    Write-Error "Missing dependency: PowerShell 5.1 or later is required. Current version: $($PSVersionTable.PSVersion)."
    exit 1
}

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($KeystorePassword.Length -lt 6) {
    throw "The WireMock keystore password must contain at least six characters."
}
if ($ValidityDays -lt 1) {
    throw "Certificate validity must be a positive number of days."
}

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
    $toolName = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { "keytool.exe" } else { "keytool" }
    $javaHomeTool = Join-Path $env:JAVA_HOME "bin/$toolName"
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

$KeyToolKeystorePath = $KeystorePath
$KeyToolCertPath = $CertPath
$ContainerArguments = @()
if ($keytool) {
    Write-Host "Using host keytool." -ForegroundColor Cyan
} else {
    if ($ContainerRuntime -eq "auto") {
        foreach ($candidate in @("docker", "podman")) {
            if (Get-Command $candidate -ErrorAction SilentlyContinue) {
                & $candidate info *> $null
                if ($LASTEXITCODE -eq 0) {
                    $ContainerRuntime = $candidate
                    break
                }
            }
        }
    }
    if ($ContainerRuntime -eq "auto" -or -not (Get-Command $ContainerRuntime -ErrorAction SilentlyContinue)) {
        throw "Certificate generation requires host keytool or a reachable Docker/Podman engine."
    }
    . (Join-Path $PSScriptRoot '../../.devcontainer/ensure_image.ps1')
    Ensure-ToolboxImage -Runtime $ContainerRuntime -Image 'wiremock/wiremock:latest'
    $KeyToolKeystorePath = "/toolbox/certs/wiremock.jks"
    $KeyToolCertPath = "/toolbox/certs/wiremock.crt"
    $mountOptions = if ($ContainerRuntime -eq "podman") { "rw,z" } else { "rw" }
    $ContainerArguments = @("run", "--rm", "--network", "none",
        "--entrypoint", "keytool", "--env", "TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD",
        "--volume", "${ScriptDir}:/toolbox/certs:$mountOptions")
    if ($ContainerRuntime -eq "podman") {
        # Preserve writable bind mounts for both rootless and rootful developer engines.
        $ContainerArguments += @("--userns", "keep-id")
    } elseif ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        $userId = (& id -u).Trim()
        $groupId = (& id -g).Trim()
        if ($userId -notmatch '^\d+$' -or $groupId -notmatch '^\d+$') { throw "Cannot determine certificate file ownership." }
        $ContainerArguments += @("--user", "${userId}:${groupId}")
    }
    $ContainerArguments += "wiremock/wiremock:latest"
    Write-Host "Using keytool from the WireMock image with $ContainerRuntime." -ForegroundColor Cyan
}

function Invoke-WireMockKeyTool {
    param([string[]]$Arguments)
    $previousPassword = [Environment]::GetEnvironmentVariable("TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD", "Process")
    try {
        $env:TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD = $KeystorePassword
        if ($keytool) {
            & $keytool @Arguments *> $null
        } else {
            & $ContainerRuntime @ContainerArguments @Arguments *> $null
        }
        if ($LASTEXITCODE -ne 0) {
            throw "WireMock keytool failed with exit code $LASTEXITCODE. Check the selected engine and certificate-directory permissions."
        }
    } finally {
        [Environment]::SetEnvironmentVariable("TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD", $previousPassword, "Process")
    }
}

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
    "-keystore", $KeyToolKeystorePath,
    "-storetype", "JKS",
    "-storepass:env", "TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD",
    "-keypass:env", "TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD",
    "-dname", "CN=localhost, OU=Development, O=Local, L=Local, ST=UT, C=US",
    "-ext", "SAN=dns:localhost,dns:wiremock,dns:host.docker.internal,ip:127.0.0.1"
)

Invoke-WireMockKeyTool -Arguments $genArgs
if (-not (Test-Path -LiteralPath $KeystorePath)) { throw "WireMock keytool did not create the expected keystore." }

Write-Host "Keystore created: $KeystorePath" -ForegroundColor Green

# Export certificate for client trust
Write-Host ""
Write-Host "Exporting public certificate..." -ForegroundColor Cyan

$exportArgs = @(
    "-exportcert",
    "-alias", "wiremock",
    "-keystore", $KeyToolKeystorePath,
    "-storepass:env", "TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD",
    "-rfc",
    "-file", $KeyToolCertPath
)

Invoke-WireMockKeyTool -Arguments $exportArgs
if (-not (Test-Path -LiteralPath $CertPath)) { throw "WireMock keytool did not export the expected certificate." }

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
Write-Host "     docker compose up wiremock  # or: podman compose up wiremock" -ForegroundColor DarkGray
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
