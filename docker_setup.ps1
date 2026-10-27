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
        $installedRuntimes = @()
        foreach ($runtime in @("docker", "podman")) {
            if (Get-Command $runtime -ErrorAction SilentlyContinue) {
                $installedRuntimes += $runtime
                $runtimeIsReachable = $false
                try {
                    & $runtime info *> $null
                    $runtimeIsReachable = ($LASTEXITCODE -eq 0)
                } catch {
                    $runtimeIsReachable = $false
                }
                if (-not $runtimeIsReachable) {
                    continue
                }

                $composeIsAvailable = $false
                try {
                    & $runtime compose version *> $null
                    $composeIsAvailable = ($LASTEXITCODE -eq 0)
                } catch {
                    $composeIsAvailable = $false
                }
                if ($composeIsAvailable) {
                    return $runtime
                }
            }
        }

        if ($installedRuntimes.Count -gt 0) {
            throw @"
No installed container runtime is reachable with Compose support.
Start Docker or Podman, or select one explicitly with -ContainerRuntime.
"@
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

    $runtimeIsReachable = $false
    try {
        & $ContainerCli info *> $null
        $runtimeIsReachable = ($LASTEXITCODE -eq 0)
    } catch {
        $runtimeIsReachable = $false
    }
    if ($runtimeIsReachable) {
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

    $composeIsAvailable = $false
    try {
        & $ContainerCli compose version *> $null
        $composeIsAvailable = ($LASTEXITCODE -eq 0)
    } catch {
        $composeIsAvailable = $false
    }
    if (-not $composeIsAvailable) {
        throw @"
Missing dependency: $RuntimeDisplayName compose support is not available.
Install $RuntimeDisplayName with compose support, or choose another runtime with -ContainerRuntime.
"@
    }
}

function Assert-PodmanStopPersistence {
    if ($ContainerCli -ne "podman") { return }
    $serverVersion = ((& $ContainerCli version --format '{{.Server.Version}}' 2>$null) | Out-String).Trim()
    $versionSucceeded = ($LASTEXITCODE -eq 0)
    if (-not $versionSucceeded -or $serverVersion -notmatch '^\d+\.\d+\.\d+(?:[-+].+)?$') {
        # Native Linux Podman has no separate server object; info describes its engine.
        $remote = ((& $ContainerCli info --format '{{.Host.ServiceIsRemote}}' 2>$null) | Out-String).Trim()
        if ($LASTEXITCODE -eq 0 -and $remote -eq "false") {
            $serverVersion = ((& $ContainerCli info --format '{{.Version.Version}}' 2>$null) | Out-String).Trim()
            $versionSucceeded = ($LASTEXITCODE -eq 0)
        }
    }
    if (-not $versionSucceeded -or $serverVersion -notmatch '^(\d+)\.\d+\.\d+(?:[-+].+)?$') {
        throw "Unable to determine the Podman server version. Podman 6 or later is required to keep manually stopped services stopped across restarts."
    }
    if ([int]$matches[1] -lt 6) {
        throw "Podman server 6 or later is required to keep manually stopped services stopped across restarts. Upgrade the Podman engine, then rerun setup."
    }
}

function Invoke-SharedCompose {
    param([string[]]$ComposeArguments)
    & $ContainerCli compose @ComposeArguments
}

function Initialize-SharedDataVolumes {
    # Compose must treat migrated volumes as external because their original labels remain.
    # Label new volumes so explicit teardown cleanup can find them with either runtime.
    $groupVolumes = [ordered]@{
        azure = @('cosmos-data', 'mssql-data', 'azurite-data')
        aws = @('postgres-data', 'dynamodb-data', 'rabbitmq-data')
    }
    foreach ($group in $groupVolumes.Keys) {
        foreach ($volume in $groupVolumes[$group]) {
            $volumeName = "dev_common_shared_$volume"
            & $ContainerCli volume inspect $volumeName *> $null
            if ($LASTEXITCODE -eq 0) { continue }
            & $ContainerCli volume create --driver local `
                --label "com.docker.compose.project=dev_${group}_shared" `
                --label "com.docker.compose.volume=$volume" $volumeName | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Unable to create shared data volume '$volumeName'." }
        }
    }
}

function Remove-LegacySharedContainers {
    # Podman Compose's --remove-orphans can also remove retained resources.
    # Inspect exact labels and remove only retired samples or relocated cloud resources.
    $servicesToRemove = @('functions-key-init', 'function-app', 'lambda-runtime',
        'lambda-sqs-runtime', 'lambda-sqs-dispatcher', 'function-app-java',
        'lambda-runtime-java', 'lambda-sqs-runtime-java', 'lambda-sqs-dispatcher-java',
        'function-app-node', 'lambda-runtime-node', 'lambda-sqs-dispatcher-node',
        'function-app-python', 'lambda-runtime-python', 'lambda-sqs-dispatcher-python')
    # Release fixed container names before recreating cloud resources in their groups.
    # The new Compose files reuse the original named volumes.
    $servicesToRemove += @('mssql', 'cosmosdb', 'azurite', 'service-bus',
        'postgres', 'dynamodb-volume-init', 'dynamodb', 'rabbitmq', 'aws-emulator', 'aws-init')
    $containerIds = @()
    foreach ($projectLabel in @('com.docker.compose.project', 'io.podman.compose.project')) {
        $found = @(& $ContainerCli ps -a --filter "label=${projectLabel}=dev_common_shared" -q 2>$null)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate retired sample containers in the shared toolbox project.' }
        $containerIds += $found
    }
    foreach ($containerId in @($containerIds | Select-Object -Unique)) {
        $containerId = ([string]$containerId).Trim()
        if (-not $containerId) { continue }
        if ($containerId -notmatch '^[a-fA-F0-9]{12,64}$') { throw 'The selected engine returned an invalid container identifier.' }
        $labelJson = ((& $ContainerCli inspect --format '{{json .Config.Labels}}' $containerId 2>$null) | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Unable to verify labels on a possible retired sample container.' }
        try { $labels = ConvertFrom-Json -InputObject $labelJson -ErrorAction Stop }
        catch { throw 'The selected engine returned invalid container-label metadata.' }
        if ($null -eq $labels) { continue }
        $service = $null
        foreach ($prefix in @('com.docker.compose', 'io.podman.compose')) {
            $projectLabel = $labels.PSObject.Properties["$prefix.project"]
            $serviceLabel = $labels.PSObject.Properties["$prefix.service"]
            if ($projectLabel -and $projectLabel.Value -ceq 'dev_common_shared' -and $serviceLabel) {
                $service = $serviceLabel.Value
                break
            }
        }
        if ($servicesToRemove -ccontains $service) {
            & $ContainerCli rm -f $containerId *> $null
            if ($LASTEXITCODE -ne 0) { throw 'Unable to remove a retired sample container from the shared toolbox project.' }
            Write-Host "Removed legacy shared container: $service (volumes retained)." -ForegroundColor Gray
        }
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
        throw ".env.example not found. Cannot create .env file."
    }
}

# Keep existing developer .env files compatible when new service settings
# are introduced without overwriting any local values.
$envLines = @(Get-Content -LiteralPath $EnvFile -Encoding UTF8)
$missingAssignments = @()
foreach ($templateLine in Get-Content -LiteralPath $EnvExampleFile -Encoding UTF8) {
    if ($templateLine -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=') {
        $key = $matches[1]
        if (-not ($envLines -cmatch "^\s*(?:export\s+)?$([regex]::Escape($key))\s*=")) {
            $missingAssignments += $templateLine
            $envLines += $templateLine
            Write-Host "$key added to .env for local services." -ForegroundColor Green
        }
    }
}
if ($missingAssignments.Count -gt 0) {
    # A leading newline also handles a developer file with no final newline.
    Add-Content -LiteralPath $EnvFile -Encoding UTF8 -Value ([Environment]::NewLine + ($missingAssignments -join [Environment]::NewLine))
}

# Generate explicit auto placeholders once without overwriting developer values.
. (Join-Path $ContainersDir "environment.ps1")
Initialize-ToolboxEnvironment -EnvFile $EnvFile

#endregion

#region WireMock Certificate Setup
$ContainerCli = Resolve-ContainerRuntime -RequestedRuntime $ContainerRuntime
$RuntimeDisplayName = (Get-Culture).TextInfo.ToTitleCase($ContainerCli)
Assert-ContainerRuntimeReady -ContainerCli $ContainerCli -RuntimeDisplayName $RuntimeDisplayName
Assert-ContainerComposeAvailable -ContainerCli $ContainerCli -RuntimeDisplayName $RuntimeDisplayName
& (Join-Path $PSScriptRoot 'docker_pull.ps1') -ContainerRuntime $ContainerCli
if ($LASTEXITCODE -ne 0) { throw 'Missing-image preparation failed.' }
$engineVersion = & $ContainerCli image inspect --format '{{ index .Config.Labels "com.microsoft.version" }}' mcr.microsoft.com/mssql/server:latest
if ($LASTEXITCODE -ne 0 -or [string]$engineVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw 'The local SQL Server base is missing a valid publisher version label.'
}
$env:TOOLBOX_MSSQL_ENGINE_VERSION = ([string]$engineVersion).Trim()
Assert-PodmanStopPersistence

$SharedGroups = @("common", "azure", "aws")
$SelectedServices = @()
foreach ($group in $SharedGroups) {
    $SelectedServices += @(Invoke-SharedCompose -ComposeArguments @("--env-file", $EnvFile,
        "-f", "$ContainersDir/docker-compose-$group.yml", "-p", "dev_${group}_shared", "config", "--services"))
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the $group Compose services. Check containers/.env."
    }
}

Write-Host "`n=== WireMock Certificate Setup ===" -ForegroundColor Cyan

$WireMockKeystore = Join-Path $CertsDir "wiremock.jks"
$GenerateCertScript = Join-Path $CertsDir "Generate-WireMockCert.ps1"

if (-not (Test-Path $WireMockKeystore) -or $EnvFileJustCreated -or $script:ToolboxGeneratedWireMockPassword) {
    if (Test-Path $GenerateCertScript) {
        Write-Host "Generating WireMock TLS certificate..." -ForegroundColor Yellow
        $EnvPassword = Get-ToolboxWireMockPassword -EnvFile $EnvFile -ContainerRuntime $ContainerCli
        if (-not $EnvPassword) { throw "Configure WIREMOCK_KEYSTORE_PASSWORD in containers/.env before generating certificates." }
        $previousKeystorePassword = [Environment]::GetEnvironmentVariable("TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD", "Process")
        try {
            $env:TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD = $EnvPassword
            & $GenerateCertScript -ContainerRuntime $ContainerCli -Force
        } finally {
            [Environment]::SetEnvironmentVariable("TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD", $previousKeystorePassword, "Process")
        }
        Write-Host "WireMock certificate generated using the configured keystore password." -ForegroundColor Green
    } else {
        Write-Warning "WireMock certificate script not found at: $GenerateCertScript"
    }
} else {
    Write-Host "WireMock keystore already exists. Skipping certificate generation." -ForegroundColor Gray
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
Write-Host "`n=== $RuntimeDisplayName Services ===" -ForegroundColor Cyan

Write-Host "Starting $RuntimeDisplayName containers..." -ForegroundColor Yellow

## Start the shared infrastructure resources.
Initialize-SharedDataVolumes
Remove-LegacySharedContainers
foreach ($group in $SharedGroups) {
    Write-Host "Starting dev_${group}_shared..." -ForegroundColor Yellow
    Invoke-SharedCompose -ComposeArguments @("--env-file", $EnvFile,
        "-f", "$ContainersDir/docker-compose-$group.yml", "-p", "dev_${group}_shared",
        "up", "--build", "-d")
    if ($LASTEXITCODE -ne 0) {
        Write-Error "$RuntimeDisplayName compose for dev_${group}_shared failed with exit code $LASTEXITCODE." -ErrorAction Continue
        exit $LASTEXITCODE
    }
}

Write-Host "$RuntimeDisplayName containers started." -ForegroundColor Green
#endregion

Write-Host "`n=== Setup Complete ===" -ForegroundColor Green
Write-Host "Services available:" -ForegroundColor White
Write-Host "  SQL Server:    localhost:10433" -ForegroundColor Gray
Write-Host "  PostgreSQL:    localhost:10432" -ForegroundColor Gray
Write-Host "  CosmosDB:      https://localhost:10081" -ForegroundColor Gray
Write-Host "  Cosmos Explorer: https://localhost:10181" -ForegroundColor Gray
Write-Host "  DynamoDB:      http://localhost:10180" -ForegroundColor Gray
Write-Host "  Redis:         localhost:10120" -ForegroundColor Gray
Write-Host "  RedisInsight:  http://localhost:10121" -ForegroundColor Gray
Write-Host "  SMTP4Dev SMTP: localhost:10130" -ForegroundColor Gray
Write-Host "  SMTP4Dev POP:  localhost:10131" -ForegroundColor Gray
Write-Host "  SMTP4Dev IMAP: localhost:10132" -ForegroundColor Gray
Write-Host "  SMTP4Dev Web:  http://localhost:10140" -ForegroundColor Gray
Write-Host "  Seq (OTLP):    http://localhost:10150" -ForegroundColor Gray
Write-Host "  WireMock HTTP: http://localhost:10080" -ForegroundColor Gray
Write-Host "  WireMock HTTPS: https://localhost:10443" -ForegroundColor Gray
Write-Host "  Azurite Blob:  localhost:10000" -ForegroundColor Gray
Write-Host "  Azurite Queue: localhost:10001" -ForegroundColor Gray
Write-Host "  Azurite Table: localhost:10002" -ForegroundColor Gray
Write-Host "  Service Bus:   localhost:10170" -ForegroundColor Gray
Write-Host "  Service Bus Admin: localhost:10171" -ForegroundColor Gray
if ($SelectedServices -contains "rabbitmq") {
    Write-Host "  RabbitMQ: localhost:10190; management: http://localhost:10191" -ForegroundColor Gray
}
if ($SelectedServices -contains "aws-emulator") {
    Write-Host "  Moto SQS/SNS/EventBridge/S3: http://localhost:10195" -ForegroundColor Gray
}
Write-Host ""
Write-Host "See README.md for using these infrastructure resources from the independent examples."

