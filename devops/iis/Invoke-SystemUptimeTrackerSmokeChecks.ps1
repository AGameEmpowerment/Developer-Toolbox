[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ApiBaseUrl,

    [string]$PublicBaseUrl,

    [switch]$RunQaAutomation,

    [switch]$SkipUiAutomation,

    [switch]$SkipIdentityCleanup,

    [string]$QaProjectPath = "src/SystemUptimeTracker/SystemUptimeTracker.Qa.Automation/SystemUptimeTracker.Qa.Automation.csproj",

    [string]$QaSettingsPath = "src/SystemUptimeTracker/SystemUptimeTracker.Qa.Automation/SystemUptimeTracker.Qa.Automation.full.runsettings"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Normalize-BaseUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl
    )

    $trimmedBaseUrl = $BaseUrl.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmedBaseUrl)) {
        throw "A non-empty base URL is required."
    }

    return $trimmedBaseUrl.TrimEnd("/")
}

function Invoke-JsonRequest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri
    )

    $response = Invoke-WebRequest -Uri $Uri -Headers @{ Accept = "application/json" } -SkipHttpErrorCheck
    $content = $null

    if (-not [string]::IsNullOrWhiteSpace($response.Content)) {
        try {
            $content = $response.Content | ConvertFrom-Json
        }
        catch {
            $content = $null
        }
    }

    return [pscustomobject]@{
        StatusCode = [int]$response.StatusCode
        TraceId = $response.Headers["X-Trace-Id"]
        Content = $content
    }
}

function Resolve-QAPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [Parameter(Mandatory = $true)]
        [string]$ConfiguredPath
    )

    $trimmedPath = $ConfiguredPath.Trim()
    if ([System.IO.Path]::IsPathRooted($trimmedPath)) {
        return [System.IO.Path]::GetFullPath($trimmedPath)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $trimmedPath))
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$normalizedApiBaseUrl = Normalize-BaseUrl -BaseUrl $ApiBaseUrl
$normalizedPublicBaseUrl = if ([string]::IsNullOrWhiteSpace($PublicBaseUrl)) {
    ""
}
else {
    Normalize-BaseUrl -BaseUrl $PublicBaseUrl
}

Write-Step -Message "Validating API health and operations metadata"
$healthResponse = Invoke-JsonRequest -Uri "$normalizedApiBaseUrl/_health"
if ($healthResponse.StatusCode -ne 200) {
    throw "Health endpoint returned status $($healthResponse.StatusCode)."
}

if ($healthResponse.Content.status -ne "Healthy") {
    throw "Health endpoint reported '$($healthResponse.Content.status)' instead of 'Healthy'."
}

if ([string]::IsNullOrWhiteSpace($healthResponse.TraceId)) {
    throw "Health endpoint response did not include the X-Trace-Id header."
}

$metadataResponse = Invoke-JsonRequest -Uri "$normalizedApiBaseUrl/api/operations/metadata"
if ($metadataResponse.StatusCode -ne 200) {
    throw "Operations metadata endpoint returned status $($metadataResponse.StatusCode)."
}

if ([string]::IsNullOrWhiteSpace($metadataResponse.Content.applicationVersion)) {
    throw "Operations metadata response did not include applicationVersion."
}

if ([string]::IsNullOrWhiteSpace($metadataResponse.Content.buildVersion)) {
    throw "Operations metadata response did not include buildVersion."
}

Write-Host "Health status: $($healthResponse.Content.status)" -ForegroundColor Green
Write-Host "Health trace ID: $($healthResponse.TraceId)" -ForegroundColor Gray
Write-Host "Application version: $($metadataResponse.Content.applicationVersion)" -ForegroundColor Gray
Write-Host "Build version: $($metadataResponse.Content.buildVersion)" -ForegroundColor Gray

if (-not [string]::IsNullOrWhiteSpace($normalizedPublicBaseUrl)) {
    Write-Step -Message "Validating public web endpoint"
    $publicResponse = Invoke-WebRequest -Uri $normalizedPublicBaseUrl -SkipHttpErrorCheck
    if ([int]$publicResponse.StatusCode -lt 200 -or [int]$publicResponse.StatusCode -ge 400) {
        throw "Public site returned status $([int]$publicResponse.StatusCode)."
    }

    Write-Host "Public site responded with status $([int]$publicResponse.StatusCode)." -ForegroundColor Green
}

if ($RunQaAutomation) {
    if ([string]::IsNullOrWhiteSpace($normalizedPublicBaseUrl)) {
        throw "PublicBaseUrl is required when -RunQaAutomation is used."
    }

    $resolvedProjectPath = Resolve-QAPath -RepoRoot $repoRoot -ConfiguredPath $QaProjectPath
    $resolvedSettingsPath = Resolve-QAPath -RepoRoot $repoRoot -ConfiguredPath $QaSettingsPath

    if (-not (Test-Path -LiteralPath $resolvedProjectPath)) {
        throw "QA automation project was not found at '$resolvedProjectPath'. When running from a packaged IIS deployment, provide an absolute -QaProjectPath/-QaSettingsPath or run the script from a full repository checkout."
    }

    if (-not (Test-Path -LiteralPath $resolvedSettingsPath)) {
        throw "QA automation settings file was not found at '$resolvedSettingsPath'. When running from a packaged IIS deployment, provide an absolute -QaProjectPath/-QaSettingsPath or run the script from a full repository checkout."
    }

    Write-Step -Message "Running deployed-environment QA smoke automation"

    $originalUseExternalHost = $env:QaAutomation__UseExternalHost
    $originalWebBaseUrl = $env:QaAutomation__WebBaseUrl
    $originalSkipIdentityCleanup = $env:QaAutomation__SkipIdentityCleanup
    $originalApiBaseUrl = $env:AppSettings__BaseUrl
    $originalWebValidationBaseUrl = $env:TestConfiguration__WebValidation__BaseUrl

    try {
        $env:QaAutomation__UseExternalHost = "true"
        $env:QaAutomation__WebBaseUrl = $normalizedPublicBaseUrl
        $env:AppSettings__BaseUrl = if ($normalizedApiBaseUrl.EndsWith("/")) { $normalizedApiBaseUrl } else { "$normalizedApiBaseUrl/" }
        $env:TestConfiguration__WebValidation__BaseUrl = $normalizedPublicBaseUrl

        if ($SkipIdentityCleanup) {
            $env:QaAutomation__SkipIdentityCleanup = "true"
        }

        $filterExpression = if ($SkipUiAutomation) {
            "TestCategory=Smoke&TestCategory!=Ui"
        }
        else {
            "TestCategory=Smoke"
        }

        $dotnetArguments = @(
            "test"
            $resolvedProjectPath
            "--settings"
            $resolvedSettingsPath
            "--filter"
            $filterExpression
        )

        & dotnet @dotnetArguments
        if ($LASTEXITCODE -ne 0) {
            throw "QA smoke automation failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        $env:QaAutomation__UseExternalHost = $originalUseExternalHost
        $env:QaAutomation__WebBaseUrl = $originalWebBaseUrl
        $env:QaAutomation__SkipIdentityCleanup = $originalSkipIdentityCleanup
        $env:AppSettings__BaseUrl = $originalApiBaseUrl
        $env:TestConfiguration__WebValidation__BaseUrl = $originalWebValidationBaseUrl
    }
}
