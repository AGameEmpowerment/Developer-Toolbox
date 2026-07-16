# Setup Container Services

[CmdletBinding()]
param(
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

$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) { $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

$ContainersDir = Join-Path $ScriptDir "containers"
$CertsDir = Join-Path $ContainersDir "certs"
$EnvFile = Join-Path $ContainersDir ".env"
$EnvExampleFile = Join-Path $ContainersDir ".env.example"
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

        throw @"
Missing dependency: no supported container runtime CLI was found.
Install Docker Desktop or Podman, make sure the CLI is available in PATH, then open a new terminal.
"@
    }

    if (Get-Command $RequestedRuntime -ErrorAction SilentlyContinue) {
        return $RequestedRuntime
    }

    throw @"
Missing dependency: '$RequestedRuntime' was not found in PATH.
Install $RequestedRuntime, make sure its CLI is available in PATH, then open a new terminal.
"@
}

function Assert-ContainerRuntimeReady {
    param(
        [Parameter(Mandatory)]
        [string]$ContainerCli,

        [Parameter(Mandatory)]
        [string]$RuntimeDisplayName
    )

    & $ContainerCli info *> $null
    if ($LASTEXITCODE -eq 0) {
        return
    }

    if ($ContainerCli -eq "docker") {
        throw @"
$RuntimeDisplayName CLI is installed, but the Docker engine is not reachable.
Start Docker Desktop, wait until it finishes starting, then rerun this script.
"@
    }

    throw @"
$RuntimeDisplayName CLI is installed, but the Podman engine is not reachable.
Start the Podman machine with 'podman machine start', then rerun this script.
"@
}

function Assert-ContainerComposeAvailable {
    param(
        [Parameter(Mandatory)]
        [string]$ContainerCli,

        [Parameter(Mandatory)]
        [string]$RuntimeDisplayName
    )

    & $ContainerCli compose version *> $null
    if ($LASTEXITCODE -ne 0) {
        throw @"
Missing dependency: $RuntimeDisplayName compose support is not available.
Install $RuntimeDisplayName with compose support, or choose another runtime with -ContainerRuntime.
"@
    }
}

function Find-KeyTool {
    $keytool = Get-Command keytool -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
    if ($keytool) {
        return $keytool
    }

    if ($env:JAVA_HOME) {
        $javaHomeTool = Join-Path $env:JAVA_HOME "bin\keytool.exe"
        if (Test-Path $javaHomeTool) {
            return $javaHomeTool
        }
    }

    $commonPaths = @(
        "C:\Program Files\Java\*\bin\keytool.exe",
        "C:\Program Files\Eclipse Adoptium\*\bin\keytool.exe",
        "C:\Program Files\Microsoft\jdk-*\bin\keytool.exe",
        "C:\Program Files\Zulu\*\bin\keytool.exe"
    )

    foreach ($pattern in $commonPaths) {
        $found = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            return $found.FullName
        }
    }

    return $null
}

function Assert-JavaKeyToolAvailable {
    if (Find-KeyTool) {
        return
    }

    throw @"
Missing dependency: Java JDK keytool was not found.
Install free Microsoft OpenJDK with 'winget install Microsoft.OpenJDK.25', then open a new terminal.
If keytool is still unavailable, add the JDK bin directory to PATH or set JAVA_HOME.
WireMock HTTPS certificate generation requires keytool.
"@
}

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
        throw ".env.example not found. Cannot create .env file."
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
        Assert-JavaKeyToolAvailable

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
            Assert-JavaKeyToolAvailable

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

# Import certificate to Windows trusted root store for automatic trust (Windows only)
$WireMockCert = Join-Path $CertsDir "wiremock.crt"
if (Test-Path $WireMockCert) {
    # Always attempt to import to Windows certificate store on Windows
    if ($isWindowsPlatform) {
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
        Write-Host "WireMock certificate generated at: $WireMockCert" -ForegroundColor Gray
        Write-Host "To trust the certificate on Linux/macOS, you may need to add it to your system's CA trust store." -ForegroundColor Yellow
    }
} else {
    Write-Warning "WireMock certificate not found. HTTPS clients may not trust the server."
}
#endregion

#region Container Services
$ContainerCli = Resolve-ContainerRuntime -RequestedRuntime $ContainerRuntime
$RuntimeDisplayName = (Get-Culture).TextInfo.ToTitleCase($ContainerCli)
Assert-ContainerRuntimeReady -ContainerCli $ContainerCli -RuntimeDisplayName $RuntimeDisplayName
Assert-ContainerComposeAvailable -ContainerCli $ContainerCli -RuntimeDisplayName $RuntimeDisplayName

Write-Host "`n=== $RuntimeDisplayName Services ===" -ForegroundColor Cyan

Write-Host "Starting $RuntimeDisplayName containers..." -ForegroundColor Yellow

## Start the shared development collection.
& $ContainerCli compose `
    --env-file "$EnvFile" `
    -f "$ContainersDir/docker-compose-common.yml" `
    -p dev_common_shared `
    up -d

if ($LASTEXITCODE -ne 0) {
    Write-Error "$RuntimeDisplayName compose failed with exit code $LASTEXITCODE." -ErrorAction Continue
    exit $LASTEXITCODE
}

Write-Host "$RuntimeDisplayName containers started." -ForegroundColor Green
#endregion

Write-Host "`n=== Setup Complete ===" -ForegroundColor Green
Write-Host "Services available:" -ForegroundColor White
Write-Host "  SQL Server:    localhost:10433" -ForegroundColor Gray
Write-Host "  CosmosDB:      https://localhost:10081" -ForegroundColor Gray
Write-Host "  Cosmos Explorer: http://localhost:10181" -ForegroundColor Gray
Write-Host "  Redis:         localhost:10120" -ForegroundColor Gray
Write-Host "  RedisInsight:  http://localhost:10121" -ForegroundColor Gray
Write-Host "  SMTP4Dev SMTP: localhost:10130" -ForegroundColor Gray
Write-Host "  SMTP4Dev POP:  localhost:10131" -ForegroundColor Gray
Write-Host "  SMTP4Dev IMAP: localhost:10132" -ForegroundColor Gray
Write-Host "  SMTP4Dev Web:  http://localhost:10140" -ForegroundColor Gray
Write-Host "  Seq (OTEL):    http://localhost:10150" -ForegroundColor Gray
Write-Host "  WireMock HTTP: http://localhost:10080" -ForegroundColor Gray
Write-Host "  WireMock HTTPS: https://localhost:10443" -ForegroundColor Gray
Write-Host "  Azurite Blob:  localhost:10000" -ForegroundColor Gray
Write-Host "  Azurite Queue: localhost:10001" -ForegroundColor Gray
Write-Host "  Azurite Table: localhost:10002" -ForegroundColor Gray
Write-Host "  Service Bus:   localhost:10170" -ForegroundColor Gray
Write-Host "  Service Bus Admin: localhost:10171" -ForegroundColor Gray
Write-Host ""
Write-Host "Head back to README.md for deployment of the database and other services..."

