# Setup Docker Services

[CmdletBinding()]
param()

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

function Sync-DockerClientEnvironment {
    if (-not $isWindowsPlatform) {
        return
    }

    $persistedDockerHost = [Environment]::GetEnvironmentVariable('DOCKER_HOST', 'User')
    if (-not $persistedDockerHost) {
        $persistedDockerHost = [Environment]::GetEnvironmentVariable('DOCKER_HOST', 'Machine')
    }

    if (-not $env:DOCKER_HOST -and $persistedDockerHost) {
        $env:DOCKER_HOST = $persistedDockerHost.Trim()
    }

    if ($env:DOCKER_CONTEXT -and [string]::IsNullOrWhiteSpace($env:DOCKER_CONTEXT)) {
        Remove-Item Env:DOCKER_CONTEXT -ErrorAction SilentlyContinue
    }
}

function Test-WslDockerHost {
    if (-not $isWindowsPlatform) {
        return $false
    }

    return $env:DOCKER_HOST -match '^tcp://(127\.0\.0\.1|localhost):2375/?$'
}

function Get-WslPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $windowsPathMatch = [System.Text.RegularExpressions.Regex]::Match($resolvedPath, '^(?<drive>[A-Za-z]):(?<rest>.*)$')

    if (-not $windowsPathMatch.Success) {
        Write-Error "Failed to translate '$Path' to a WSL path."
        exit 1
    }

    $driveLetter = $windowsPathMatch.Groups['drive'].Value.ToLowerInvariant()
    $pathRemainder = $windowsPathMatch.Groups['rest'].Value -replace '\\', '/'

    return "/mnt/$driveLetter$pathRemainder"
}

function Invoke-ComposeUp {
    param(
        [Parameter(Mandatory)]
        [bool]$UseDockerComposePlugin,
        [Parameter(Mandatory)]
        [bool]$UseDockerComposeStandalone
    )

    if (Test-WslDockerHost) {
        $wslContainersDir = Get-WslPath -Path $ContainersDir
        $composeCommand = "cd '$wslContainersDir' && docker compose --env-file .env -f docker-compose-common.yml -p dev_common_shared up -d"
        wsl.exe sh -lc $composeCommand
        return
    }

    if ($UseDockerComposePlugin) {
        docker compose -f "docker-compose-common.yml" -p dev_common_shared up -d
    } else {
        docker-compose -f "docker-compose-common.yml" -p dev_common_shared up -d
    }
}

function Assert-LinuxContainerEngine {
    $dockerOsType = docker info --format '{{.OSType}}' 2>$null
    $dockerOperatingSystem = docker info --format '{{.OperatingSystem}}' 2>$null

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Unable to determine the Docker daemon container OS type."
        exit 1
    }

    $dockerOsType = $dockerOsType.Trim()
    $dockerOperatingSystem = $dockerOperatingSystem.Trim()

    if ($dockerOsType -ne 'linux') {
        Write-Error "This development stack requires Docker to run Linux containers. Current Docker daemon OSType: '$dockerOsType' ($dockerOperatingSystem)."

        if ($isWindowsPlatform) {
            Write-Host "If you are using Docker Desktop, switch to Linux containers and rerun this script." -ForegroundColor Yellow
            Write-Host "Typical fix: Docker Desktop tray icon > Switch to Linux containers..." -ForegroundColor Gray
        }

        exit 1
    }
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

#region Docker Services
Write-Host "`n=== Docker Services ===" -ForegroundColor Cyan

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Error "Docker is not installed or not in PATH."
    exit 1
}

Sync-DockerClientEnvironment

docker info *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Cannot access the Docker daemon. The current user likely does not have permission to the Docker API named pipe or the daemon is not running."

    $dockerService = Get-Service -Name "docker" -ErrorAction SilentlyContinue
    if ($dockerService) {
        Write-Host "Docker service status: $($dockerService.Status)" -ForegroundColor Yellow
        if ($dockerService.Status -ne 'Running') {
            Write-Host "Start it with: Start-Service docker" -ForegroundColor Gray
        }
    }

    Write-Host "Try these fixes:" -ForegroundColor Yellow
    Write-Host "  1. Start an elevated PowerShell and run this script again." -ForegroundColor Gray
    Write-Host "  2. Ensure the Docker daemon service is running (Get-Service docker)." -ForegroundColor Gray
    Write-Host "  3. If using a non-Desktop Docker Engine, configure daemon access for your non-admin user." -ForegroundColor Gray
    exit 1
}

Assert-LinuxContainerEngine

$useDockerComposePlugin = $false
docker compose version *> $null
if ($LASTEXITCODE -eq 0) {
    $useDockerComposePlugin = $true
}

$useDockerComposeStandalone = $false
if (-not $useDockerComposePlugin -and (Get-Command docker-compose -ErrorAction SilentlyContinue)) {
    $useDockerComposeStandalone = $true
}

if (-not $useDockerComposePlugin -and -not $useDockerComposeStandalone) {
    Write-Error "Neither 'docker compose' nor 'docker-compose' is available. Install Docker Compose and try again."
    exit 1
}

Write-Host "Starting Docker containers..." -ForegroundColor Yellow

Push-Location $ContainersDir
try {
    # Run from containers/ so compose automatically loads containers/.env across compose variants.
    Invoke-ComposeUp -UseDockerComposePlugin $useDockerComposePlugin -UseDockerComposeStandalone $useDockerComposeStandalone
} finally {
    Pop-Location
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "Docker compose failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

Write-Host "Docker containers started." -ForegroundColor Green
#endregion

function Get-ServiceBindLabel {
    param(
        [Parameter(Mandatory)]
        [string]$Key
    )

    if (-not $EnvValues.ContainsKey($Key)) {
        return 'localhost'
    }

    $configuredHost = $EnvValues[$Key].Trim()
    if (-not $configuredHost -or $configuredHost -eq '127.0.0.1') {
        return 'localhost'
    }

    if ($configuredHost -eq '0.0.0.0') {
        return '<host-ip-or-dns>'
    }

    return $configuredHost
}

$mssqlBindHost = Get-ServiceBindLabel -Key 'MSSQL_BIND_HOST'
$cosmosBindHost = Get-ServiceBindLabel -Key 'COSMOSDB_BIND_HOST'
$redisBindHost = Get-ServiceBindLabel -Key 'REDIS_BIND_HOST'
$redisInsightBindHost = Get-ServiceBindLabel -Key 'REDISINSIGHT_BIND_HOST'
$smtpBindHost = Get-ServiceBindLabel -Key 'SMTP4DEV_BIND_HOST'
$seqBindHost = Get-ServiceBindLabel -Key 'SEQ_BIND_HOST'
$wiremockBindHost = Get-ServiceBindLabel -Key 'WIREMOCK_BIND_HOST'
$azuriteBindHost = Get-ServiceBindLabel -Key 'AZURITE_BIND_HOST'
$serviceBusBindHost = Get-ServiceBindLabel -Key 'SERVICEBUS_BIND_HOST'

Write-Host "`n=== Setup Complete ===" -ForegroundColor Green
Write-Host "Services available:" -ForegroundColor White
Write-Host "  SQL Server:    ${mssqlBindHost}:10433" -ForegroundColor Gray
Write-Host "  CosmosDB:      https://${cosmosBindHost}:10081" -ForegroundColor Gray
Write-Host "  Cosmos Explorer: http://${cosmosBindHost}:10181" -ForegroundColor Gray
Write-Host "  Redis:         ${redisBindHost}:10120" -ForegroundColor Gray
Write-Host "  RedisInsight:  http://${redisInsightBindHost}:10121" -ForegroundColor Gray
Write-Host "  SMTP4Dev SMTP: ${smtpBindHost}:10130" -ForegroundColor Gray
Write-Host "  SMTP4Dev POP:  ${smtpBindHost}:10131" -ForegroundColor Gray
Write-Host "  SMTP4Dev IMAP: ${smtpBindHost}:10132" -ForegroundColor Gray
Write-Host "  SMTP4Dev Web:  http://${smtpBindHost}:10140" -ForegroundColor Gray
Write-Host "  Seq (OTEL):    http://${seqBindHost}:10150" -ForegroundColor Gray
Write-Host "  WireMock HTTP: http://${wiremockBindHost}:10080" -ForegroundColor Gray
Write-Host "  WireMock HTTPS: https://${wiremockBindHost}:10443" -ForegroundColor Gray
Write-Host "  Azurite Blob:  ${azuriteBindHost}:10000" -ForegroundColor Gray
Write-Host "  Azurite Queue: ${azuriteBindHost}:10001" -ForegroundColor Gray
Write-Host "  Azurite Table: ${azuriteBindHost}:10002" -ForegroundColor Gray
Write-Host "  Service Bus:   ${serviceBusBindHost}:10170" -ForegroundColor Gray
Write-Host "  Service Bus Admin: ${serviceBusBindHost}:10171" -ForegroundColor Gray
Write-Host ""
Write-Host "Set individual *_BIND_HOST values in containers/.env to 0.0.0.0 to expose selected services externally." -ForegroundColor DarkGray
Write-Host "Head back to README.md for deployment of the database and other services..."

