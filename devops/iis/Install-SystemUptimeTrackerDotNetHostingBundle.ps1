[CmdletBinding()]
param(
    [string]$RequiredMajorVersion = "10",

    [string]$InstallerUri = "https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/10.0.10/dotnet-hosting-10.0.10-win.exe",

    [string]$DownloadRoot = $env:TEMP
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-HostingBundle {
    $aspNetCoreModulePath = Join-Path $env:ProgramFiles "IIS\Asp.Net Core Module\V2\aspnetcorev2.dll"
    $aspNetCoreRuntimeRoot = Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.AspNetCore.App"
    $requiredRuntime = Get-ChildItem -LiteralPath $aspNetCoreRuntimeRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match "^$([regex]::Escape($RequiredMajorVersion))\." } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1

    [pscustomobject]@{
        ModulePath = $aspNetCoreModulePath
        ModuleInstalled = Test-Path -LiteralPath $aspNetCoreModulePath -PathType Leaf
        RuntimeVersion = if ($requiredRuntime) { $requiredRuntime.Name } else { $null }
        RuntimeInstalled = $null -ne $requiredRuntime
    }
}

function Invoke-NetCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $process = Start-Process -FilePath (Join-Path $env:SystemRoot "System32\net.exe") `
        -ArgumentList $Arguments `
        -Wait `
        -PassThru `
        -NoNewWindow
    if ($process.ExitCode -ne 0) {
        throw "'net $($Arguments -join ' ')' failed with exit code $($process.ExitCode)."
    }
}

$currentState = Test-HostingBundle
if ($currentState.ModuleInstalled -and $currentState.RuntimeInstalled) {
    Write-Host "ASP.NET Core Hosting Bundle is ready. Runtime: $($currentState.RuntimeVersion); module: $($currentState.ModulePath)"
    return
}

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator
    )) {
    throw "Installing the ASP.NET Core Hosting Bundle requires an elevated Windows account."
}

New-Item -ItemType Directory -Path $DownloadRoot -Force | Out-Null
$installerPath = Join-Path $DownloadRoot "dotnet-hosting-win.exe"

try {
    Write-Host "Downloading the ASP.NET Core Hosting Bundle from Microsoft."
    Invoke-WebRequest -Uri $InstallerUri -OutFile $installerPath -UseBasicParsing

    $installer = Start-Process -FilePath $installerPath `
        -ArgumentList @("/install", "/quiet", "/norestart") `
        -Wait `
        -PassThru
    if ($installer.ExitCode -notin @(0, 1641, 3010)) {
        throw "ASP.NET Core Hosting Bundle installation failed with exit code $($installer.ExitCode)."
    }

    Write-Host "Restarting IIS services so the ASP.NET Core Module is loaded."
    Invoke-NetCommand -Arguments @("stop", "was", "/y")
    Invoke-NetCommand -Arguments @("start", "w3svc")

    $installedState = Test-HostingBundle
    if (-not $installedState.ModuleInstalled -or -not $installedState.RuntimeInstalled) {
        throw "Hosting Bundle validation failed. Runtime installed: $($installedState.RuntimeInstalled); IIS module installed: $($installedState.ModuleInstalled)."
    }

    Write-Host "ASP.NET Core Hosting Bundle installed. Runtime: $($installedState.RuntimeVersion); module: $($installedState.ModulePath)"
}
finally {
    Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue
}
