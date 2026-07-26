[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ApiAppPoolName,

    [string]$PublicAppPoolName = "systemuptimetracker-web",

    [Parameter(Mandatory = $true)]
    [string]$WebDeploymentPath,

    [Parameter(Mandatory = $true)]
    [string]$WebLauncherPath,

    [string]$WebTaskName = "SystemUptimeTracker.Web",

    [string]$DataRoot = "D:\AppData\SystemUptimeTracker",

    [string]$AppBaseUrl = "https://app.example.com",

    [string]$ApiBaseUrl = "https://api.example.com",

    [string]$AppVersion = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module WebAdministration
$iisAdministrationAssembly = Join-Path $env:WINDIR "System32\inetsrv\Microsoft.Web.Administration.dll"
if (-not (Test-Path -LiteralPath $iisAdministrationAssembly)) {
    throw "The IIS administration assembly was not found at '$iisAdministrationAssembly'."
}
Add-Type -Path $iisAdministrationAssembly

$requiredEnvironmentVariables = @(
    "ConnectionStrings__DefaultConnection",
    "RedactionKey",
    "UI_AUTH_COOKIE_SECRET",
    "UI_IMPERSONATE_ENCRYPTION_KEY"
)

foreach ($name in $requiredEnvironmentVariables) {
    $value = [Environment]::GetEnvironmentVariable($name, "Process")
    if ([string]::IsNullOrWhiteSpace($value) -or $value -match "^\$\(.+\)$") {
        throw "Required deployment environment variable '$name' was not provided."
    }
}

$apiEnvironment = [ordered]@{
    "ASPNETCORE_ENVIRONMENT" = "Production"
    "SystemUptimeTracker__ApplyStartupMigrations" = "true"
    "ConnectionStrings__DefaultConnection" = $env:ConnectionStrings__DefaultConnection
    "RedactionKey" = $env:RedactionKey
    "DataProtection__ApplicationName" = "SystemUptimeTracker"
    "DataProtection__KeyRingPath" = (Join-Path $DataRoot "DataProtection-Keys")
    "DataProtection__ProtectKeysWithDpapi" = "true"
    "Cors__AllowedOrigins__0" = $AppBaseUrl
}

$optionalApiEnvironmentMap = [ordered]@{
    "API_ALLOWED_HOSTS" = "AllowedHosts"
    "API_KEYVAULT_URI" = "KeyVaultUri"
    "API_APPLICATIONINSIGHTS_CONNECTION_STRING" = "ConnectionStrings__ApplicationInsights"
    "API_FEATURE_ASPIRE_ENABLED" = "FeatureManagement__AspireEnabled"
    "API_FEATURE_CONFIGURATIONINFO_ENABLED" = "FeatureManagement__ConfigurationInfoEnabled"
    "API_FEATURE_INFOENDPOINT_ENABLED" = "FeatureManagement__InfoEndpointEnabled"
    "API_FEATURE_OPENAPI_ENABLED" = "FeatureManagement__OpenApiEnabled"
    "API_FEATURE_OPENTELEMETRY_ENABLED" = "FeatureManagement__OpenTelemetryEnabled"
    "API_FEATURE_OPENTELEMETRY_SEQ_ENABLED" = "FeatureManagement__OpenTelemetrySeqEnabled"
    "API_OPENTELEMETRY_ENDPOINT" = "OpenTelemetry__Endpoint"
    "API_OPENTELEMETRY_APIKEY" = "OpenTelemetry__ApiKey"
    "API_AUTH_JWT_ENABLED" = "Auth__Jwt__Enabled"
    "API_AUTH_JWT_ISSUER" = "Auth__Jwt__Issuer"
    "API_AUTH_JWT_AUDIENCE" = "Auth__Jwt__Audience"
    "API_AUTH_JWT_SIGNINGKEY" = "Auth__Jwt__SigningKey"
    "API_AUTH_JWT_CLOCKSKEW_SECONDS" = "Auth__Jwt__ClockSkewSeconds"
    "API_REDIS_URL" = "Redis__Url"
    "API_REDIS_INSTANCENAME" = "Redis__InstanceName"
}

foreach ($entry in $optionalApiEnvironmentMap.GetEnumerator()) {
    $value = [Environment]::GetEnvironmentVariable($entry.Key, "Process")
    if (-not [string]::IsNullOrWhiteSpace($value) -and $value -notmatch "^\$\(.+\)$") {
        $apiEnvironment[$entry.Value] = $value
    }
}

$serverManager = [Microsoft.Web.Administration.ServerManager]::new()
try {
    $appPool = $serverManager.ApplicationPools[$ApiAppPoolName]
    if ($null -eq $appPool) {
        throw "IIS application pool '$ApiAppPoolName' was not found."
    }

    $environmentVariables = $appPool.GetCollection("environmentVariables")
    foreach ($entry in $apiEnvironment.GetEnumerator()) {
        $existing = $environmentVariables |
            Where-Object { [string]$_["name"] -eq $entry.Key } |
            Select-Object -First 1
        if ($null -eq $existing) {
            $existing = $environmentVariables.CreateElement("add")
            $existing["name"] = $entry.Key
            $existing["value"] = [string]$entry.Value
            [void]$environmentVariables.Add($existing)
        }
        else {
            $existing["value"] = [string]$entry.Value
        }
    }

    $serverManager.CommitChanges()
}
finally {
    $serverManager.Dispose()
}

& (Join-Path $PSScriptRoot "Initialize-SystemUptimeTrackerWebEnvironment.ps1") `
    -DeploymentPath $WebDeploymentPath `
    -AppBaseUrl $AppBaseUrl `
    -ApiBaseUrl $ApiBaseUrl `
    -AuthCookieSecret $env:UI_AUTH_COOKIE_SECRET `
    -ImpersonateEncryptionKey $env:UI_IMPERSONATE_ENCRYPTION_KEY `
    -AppVersion $AppVersion

$pwshExecutable = (Get-Command pwsh.exe -ErrorAction Stop).Source
$taskAction = New-ScheduledTaskAction `
    -Execute $pwshExecutable `
    -Argument "-NoLogo -NoProfile -File `"$WebLauncherPath`""
$taskTrigger = New-ScheduledTaskTrigger -AtStartup
$taskSettings = New-ScheduledTaskSettingsSet `
    -RestartCount 5 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -StartWhenAvailable
$taskPrincipal = New-ScheduledTaskPrincipal `
    -UserId "SYSTEM" `
    -LogonType ServiceAccount `
    -RunLevel Highest

Stop-ScheduledTask -TaskName $WebTaskName -ErrorAction SilentlyContinue
Register-ScheduledTask `
    -TaskName $WebTaskName `
    -Action $taskAction `
    -Trigger $taskTrigger `
    -Settings $taskSettings `
    -Principal $taskPrincipal `
    -Force | Out-Null
Start-ScheduledTask -TaskName $WebTaskName

foreach ($appPoolName in @($ApiAppPoolName, $PublicAppPoolName)) {
    $appPoolState = (Get-WebAppPoolState -Name $appPoolName).Value
    if ($appPoolState -eq "Stopped") {
        Start-WebAppPool -Name $appPoolName
    }
    else {
        Restart-WebAppPool -Name $appPoolName
    }
}
