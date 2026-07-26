[CmdletBinding()]
param(
    [string]$SiteName = "systemuptimetracker-web-docker",
    [string]$AppPoolName = "systemuptimetracker-web-docker",
    [string]$HostName = "docker-app.example.com",
    [int]$PublicPort = 80,
    [int]$HttpsPort = 443,
    [int]$ContainerPort = 8001,
    [string]$ProxyRoot = "C:\Apps\SystemUptimeTracker\docker-proxy",
    [string]$CertificateThumbprint = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module WebAdministration
New-Item -ItemType Directory -Path $ProxyRoot -Force | Out-Null

@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <clear />
        <rule name="SystemUptimeTrackerDockerHttps" stopProcessing="true">
          <match url="(.*)" />
          <conditions>
            <add input="{HTTPS}" pattern="^on$" />
          </conditions>
          <action type="Rewrite" url="http://127.0.0.1:$ContainerPort/{R:1}" appendQueryString="true" />
          <serverVariables>
            <set name="HTTP_X_FORWARDED_PROTO" value="https" />
            <set name="HTTP_X_FORWARDED_HOST" value="{HTTP_HOST}" />
          </serverVariables>
        </rule>
        <rule name="SystemUptimeTrackerDockerHttp" stopProcessing="true">
          <match url="(.*)" />
          <action type="Rewrite" url="http://127.0.0.1:$ContainerPort/{R:1}" appendQueryString="true" />
          <serverVariables>
            <set name="HTTP_X_FORWARDED_PROTO" value="http" />
            <set name="HTTP_X_FORWARDED_HOST" value="{HTTP_HOST}" />
          </serverVariables>
        </rule>
      </rules>
    </rewrite>
  </system.webServer>
</configuration>
"@ | Set-Content -LiteralPath (Join-Path $ProxyRoot "web.config") -Encoding utf8

Set-WebConfigurationProperty `
    -PSPath "MACHINE/WEBROOT/APPHOST" `
    -Filter "system.webServer/proxy" `
    -Name enabled `
    -Value $true

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

if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    New-WebAppPool -Name $AppPoolName | Out-Null
}
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ""

if (Test-Path "IIS:\Sites\$SiteName") {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $ProxyRoot
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
}
else {
    New-Website -Name $SiteName -PhysicalPath $ProxyRoot -ApplicationPool $AppPoolName -Port $PublicPort | Out-Null
    Get-WebBinding -Name $SiteName | Remove-WebBinding
}

function Get-OrCreateBinding {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("http", "https")]
        [string]$Protocol,

        [Parameter(Mandatory = $true)]
        [int]$Port
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

    return $binding
}

$null = Get-OrCreateBinding -Protocol http -Port $PublicPort
$httpsBinding = Get-OrCreateBinding -Protocol https -Port $HttpsPort

$certificateStoreName = "WebHosting"
if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $matchingCertificate = $null
    foreach ($storeName in @("WebHosting", "My")) {
        $matchingCertificate = Get-ChildItem -Path "Cert:\LocalMachine\$storeName" -ErrorAction SilentlyContinue |
            Where-Object {
                $_.NotAfter -gt [DateTime]::UtcNow -and
                $_.HasPrivateKey -and
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
        throw "No valid local-machine certificate covers the HTTPS binding '$HostName'."
    }
    $CertificateThumbprint = $matchingCertificate.Thumbprint
}
$httpsBinding.AddSslCertificate($CertificateThumbprint, $certificateStoreName)

if ((Get-WebAppPoolState -Name $AppPoolName).Value -eq "Stopped") {
    Start-WebAppPool -Name $AppPoolName
}

if ((Get-WebsiteState -Name $SiteName).Value -ne "Started") {
    $startAttempts = 5
    for ($attempt = 1; $attempt -le $startAttempts; $attempt++) {
        try {
            Start-Website -Name $SiteName
            break
        }
        catch {
            if ($attempt -eq $startAttempts) {
                throw
            }
            Start-Sleep -Seconds 2
        }
    }
}
