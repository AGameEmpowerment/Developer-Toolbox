[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release", "Development", "Testing", "Staging", "Production")]
    [string]$Configuration = "Development",

    [string]$Tag = "local",

    [string]$EnvironmentFile = "devops/docker/systemuptimetracker.production.env",

    [switch]$SkipBuild,

    [switch]$NoCache
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepositoryPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ([IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $RepositoryRoot $Path
}

function Wait-ContainerHealth {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName,

        [TimeSpan]$Timeout = [TimeSpan]::FromMinutes(3)
    )

    $deadline = [DateTimeOffset]::UtcNow.Add($Timeout)
    do {
        $status = & docker inspect $ContainerName --format "{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}" 2>$null
        if ($status -eq "healthy") {
            return
        }

        Start-Sleep -Seconds 3
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    & docker logs --tail 75 $ContainerName
    throw "Container '$ContainerName' did not become healthy within $($Timeout.TotalMinutes) minutes."
}

function Wait-HttpHealth {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [Uri]$Uri,

        [TimeSpan]$Timeout = [TimeSpan]::FromMinutes(3)
    )

    $deadline = [DateTimeOffset]::UtcNow.Add($Timeout)
    do {
        try {
            $response = Invoke-WebRequest -Uri $Uri -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                $health = $response.Content | ConvertFrom-Json -ErrorAction Stop
                if ([string]$health.status -ieq "healthy") {
                    return
                }
            }
        }
        catch {
            # The application can refuse connections while its dependencies and
            # startup migrations are still initializing.
        }

        Start-Sleep -Seconds 3
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "$Name did not report healthy at '$Uri' within $($Timeout.TotalMinutes) minutes."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$composeFile = Join-Path $PSScriptRoot "docker-compose.yml"
$localComposeFile = Join-Path $PSScriptRoot "docker-compose.local.yml"
$buildScript = Join-Path $PSScriptRoot "Build-SystemUptimeTrackerDockerImages.ps1"
$loginScript = Join-Path $PSScriptRoot "Set-SystemUptimeTrackerSqlApplicationLogin.ps1"
$environmentHelper = Join-Path $PSScriptRoot "SystemUptimeTracker.Environment.ps1"
$environmentPath = Resolve-RepositoryPath -RepositoryRoot $repoRoot -Path $EnvironmentFile
$environmentTemplatePath = "$environmentPath.example"

foreach ($requiredPath in @(
        $composeFile,
        $localComposeFile,
        $buildScript,
        $loginScript,
        $environmentHelper,
        $environmentTemplatePath
    )) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required local Docker file '$requiredPath' was not found."
    }
}

. $environmentHelper
Assert-SystemUptimeTrackerSqlEnvironmentCanReuseVolume -EnvironmentFilePath $environmentPath
[void](Initialize-SystemUptimeTrackerProductionEnvironmentFile `
        -EnvironmentFilePath $environmentPath `
        -TemplateFilePath $environmentTemplatePath)

$environmentState = Get-EnvironmentFileState -Path $environmentPath
$environmentChanged = $false

if (Test-EnvironmentValueMissing -Value $environmentState.Entries["SYSTEMUPTIMETRACKER_SQL_APP_USERNAME"]) {
    Set-EnvironmentEntryValue `
        -State $environmentState `
        -Name "SYSTEMUPTIMETRACKER_SQL_APP_USERNAME" `
        -Value "systemuptimetracker"
    $environmentChanged = $true
}

if (Test-EnvironmentValueMissingOrPlaceholder -Value $environmentState.Entries["SYSTEMUPTIMETRACKER_SQL_APP_PASSWORD"]) {
    Set-EnvironmentEntryValue `
        -State $environmentState `
        -Name "SYSTEMUPTIMETRACKER_SQL_APP_PASSWORD" `
        -Value ("Sql!{0}" -f (New-HexSecret -ByteCount 24))
    $environmentChanged = $true
}

if ($environmentChanged) {
    Save-EnvironmentFileState -State $environmentState
    $environmentState = Get-EnvironmentFileState -Path $environmentPath
    Write-Host "Completed ignored local SQL application settings in '$environmentPath'."
}

$applicationUsername = $environmentState.Entries["SYSTEMUPTIMETRACKER_SQL_APP_USERNAME"]
$applicationPassword = $environmentState.Entries["SYSTEMUPTIMETRACKER_SQL_APP_PASSWORD"]

$originalImageTag = $env:SYSTEMUPTIMETRACKER_IMAGE_TAG
$originalUiBindHost = $env:UI_BIND_HOST
$originalUiPublicPort = $env:UI_PUBLIC_PORT
$originalUiBaseUrl = $env:UI_APP_BASE_URL
$originalApiBindHost = $env:API_BIND_HOST
$originalApiPublicPort = $env:API_PUBLIC_PORT
$originalStartupMigrations = $env:API_APPLY_STARTUP_MIGRATIONS

try {
    $env:SYSTEMUPTIMETRACKER_IMAGE_TAG = $Tag
    $env:UI_BIND_HOST = "127.0.0.1"
    $env:UI_PUBLIC_PORT = "8001"
    $env:UI_APP_BASE_URL = "http://localhost:8001"
    $env:API_BIND_HOST = "127.0.0.1"
    $env:API_PUBLIC_PORT = "8002"
    $env:API_APPLY_STARTUP_MIGRATIONS = "true"

    if (-not $SkipBuild) {
        & $buildScript `
            -Configuration $Configuration `
            -Tag $Tag `
            -EnvironmentFile $environmentPath `
            -NoCache:$NoCache
        if ($LASTEXITCODE -ne 0) {
            throw "Building local SystemUptimeTracker Docker images failed."
        }
    }

    $composeArguments = @(
        "compose",
        "--project-name", "systemuptimetracker",
        "--env-file", $environmentPath,
        "-f", $composeFile,
        "-f", $localComposeFile
    )

    & docker @composeArguments up -d systemuptimetracker-sql
    if ($LASTEXITCODE -ne 0) {
        throw "Starting the local SystemUptimeTracker SQL container failed."
    }

    Wait-ContainerHealth -ContainerName "systemuptimetracker-sql"

    & $loginScript `
        -ComposeFile $composeFile `
        -EnvironmentFile $environmentPath `
        -ApplicationUsername $applicationUsername `
        -ApplicationPassword $applicationPassword `
        -ProjectName "systemuptimetracker"
    if ($LASTEXITCODE -ne 0) {
        throw "Provisioning the local SystemUptimeTracker SQL application login failed."
    }

    & docker @composeArguments up -d --remove-orphans
    if ($LASTEXITCODE -ne 0) {
        throw "Starting the full local SystemUptimeTracker Docker stack failed."
    }

    Wait-HttpHealth -Name "SystemUptimeTracker backend" -Uri "http://localhost:8002/_health"
    Wait-HttpHealth -Name "SystemUptimeTracker frontend" -Uri "http://localhost:8001/_health"
}
finally {
    $env:SYSTEMUPTIMETRACKER_IMAGE_TAG = $originalImageTag
    $env:UI_BIND_HOST = $originalUiBindHost
    $env:UI_PUBLIC_PORT = $originalUiPublicPort
    $env:UI_APP_BASE_URL = $originalUiBaseUrl
    $env:API_BIND_HOST = $originalApiBindHost
    $env:API_PUBLIC_PORT = $originalApiPublicPort
    $env:API_APPLY_STARTUP_MIGRATIONS = $originalStartupMigrations
}

Write-Host ""
Write-Host "Local SystemUptimeTracker Docker development stack is healthy." -ForegroundColor Green
Write-Host "  UI:  http://localhost:8001" -ForegroundColor Gray
Write-Host "  API: http://localhost:8002" -ForegroundColor Gray
Write-Host "  SQL: 127.0.0.1,11433" -ForegroundColor Gray
Write-Host "  Compose project: systemuptimetracker" -ForegroundColor Gray
