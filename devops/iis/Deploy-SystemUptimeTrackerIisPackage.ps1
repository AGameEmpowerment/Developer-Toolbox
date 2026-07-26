[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot,

    [string]$ReleaseRoot = "C:\Apps\SystemUptimeTracker\releases",

    [string]$DataRoot = "D:\AppData\SystemUptimeTracker",

    [string]$ApiSiteName = "systemuptimetracker-api",

    [string]$ApiAppPoolName = "systemuptimetracker-api",

    [string]$PublicSiteName = "systemuptimetracker-web",

    [string]$PublicAppPoolName = "systemuptimetracker-web",

    [ValidateRange(1, 65535)]
    [int]$ApiPort = 5101,

    [ValidateRange(1, 65535)]
    [int]$WebCompanionPort = 3101,

    [ValidateRange(1, 65535)]
    [int]$PublicPort = 443,

    [ValidateSet("http", "https")]
    [string]$PublicProtocol = "https",

    [string]$PublicHostName = "systemuptimetracker.example",

    [string]$CertificateThumbprint = "",

    [string]$NodeExecutablePath = "C:\Program Files\nodejs\node.exe",

    [string]$PublicBaseUrl = "",

    [string]$ApiBaseUrl = "",

    [string]$ApiPublicHostName = "",

    [ValidateRange(1, 65535)]
    [int]$ApiPublicPort = 443,

    [ValidateSet("http", "https")]
    [string]$ApiPublicProtocol = "https",

    [string]$ApiCertificateThumbprint = "",

    [string]$AppLoggingLevel = "Information",

    [switch]$RunSmokeChecks,

    [switch]$RunQaAutomation,

    [string]$QaProjectPath = "src/SystemUptimeTracker/SystemUptimeTracker.Qa.Automation/SystemUptimeTracker.Qa.Automation.csproj",

    [string]$QaSettingsPath = "src/SystemUptimeTracker/SystemUptimeTracker.Qa.Automation/SystemUptimeTracker.Qa.Automation.full.runsettings"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Import-Module WebAdministration

function Write-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Resolve-ReleasePackageRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $candidateRoots = @(
        $Root,
        (Join-Path $Root "iis-package")
    )

    foreach ($candidateRoot in $candidateRoots) {
        if (((Test-Path -LiteralPath (Join-Path $candidateRoot "api")) -or
                (Test-Path -LiteralPath (Join-Path $candidateRoot "server-package"))) -and
            ((Test-Path -LiteralPath (Join-Path $candidateRoot "web")) -or
                (Test-Path -LiteralPath (Join-Path $candidateRoot "web-package"))) -and
            (Test-Path -LiteralPath (Join-Path $candidateRoot "database")) -and
            ((Test-Path -LiteralPath (Join-Path $candidateRoot "deployment")) -or
                (Test-Path -LiteralPath (Join-Path $candidateRoot "deployment-assets")))) {
            return (Resolve-Path -LiteralPath $candidateRoot).Path
        }
    }

    throw "PackageRoot '$Root' does not contain a supported IIS deployment package. Expected either api/web/database/deployment or server-package/web-package/database/deployment-assets folders."
}

function Resolve-PackageContentPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string[]]$CandidateNames
    )

    foreach ($candidateName in $CandidateNames) {
        $candidatePath = Join-Path $Root $candidateName
        if (Test-Path -LiteralPath $candidatePath) {
            return $candidatePath
        }
    }

    throw "Package root '$Root' does not contain any of the expected paths: $($CandidateNames -join ', ')."
}

function Ensure-Directory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Remove-PathIfExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        for ($attempt = 1; $attempt -le 5; $attempt++) {
            try {
                Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
                return
            }
            catch {
                if ($attempt -eq 5) {
                    throw
                }
                Start-Sleep -Seconds 2
            }
        }
    }
}

function Copy-DirectoryContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    Ensure-Directory -Path $DestinationPath
    Copy-Item -Path (Join-Path $SourcePath "*") -Destination $DestinationPath -Recurse -Force
}

function Ensure-WebAppPool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if (-not (Test-Path -LiteralPath "IIS:\AppPools\$Name")) {
        New-WebAppPool -Name $Name | Out-Null
    }

    Set-ItemProperty -LiteralPath "IIS:\AppPools\$Name" -Name managedRuntimeVersion -Value ""
    Set-ItemProperty -LiteralPath "IIS:\AppPools\$Name" -Name processModel.identityType -Value 4
}

function Set-SystemUptimeTrackerWebsite {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$PhysicalPath,

        [Parameter(Mandatory = $true)]
        [string]$ApplicationPool,

        [Parameter(Mandatory = $true)]
        [int]$Port,

        [Parameter(Mandatory = $true)]
        [string]$Protocol,

        [Parameter(Mandatory = $true)]
        [string]$HostName
    )

    $bindingInformation = "*:${Port}:$HostName"

    if (Get-Website -Name $Name -ErrorAction SilentlyContinue) {
        Set-ItemProperty -LiteralPath "IIS:\Sites\$Name" -Name physicalPath -Value $PhysicalPath
        Set-ItemProperty -LiteralPath "IIS:\Sites\$Name" -Name applicationPool -Value $ApplicationPool

        Get-WebBinding -Name $Name | Remove-WebBinding
        New-WebBinding `
            -Name $Name `
            -Protocol $Protocol `
            -IPAddress "*" `
            -Port $Port `
            -HostHeader $HostName `
            -SslFlags ($(if ($Protocol -eq "https") { 1 } else { 0 })) | Out-Null
        return
    }

    New-Website -Name $Name -PhysicalPath $PhysicalPath -ApplicationPool $ApplicationPool -Port 80 | Out-Null
    Get-WebBinding -Name $Name | Remove-WebBinding
    New-WebBinding `
        -Name $Name `
        -Protocol $Protocol `
        -IPAddress "*" `
        -Port $Port `
        -HostHeader $HostName `
        -SslFlags ($(if ($Protocol -eq "https") { 1 } else { 0 })) | Out-Null
}

function Ensure-HostBinding {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SiteName,

        [Parameter(Mandatory = $true)]
        [ValidateSet("http", "https")]
        [string]$Protocol,

        [Parameter(Mandatory = $true)]
        [int]$Port,

        [Parameter(Mandatory = $true)]
        [string]$HostName,

        [string]$CertificateThumbprint = ""
    )

    $bindingInformation = "*:${Port}:$HostName"
    $binding = Get-WebBinding -Name $SiteName -Protocol $Protocol |
        Where-Object { $_.bindingInformation -eq $bindingInformation } |
        Select-Object -First 1

    if ($null -eq $binding) {
        New-WebBinding `
            -Name $SiteName `
            -Protocol $Protocol `
            -Port $Port `
            -HostHeader $HostName `
            -SslFlags ($(if ($Protocol -eq "https") { 1 } else { 0 })) | Out-Null
        $binding = Get-WebBinding -Name $SiteName -Protocol $Protocol |
            Where-Object { $_.bindingInformation -eq $bindingInformation } |
            Select-Object -First 1
    }

    if ($Protocol -eq "https") {
        $certificateStoreName = "MY"
        if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
            $matchingCertificate = $null
            foreach ($storeName in @("My", "WebHosting")) {
                $matchingCertificate = Get-ChildItem -Path "Cert:\LocalMachine\$storeName" -ErrorAction SilentlyContinue |
                    Where-Object {
                        $_.NotAfter -gt [DateTime]::UtcNow -and
                        $_.DnsNameList.Unicode -contains $HostName
                    } |
                    Sort-Object NotAfter -Descending |
                    Select-Object -First 1

                if ($null -ne $matchingCertificate) {
                    $certificateStoreName = $storeName
                    break
                }
            }

            if ($null -eq $matchingCertificate) {
                $hostParts = $HostName.Split(".", 2)
                $wildcardName = if ($hostParts.Count -eq 2) { "*.$($hostParts[1])" } else { "" }
                foreach ($storeName in @("My", "WebHosting")) {
                    $matchingCertificate = Get-ChildItem -Path "Cert:\LocalMachine\$storeName" -ErrorAction SilentlyContinue |
                        Where-Object {
                            $_.NotAfter -gt [DateTime]::UtcNow -and
                            $_.DnsNameList.Unicode -contains $wildcardName
                        } |
                        Sort-Object NotAfter -Descending |
                        Select-Object -First 1

                    if ($null -ne $matchingCertificate) {
                        $certificateStoreName = $storeName
                        break
                    }
                }
            }

            if ($null -eq $matchingCertificate) {
                throw "No valid local-machine certificate covers the HTTPS binding '$HostName'."
            }

            $CertificateThumbprint = $matchingCertificate.Thumbprint
            Write-Host "Using certificate '$CertificateThumbprint' from '$certificateStoreName' for '$HostName'."
        }

        $binding.AddSslCertificate($CertificateThumbprint, $certificateStoreName)
    }
}

function Enable-IisReverseProxy {
    Set-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Filter "system.webServer/proxy" -Name enabled -Value $true

    $allowedServerVariablesFilter = "system.webServer/rewrite/allowedServerVariables"
    $allowedServerVariables = Get-WebConfigurationProperty `
        -PSPath "MACHINE/WEBROOT/APPHOST" `
        -Filter $allowedServerVariablesFilter `
        -Name "."

    foreach ($variableName in @("HTTP_X_FORWARDED_PROTO", "HTTP_X_FORWARDED_HOST")) {
        $existingVariable = $allowedServerVariables.Collection |
            Where-Object { [string]$_["name"] -eq $variableName } |
            Select-Object -First 1
        if ($null -eq $existingVariable) {
            Add-WebConfigurationProperty `
                -PSPath "MACHINE/WEBROOT/APPHOST" `
                -Filter $allowedServerVariablesFilter `
                -Name "." `
                -Value @{ name = $variableName }
        }
    }
}

function Write-ReverseProxyConfig {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [int]$WebCompanionPort,

        [Parameter(Mandatory = $true)]
        [ValidateSet("http", "https")]
        [string]$PublicProtocol
    )

    $content = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <clear />
        <rule name="SystemUptimeTrackerWebCompanion" stopProcessing="true">
          <match url="(.*)" />
          <action type="Rewrite" url="http://127.0.0.1:$WebCompanionPort/{R:1}" appendQueryString="true" />
          <serverVariables>
            <set name="HTTP_X_FORWARDED_PROTO" value="$PublicProtocol" />
            <set name="HTTP_X_FORWARDED_HOST" value="{HTTP_HOST}" />
          </serverVariables>
        </rule>
      </rules>
    </rewrite>
  </system.webServer>
</configuration>
"@

    Set-Content -LiteralPath $Path -Value $content
}

function Write-WebCompanionLauncher {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$WebRoot,

        [Parameter(Mandatory = $true)]
        [string]$NodePath,

        [Parameter(Mandatory = $true)]
        [int]$Port,

        [Parameter(Mandatory = $true)]
        [string]$ResolvedPublicBaseUrl,

        [Parameter(Mandatory = $true)]
        [string]$ResolvedApiBaseUrl,

        [Parameter(Mandatory = $true)]
        [string]$LoggingLevel
    )

    $launcher = @"
[CmdletBinding()]
param()

Set-StrictMode -Version Latest

Set-Location "$WebRoot"



`$env:NODE_ENV = "production"
`$env:HOSTNAME = "127.0.0.1"
`$env:PORT = "$Port"
`$env:APP_BASE_URL = "$ResolvedPublicBaseUrl"
`$env:API_BASE_URL = "$ResolvedApiBaseUrl"
`$env:APP_LOGGING_LEVEL = "$LoggingLevel"

& "$NodePath" ".\server.js"
"@

    Set-Content -LiteralPath $Path -Value $launcher
}

$resolvedPackageRoot = Resolve-ReleasePackageRoot -Root $PackageRoot
$manifestPath = Join-Path $resolvedPackageRoot "release-manifest.json"
$manifest = if (Test-Path -LiteralPath $manifestPath) {
    Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
}
else {
    $null
}

$releaseVersion = if ($null -ne $manifest -and -not [string]::IsNullOrWhiteSpace($manifest.buildVersion)) {
    $manifest.buildVersion
}
else {
    Get-Date -Format "yyyyMMddHHmmss"
}

$apiPackagePath = Resolve-PackageContentPath -Root $resolvedPackageRoot -CandidateNames @("api", "server-package")
$webPackagePath = Resolve-PackageContentPath -Root $resolvedPackageRoot -CandidateNames @("web", "web-package")
$databasePackagePath = Resolve-PackageContentPath -Root $resolvedPackageRoot -CandidateNames @("database")
$deploymentPackagePath = Resolve-PackageContentPath -Root $resolvedPackageRoot -CandidateNames @("deployment", "deployment-assets")

$releasePath = Join-Path $ReleaseRoot $releaseVersion
$apiReleasePath = Join-Path $releasePath "api"
$webReleasePath = Join-Path $releasePath "web"
$databaseReleasePath = Join-Path $releasePath "database"
$deploymentReleasePath = Join-Path $releasePath "deployment"
$proxyReleasePath = Join-Path $releasePath "proxy"
$dataProtectionPath = Join-Path $DataRoot "DataProtection-Keys"

$resolvedPublicBaseUrl = if ([string]::IsNullOrWhiteSpace($PublicBaseUrl)) {
    "${PublicProtocol}://$PublicHostName" + ($(if (($PublicProtocol -eq "http" -and $PublicPort -ne 80) -or ($PublicProtocol -eq "https" -and $PublicPort -ne 443)) { ":$PublicPort" } else { "" }))
}
else {
    $PublicBaseUrl.TrimEnd("/")
}

$resolvedApiBaseUrl = if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    "http://127.0.0.1:$ApiPort"
}
else {
    $ApiBaseUrl.TrimEnd("/")
}

Write-Step -Message "Preparing release folder $releasePath"
if ($PSCmdlet.ShouldProcess($releasePath, "Stage SystemUptimeTracker IIS release")) {
    Stop-ScheduledTask -TaskName "SystemUptimeTracker.Web" -ErrorAction SilentlyContinue
    foreach ($appPoolName in @($ApiAppPoolName, $PublicAppPoolName)) {
        if (
            (Test-Path -LiteralPath "IIS:\AppPools\$appPoolName") -and
            (Get-WebAppPoolState -Name $appPoolName).Value -ne "Stopped"
        ) {
            Stop-WebAppPool -Name $appPoolName
        }
    }

    Remove-PathIfExists -Path $releasePath

    Ensure-Directory -Path $releasePath
    Ensure-Directory -Path $DataRoot
    Ensure-Directory -Path (Join-Path $DataRoot "Assets")
    Ensure-Directory -Path (Join-Path $DataRoot "Logs")
    Ensure-Directory -Path (Join-Path $DataRoot "Temp")
    Ensure-Directory -Path $dataProtectionPath

    Copy-DirectoryContent -SourcePath $apiPackagePath -DestinationPath $apiReleasePath
    Copy-DirectoryContent -SourcePath $webPackagePath -DestinationPath $webReleasePath
    Copy-DirectoryContent -SourcePath $databasePackagePath -DestinationPath $databaseReleasePath
    Copy-DirectoryContent -SourcePath $deploymentPackagePath -DestinationPath $deploymentReleasePath
    Ensure-Directory -Path $proxyReleasePath

    $apiWebConfigPath = Join-Path $apiReleasePath "web.config"
    if (Test-Path -LiteralPath $apiWebConfigPath) {
        $apiWebConfig = Get-Content -LiteralPath $apiWebConfigPath -Raw
        $apiWebConfig = $apiWebConfig.Replace('stdoutLogEnabled="false"', 'stdoutLogEnabled="true"')
        Set-Content -LiteralPath $apiWebConfigPath -Value $apiWebConfig -Encoding UTF8

        $apiLogPath = Join-Path $apiReleasePath "logs"
        Ensure-Directory -Path $apiLogPath
        & icacls.exe $apiLogPath /grant "IIS AppPool\${ApiAppPoolName}:(OI)(CI)M" /T /C | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to grant the API app pool write access to '$apiLogPath'."
        }
    }

    Write-ReverseProxyConfig -Path (Join-Path $proxyReleasePath "web.config") -WebCompanionPort $WebCompanionPort -PublicProtocol $PublicProtocol
    Write-WebCompanionLauncher `
        -Path (Join-Path $deploymentReleasePath "Start-SystemUptimeTrackerWeb.ps1") `
        -WebRoot $webReleasePath `
        -NodePath $NodeExecutablePath `
        -Port $WebCompanionPort `
        -ResolvedPublicBaseUrl $resolvedPublicBaseUrl `
        -ResolvedApiBaseUrl $resolvedApiBaseUrl `
        -LoggingLevel $AppLoggingLevel
}

Write-Step -Message "Configuring IIS app pools and sites"
Enable-IisReverseProxy

if ($PSCmdlet.ShouldProcess("IIS", "Configure SystemUptimeTracker API and reverse-proxy sites")) {
    foreach ($retiredSiteName in @("SystemUptimeTracker.Api.Internal", "SystemUptimeTracker.Web")) {
        if (Test-Path -LiteralPath "IIS:\Sites\$retiredSiteName") {
            Remove-Website -Name $retiredSiteName
        }
    }
    foreach ($retiredAppPoolName in @("SystemUptimeTracker.Api", "SystemUptimeTracker.WebProxy")) {
        if (Test-Path -LiteralPath "IIS:\AppPools\$retiredAppPoolName") {
            Remove-WebAppPool -Name $retiredAppPoolName
        }
    }

    Ensure-WebAppPool -Name $ApiAppPoolName
    Ensure-WebAppPool -Name $PublicAppPoolName

    Set-SystemUptimeTrackerWebsite `
        $ApiSiteName `
        $apiReleasePath `
        $ApiAppPoolName `
        $ApiPort `
        "http" `
        "localhost"

    Set-SystemUptimeTrackerWebsite `
        $PublicSiteName `
        $proxyReleasePath `
        $PublicAppPoolName `
        $PublicPort `
        $PublicProtocol `
        $PublicHostName

    Ensure-HostBinding `
        -SiteName $PublicSiteName `
        -Protocol $PublicProtocol `
        -Port $PublicPort `
        -HostName $PublicHostName `
        -CertificateThumbprint $CertificateThumbprint

    if (-not [string]::IsNullOrWhiteSpace($ApiPublicHostName)) {
        Ensure-HostBinding `
            -SiteName $ApiSiteName `
            -Protocol $ApiPublicProtocol `
            -Port $ApiPublicPort `
            -HostName $ApiPublicHostName `
            -CertificateThumbprint $ApiCertificateThumbprint
    }

    Start-Website -Name $ApiSiteName
    Start-Website -Name $PublicSiteName
}

Write-Host ""
Write-Host "SystemUptimeTracker IIS release staged successfully." -ForegroundColor Green
Write-Host "Release version: $releaseVersion" -ForegroundColor Gray
Write-Host "Release path: $releasePath" -ForegroundColor Gray
Write-Host "API site: $ApiSiteName -> $apiReleasePath" -ForegroundColor Gray
Write-Host "Public site: $PublicSiteName -> $proxyReleasePath" -ForegroundColor Gray
Write-Host "Data root: $DataRoot" -ForegroundColor Gray
Write-Host "Prepared optional Data Protection key-ring folder: $dataProtectionPath" -ForegroundColor Gray
Write-Host "Database scripts: $databaseReleasePath" -ForegroundColor Gray
Write-Host "Web companion launcher: $(Join-Path $deploymentReleasePath 'Start-SystemUptimeTrackerWeb.ps1')" -ForegroundColor Gray
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Apply the SQL scripts in '$databaseReleasePath' if they have not already been applied." -ForegroundColor Yellow
Write-Host "2. Start the companion web process by running the generated Start-SystemUptimeTrackerWeb.ps1 script under your chosen Windows service or scheduled-task wrapper." -ForegroundColor Yellow
Write-Host "3. Provide IIS/app-pool environment variables for secrets, connection strings, and any lane-specific overrides, including DataProtection__KeyRingPath if you want the API to use '$dataProtectionPath'." -ForegroundColor Yellow

if ($RunSmokeChecks) {
    $smokeScriptPath = Join-Path $deploymentReleasePath "Invoke-SystemUptimeTrackerSmokeChecks.ps1"
    if (-not (Test-Path -LiteralPath $smokeScriptPath)) {
        throw "Smoke script was not found at '$smokeScriptPath'."
    }

    Write-Step -Message "Running post-deploy smoke checks"

    $smokeArgs = @(
        "-ApiBaseUrl", $resolvedApiBaseUrl,
        "-PublicBaseUrl", $resolvedPublicBaseUrl
    )

    if ($RunQaAutomation) {
        $smokeArgs += @(
            "-RunQaAutomation",
            "-QaProjectPath", $QaProjectPath,
            "-QaSettingsPath", $QaSettingsPath
        )
    }

    & pwsh -File $smokeScriptPath @smokeArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Post-deploy smoke checks failed with exit code $LASTEXITCODE."
    }
}
