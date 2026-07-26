[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$DeploymentPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$AppBaseUrl,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ApiBaseUrl,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$AuthCookieSecret,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{64}$')]
    [string]$ImpersonateEncryptionKey,

    [ValidateNotNullOrEmpty()]
    [string]$ImpersonatingCookie = "acting-as",

    [ValidateSet("Trace", "Debug", "Information", "Warning", "Error", "Critical", "None")]
    [string]$AppLoggingLevel = "Information",

    [string]$AppVersion = "",

    [ValidateNotNullOrEmpty()]
    [string]$AppName = "SystemUptimeTracker",

    [ValidateNotNullOrEmpty()]
    [string]$AppInsightsCloudRole = "systemuptimetracker-web",

    [switch]$ConsoleAccessToken,

    [switch]$DevEnv,

    [switch]$ShowDiagnostics,

    [switch]$AppInsightsEnabled,

    [string]$AppInsightsKey = "",

    [switch]$AppOpenTelemetryEnabled,

    [switch]$AppOpenTelemetrySeqEnabled,

    [string]$AppOpenTelemetrySeqEndpoint = "",

    [string]$AppOpenTelemetrySeqApiKey = "",

    [switch]$AppOpenTelemetryAspireEnabled,

    [string]$AppOpenTelemetryAspireEndpoint = "",

    [string]$OtelExporterOtlpEndpoint = "",

    [string]$OtelServiceName = "systemuptimetracker-web",

    [string]$OtelResourceAttributes = "",

    [string]$MicrosoftClientId = "",

    [string]$MicrosoftClientSecret = "",

    [string]$MicrosoftTenantId = "",

    [string]$MicrosoftAuthority = "",

    [string]$MicrosoftApiScope = "",

    [string]$MicrosoftAuthScopes = "",

    [string]$MicrosoftRedirectPath = "/auth/callback",

    [string]$MicrosoftPostLogoutRedirectUri = "",

    [string]$NodeExecutablePath = "C:\Program Files\nodejs\node.exe",

    [switch]$SkipIisPrerequisiteCheck
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

function Resolve-AbsolutePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return [System.IO.Path]::GetFullPath($Path)
}

function ConvertTo-EnvBoolean {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Value
    )

    if ($Value) {
        return "true"
    }

    return "false"
}

function Normalize-AppBaseUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $resolvedUri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$resolvedUri)) {
        throw "AppBaseUrl must be an absolute URI. Received '$Value'."
    }

    return $resolvedUri.ToString().TrimEnd("/")
}

function Normalize-ApiBaseUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $resolvedUri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$resolvedUri)) {
        throw "ApiBaseUrl must be an absolute URI. Received '$Value'."
    }

    $normalizedValue = $resolvedUri.ToString().TrimEnd("/")
    return "$normalizedValue/"
}

function Assert-ParameterConsistency {
    if ($AppInsightsEnabled -and [string]::IsNullOrWhiteSpace($AppInsightsKey)) {
        throw "AppInsightsKey is required when AppInsightsEnabled is specified."
    }

    if (($AppOpenTelemetrySeqEnabled -or $AppOpenTelemetryAspireEnabled) -and -not $AppOpenTelemetryEnabled) {
        throw "AppOpenTelemetryEnabled must be specified when enabling Seq or Aspire OpenTelemetry sinks."
    }

    if ($AppOpenTelemetrySeqEnabled -and [string]::IsNullOrWhiteSpace($AppOpenTelemetrySeqEndpoint)) {
        throw "AppOpenTelemetrySeqEndpoint is required when AppOpenTelemetrySeqEnabled is specified."
    }

    if ($AuthCookieSecret.Length -lt 32) {
        Write-Warning "AuthCookieSecret is shorter than 32 characters. Use a high-entropy secret for production deployments."
    }
}

function Test-NodeVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath
    )

    $versionOutput = & $ExecutablePath --version 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($versionOutput)) {
        return $null
    }

    $trimmedVersion = $versionOutput.Trim()
    $parsedVersion = $null
    if (-not [Version]::TryParse($trimmedVersion.TrimStart("v"), [ref]$parsedVersion)) {
        return $null
    }

    return [pscustomobject]@{
        Raw = $trimmedVersion
        Parsed = $parsedVersion
    }
}

function Test-IisNodeHostingPrerequisites {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResolvedDeploymentPath,

        [Parameter(Mandatory = $true)]
        [string]$ResolvedNodeExecutablePath
    )

    $issues = [System.Collections.Generic.List[string]]::new()
    $envTemplatePath = Join-Path $ResolvedDeploymentPath ".env.example"
    $serverScriptPath = Join-Path $ResolvedDeploymentPath "server.js"

    if (-not (Test-Path -LiteralPath $ResolvedDeploymentPath)) {
        $issues.Add("Deployment path '$ResolvedDeploymentPath' does not exist. Deploy the System-Uptime-Tracker-Web package before running this script.")
        return $issues
    }

    if (-not (Test-Path -LiteralPath $envTemplatePath)) {
        $issues.Add("The deployment path is missing '.env.example'. Make sure the web deployment package was extracted to '$ResolvedDeploymentPath'.")
    }

    if (-not (Test-Path -LiteralPath $serverScriptPath)) {
        $issues.Add("The deployment path is missing 'server.js'. Deploy the standalone SystemUptimeTracker.Web artifact before configuring IIS.")
    }

    if (-not (Test-Path -LiteralPath $ResolvedNodeExecutablePath)) {
        $issues.Add("Node.js was not found at '$ResolvedNodeExecutablePath'. Install Node.js 24.x on the server or pass the correct -NodeExecutablePath value.")
        return $issues
    }

    $nodeVersion = Test-NodeVersion -ExecutablePath $ResolvedNodeExecutablePath
    if ($null -eq $nodeVersion) {
        $issues.Add("Node.js could not be executed from '$ResolvedNodeExecutablePath'. Reinstall Node.js 24.x and verify the service account can execute node.exe.")
    }
    elseif ($nodeVersion.Parsed.Major -ne 24) {
        $issues.Add("Node.js version $($nodeVersion.Raw) is installed, but SystemUptimeTracker.Web requires Node.js 24.x. Install Node.js 24.x and redeploy.")
    }

    try {
        Import-Module WebAdministration -ErrorAction Stop
    }
    catch {
        $issues.Add("The IIS PowerShell module 'WebAdministration' is unavailable. Install IIS Management Scripts and Tools on the target server.")
        return $issues
    }

    try {
        $rewriteModule = Get-WebGlobalModule -Name "RewriteModule" -ErrorAction Stop
        if ($null -eq $rewriteModule) {
            $issues.Add("IIS URL Rewrite is not installed. Install IIS URL Rewrite Module 2.1 on the target server.")
        }
    }
    catch {
        $issues.Add("IIS URL Rewrite is not installed or could not be queried. Install IIS URL Rewrite Module 2.1 on the target server.")
    }

    try {
        $proxySettings = Get-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Filter "system.webServer/proxy" -Name "." -ErrorAction Stop
        if ($null -eq $proxySettings) {
            $issues.Add("IIS Application Request Routing proxy settings are unavailable. Install Application Request Routing 3.x on the target server.")
        }
        elseif (-not $proxySettings.enabled) {
            $issues.Add("IIS Application Request Routing is installed but proxying is disabled. Enable it with: Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' -Name enabled -Value true")
        }
    }
    catch {
        $issues.Add("IIS Application Request Routing was not detected. Install Application Request Routing 3.x and enable proxy support.")
    }

    return $issues
}

function Set-EnvironmentEntry {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IList]$Lines,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    $entryValue = "$Name=$Value"
    $escapedName = [regex]::Escape($Name)
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -match "^${escapedName}=") {
            $Lines[$index] = $entryValue
            return
        }
    }

    [void]$Lines.Add($entryValue)
}

Assert-ParameterConsistency

$resolvedDeploymentPath = Resolve-AbsolutePath -Path $DeploymentPath
$resolvedNodeExecutablePath = Resolve-AbsolutePath -Path $NodeExecutablePath
$envTemplatePath = Join-Path $resolvedDeploymentPath ".env.example"
$envFilePath = Join-Path $resolvedDeploymentPath ".env"

if (-not $SkipIisPrerequisiteCheck) {
    Write-Step -Message "Validating IIS and Node.js hosting prerequisites"
    $prerequisiteIssues = @(
        Test-IisNodeHostingPrerequisites `
            -ResolvedDeploymentPath $resolvedDeploymentPath `
            -ResolvedNodeExecutablePath $resolvedNodeExecutablePath
    )

    if ($prerequisiteIssues.Count -gt 0) {
        foreach ($issue in $prerequisiteIssues) {
            Write-Host "ERROR: $issue" -ForegroundColor Red
        }

        throw "IIS and Node.js hosting prerequisites were not satisfied. Resolve the reported issues and run the script again."
    }
}
else {
    if (-not (Test-Path -LiteralPath $resolvedDeploymentPath)) {
        throw "Deployment path '$resolvedDeploymentPath' does not exist."
    }

    if (-not (Test-Path -LiteralPath $envTemplatePath)) {
        throw "Environment template was not found at '$envTemplatePath'."
    }
}

$normalizedAppBaseUrl = Normalize-AppBaseUrl -Value $AppBaseUrl
$normalizedApiBaseUrl = Normalize-ApiBaseUrl -Value $ApiBaseUrl

Write-Step -Message "Creating deployment environment file"
if ($PSCmdlet.ShouldProcess($envFilePath, "Create SystemUptimeTracker.Web .env file from template")) {
    Copy-Item -LiteralPath $envTemplatePath -Destination $envFilePath -Force

    $envLines = [System.Collections.ArrayList]::new()
    foreach ($line in Get-Content -LiteralPath $envFilePath) {
        [void]$envLines.Add($line)
    }

    Set-EnvironmentEntry -Lines $envLines -Name "API_BASE_URL" -Value $normalizedApiBaseUrl
    Set-EnvironmentEntry -Lines $envLines -Name "APP_BASE_URL" -Value $normalizedAppBaseUrl
    Set-EnvironmentEntry -Lines $envLines -Name "NEXT_PUBLIC_PROFILE_ROUTE" -Value "/api/me"
    Set-EnvironmentEntry -Lines $envLines -Name "NEXT_PUBLIC_ACCESS_TOKEN_ROUTE" -Value "auth/token"
    Set-EnvironmentEntry -Lines $envLines -Name "CONSOLE_ACCESSTOKEN" -Value (ConvertTo-EnvBoolean -Value $ConsoleAccessToken.IsPresent)
    Set-EnvironmentEntry -Lines $envLines -Name "IMPERSONATING_COOKIE" -Value $ImpersonatingCookie
    Set-EnvironmentEntry -Lines $envLines -Name "NEXT_PUBLIC_IMPERSONATING_COOKIE" -Value '${IMPERSONATING_COOKIE}'
    Set-EnvironmentEntry -Lines $envLines -Name "IMPERSONATE_ENCRYPTION_KEY" -Value $ImpersonateEncryptionKey.ToLowerInvariant()
    Set-EnvironmentEntry -Lines $envLines -Name "DEV_ENV" -Value (ConvertTo-EnvBoolean -Value $DevEnv.IsPresent)
    Set-EnvironmentEntry -Lines $envLines -Name "SHOW_DIAGNOSTICS" -Value (ConvertTo-EnvBoolean -Value $ShowDiagnostics.IsPresent)
    Set-EnvironmentEntry -Lines $envLines -Name "APP_LOGGING_LEVEL" -Value ('"{0}"' -f $AppLoggingLevel)
    Set-EnvironmentEntry -Lines $envLines -Name "APP_INSIGHTS_ENABLED" -Value (ConvertTo-EnvBoolean -Value $AppInsightsEnabled.IsPresent)
    Set-EnvironmentEntry -Lines $envLines -Name "APP_INSIGHTS_KEY" -Value ('"{0}"' -f $AppInsightsKey)
    Set-EnvironmentEntry -Lines $envLines -Name "APP_OPEN_TELEMETRY_ENABLED" -Value (ConvertTo-EnvBoolean -Value $AppOpenTelemetryEnabled.IsPresent)
    Set-EnvironmentEntry -Lines $envLines -Name "APP_OPEN_TELEMETRY_SEQ_ENABLED" -Value (ConvertTo-EnvBoolean -Value $AppOpenTelemetrySeqEnabled.IsPresent)
    Set-EnvironmentEntry -Lines $envLines -Name "APP_OPEN_TELEMETRY_SEQ_ENDPOINT" -Value $AppOpenTelemetrySeqEndpoint
    Set-EnvironmentEntry -Lines $envLines -Name "APP_OPEN_TELEMETRY_SEQ_API_KEY" -Value $AppOpenTelemetrySeqApiKey
    Set-EnvironmentEntry -Lines $envLines -Name "APP_OPEN_TELEMETRY_ASPIRE_ENABLED" -Value (ConvertTo-EnvBoolean -Value $AppOpenTelemetryAspireEnabled.IsPresent)
    Set-EnvironmentEntry -Lines $envLines -Name "APP_OPEN_TELEMETRY_ASPIRE_ENDPOINT" -Value $AppOpenTelemetryAspireEndpoint
    Set-EnvironmentEntry -Lines $envLines -Name "OTEL_EXPORTER_OTLP_ENDPOINT" -Value $OtelExporterOtlpEndpoint
    Set-EnvironmentEntry -Lines $envLines -Name "OTEL_SERVICE_NAME" -Value $OtelServiceName
    Set-EnvironmentEntry -Lines $envLines -Name "OTEL_RESOURCE_ATTRIBUTES" -Value $OtelResourceAttributes
    Set-EnvironmentEntry -Lines $envLines -Name "APP_NAME" -Value ('"{0}"' -f $AppName)
    Set-EnvironmentEntry -Lines $envLines -Name "APP_INSIGHTS_CLOUD_ROLE" -Value ('"{0}"' -f $AppInsightsCloudRole)
    Set-EnvironmentEntry -Lines $envLines -Name "AUTH_COOKIE_SECRET" -Value $AuthCookieSecret
    Set-EnvironmentEntry -Lines $envLines -Name "MICROSOFT_CLIENT_ID" -Value $MicrosoftClientId
    Set-EnvironmentEntry -Lines $envLines -Name "MICROSOFT_CLIENT_SECRET" -Value $MicrosoftClientSecret
    Set-EnvironmentEntry -Lines $envLines -Name "MICROSOFT_TENANT_ID" -Value $MicrosoftTenantId
    Set-EnvironmentEntry -Lines $envLines -Name "MICROSOFT_AUTHORITY" -Value $MicrosoftAuthority
    Set-EnvironmentEntry -Lines $envLines -Name "MICROSOFT_API_SCOPE" -Value $MicrosoftApiScope
    Set-EnvironmentEntry -Lines $envLines -Name "MICROSOFT_AUTH_SCOPES" -Value $MicrosoftAuthScopes
    Set-EnvironmentEntry -Lines $envLines -Name "MICROSOFT_REDIRECT_PATH" -Value $MicrosoftRedirectPath
    Set-EnvironmentEntry -Lines $envLines -Name "MICROSOFT_POST_LOGOUT_REDIRECT_URI" -Value $MicrosoftPostLogoutRedirectUri

    if (-not [string]::IsNullOrWhiteSpace($AppVersion)) {
        Set-EnvironmentEntry -Lines $envLines -Name "APP_VERSION" -Value ('"{0}"' -f $AppVersion)
    }

    $envLines | Set-Content -LiteralPath $envFilePath -Encoding utf8
}

[pscustomobject]@{
    EnvironmentFilePath = $envFilePath
    DeploymentPath = $resolvedDeploymentPath
    AppBaseUrl = $normalizedAppBaseUrl
    ApiBaseUrl = $normalizedApiBaseUrl
    NodeExecutablePath = $resolvedNodeExecutablePath
    IisPrerequisiteCheckSkipped = $SkipIisPrerequisiteCheck.IsPresent
}
