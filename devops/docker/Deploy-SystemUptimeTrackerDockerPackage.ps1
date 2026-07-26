[CmdletBinding()]
param(
    [string]$Tag = "latest",

    [string]$PackageRoot = $PSScriptRoot,

    [string]$EnvironmentFile = "",

    [string]$ProjectName = "systemuptimetracker-production",

    [switch]$NoCache,

    [switch]$SkipStartupMigrations,

    [switch]$Detached = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-CommandExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName
    )

    if (-not (Get-Command $CommandName -ErrorAction SilentlyContinue)) {
        throw "Required command '$CommandName' was not found in PATH."
    }
}

function Invoke-DockerBuild {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContextPath,

        [Parameter(Mandatory = $true)]
        [string]$ImageTag
    )

    $arguments = @("build", "--tag", $ImageTag)
    if ($NoCache) {
        $arguments += "--no-cache"
    }

    $arguments += $ContextPath
    & docker @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker build failed for '$ImageTag' with exit code $LASTEXITCODE."
    }
}

$packageRoot = (Resolve-Path -LiteralPath $PackageRoot).Path
$composeFilePath = Join-Path $packageRoot "docker-compose.production.yml"
$environmentTemplatePath = Join-Path $packageRoot "systemuptimetracker.production.env.example"
$environmentHelperPath = Join-Path $packageRoot "SystemUptimeTracker.Environment.ps1"
$backendContextPath = Join-Path $packageRoot "backend"
$frontendContextPath = Join-Path $packageRoot "frontend"
$resolvedEnvironmentFile = if ([string]::IsNullOrWhiteSpace($EnvironmentFile)) {
    Join-Path $packageRoot "systemuptimetracker.production.env"
}
elseif ([System.IO.Path]::IsPathRooted($EnvironmentFile)) {
    $EnvironmentFile
}
else {
    Join-Path $packageRoot $EnvironmentFile
}

Assert-CommandExists -CommandName "docker"

foreach ($requiredPath in @(
    $composeFilePath,
    $environmentTemplatePath,
    $environmentHelperPath,
    $backendContextPath,
    $frontendContextPath
)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required Docker package path was not found: '$requiredPath'."
    }
}

. $environmentHelperPath
[void](Initialize-SystemUptimeTrackerProductionEnvironmentFile `
    -EnvironmentFilePath $resolvedEnvironmentFile `
    -TemplateFilePath $environmentTemplatePath)

$backendImageTag = "systemuptimetracker-backend:$Tag"
$frontendImageTag = "systemuptimetracker-frontend:$Tag"

Write-Host "Building $backendImageTag..." -ForegroundColor Cyan
Invoke-DockerBuild -ContextPath $backendContextPath -ImageTag $backendImageTag

Write-Host "Building $frontendImageTag..." -ForegroundColor Cyan
Invoke-DockerBuild -ContextPath $frontendContextPath -ImageTag $frontendImageTag

$originalImageTag = $env:SYSTEMUPTIMETRACKER_IMAGE_TAG
$originalApplyStartupMigrations = $env:API_APPLY_STARTUP_MIGRATIONS

try {
    $env:SYSTEMUPTIMETRACKER_IMAGE_TAG = $Tag
    $env:API_APPLY_STARTUP_MIGRATIONS = if ($SkipStartupMigrations) { "false" } else { "true" }

    & docker compose version *> $null
    if ($LASTEXITCODE -eq 0) {
        $composeExecutable = "docker"
        $composePrefix = @("compose")
    }
    elseif (Get-Command docker-compose -ErrorAction SilentlyContinue) {
        $composeExecutable = "docker-compose"
        $composePrefix = @()
    }
    else {
        throw "Neither 'docker compose' nor 'docker-compose' is available in PATH."
    }

    $composeArguments = @($composePrefix) + @(
        "--project-name", $ProjectName,
        "--env-file", $resolvedEnvironmentFile,
        "-f", $composeFilePath,
        "up",
        "--remove-orphans"
    )

    if ($Detached) {
        $composeArguments += "--detach"
    }

    & $composeExecutable @composeArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Compose deployment failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:SYSTEMUPTIMETRACKER_IMAGE_TAG = $originalImageTag
    $env:API_APPLY_STARTUP_MIGRATIONS = $originalApplyStartupMigrations
}

Write-Host "SystemUptimeTracker Docker package deployed successfully." -ForegroundColor Green
Write-Host "SQL volume: systemuptimetracker-production-sql-data (unless overridden in the environment file)." -ForegroundColor Gray
