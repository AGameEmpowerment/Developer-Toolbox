[CmdletBinding()]
param(
    [string]$Version = "v5.1.2",
    [string]$InstallRoot = "C:\Apps\SystemUptimeTracker\tools"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

& docker compose version *> $null
if ($LASTEXITCODE -eq 0) {
    Write-Host "The Docker Compose CLI plugin is already available."
    return
}

$existingStandalone = Get-Command docker-compose -ErrorAction SilentlyContinue
if ($existingStandalone) {
    Write-Host "Docker Compose standalone is already available at '$($existingStandalone.Source)'."
    return
}

New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
$destinationPath = Join-Path $InstallRoot "docker-compose.exe"
$downloadUri = "https://github.com/docker/compose/releases/download/$Version/docker-compose-windows-x86_64.exe"
$temporaryPath = "$destinationPath.download"

try {
    Invoke-WebRequest -Uri $downloadUri -OutFile $temporaryPath
    Move-Item -LiteralPath $temporaryPath -Destination $destinationPath -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}

$env:PATH = "$InstallRoot;$env:PATH"
Write-Host "##vso[task.prependpath]$InstallRoot"

& $destinationPath version
if ($LASTEXITCODE -ne 0) {
    throw "Docker Compose installation validation failed."
}

Write-Host "Docker Compose $Version is available at '$destinationPath'."
