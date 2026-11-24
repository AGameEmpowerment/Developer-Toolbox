<#
.SYNOPSIS
    Creates local development HTTPS certificates and isolated trust files.
.PARAMETER Action
    Initialize creates or reuses a CA. Issue creates a server key and certificate.
    Trust creates a separate copy of the CA certificate for one client or run.
.PARAMETER CaDirectory
    CA directory. Defaults to TOOLBOX_LOCAL_CA_DIR or containers/.local-ca.
.PARAMETER OutputDirectory
    Destination directory for issued files or the isolated CA certificate.
.PARAMETER Name
    Server certificate file name without an extension.
.PARAMETER DnsName
    Additional local DNS names. The certificate always covers localhost,
    127.0.0.1 and ::1.
.PARAMETER ValidityDays
    Server certificate lifetime in days.
.PARAMETER ContainerRuntime
    Fallback engine when a host Bash and OpenSSL are unavailable.
.EXAMPLE
    .\New-LocalHttpsCertificate.ps1 -Action Issue -OutputDirectory .\generated -DnsName app.test
.NOTES
    Set TOOLBOX_LOCAL_HTTPS_PFX_PASSWORD in the process environment to also
    export a password-protected PFX file.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Initialize', 'Issue', 'Trust')]
    [string]$Action,
    [string]$CaDirectory = $(if ($env:TOOLBOX_LOCAL_CA_DIR) { $env:TOOLBOX_LOCAL_CA_DIR } else { Join-Path $PSScriptRoot '../.local-ca' }),
    [string]$OutputDirectory,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,62}$')]
    [string]$Name = 'localhost',
    [string[]]$DnsName = @(),
    [ValidateRange(1, 999)]
    [int]$ValidityDays = 365,
    [ValidateSet('auto', 'docker', 'podman')]
    [string]$ContainerRuntime = $(if ($env:CONTAINER_RUNTIME) { $env:CONTAINER_RUNTIME } else { 'auto' })
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$isWindowsPlatform = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
$caPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($CaDirectory)
$rootPath = Join-Path $caPath 'root-ca.crt'
$outputPath = ''
if ($Action -in @('Issue', 'Trust')) {
    if (-not $OutputDirectory) { throw "The $Action action requires -OutputDirectory." }
    $outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
}

if ($Action -eq 'Trust') {
    if (-not (Test-Path -LiteralPath $rootPath -PathType Leaf)) { throw "Initialize the local CA first: $caPath" }
    [System.IO.Directory]::CreateDirectory($outputPath) | Out-Null
    $trustPath = Join-Path $outputPath 'root-ca.crt'
    [System.IO.File]::Copy($rootPath, $trustPath)
    return $trustPath
}

$scriptPath = Join-Path $PSScriptRoot 'local-https.sh'
$bash = Get-Command bash -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if ($isWindowsPlatform) {
    # The Windows system Bash launcher enters WSL and cannot consume native paths.
    if ($bash -like "$env:SystemRoot\*") { $bash = $null }
    if (-not $bash) {
        $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
        if ($git) {
            $gitBash = Join-Path (Split-Path -Parent (Split-Path -Parent $git)) 'bin/bash.exe'
            if (Test-Path -LiteralPath $gitBash -PathType Leaf) { $bash = $gitBash }
        }
    }
}
$actionName = $Action.ToLowerInvariant()
$options = @('--name', $Name, '--days', $ValidityDays.ToString(), '--runtime', $ContainerRuntime)
foreach ($hostName in $DnsName) { $options += @('--host', $hostName.ToLowerInvariant()) }

if ($bash) {
    $arguments = @($scriptPath, $actionName, '--ca-dir', $caPath)
    if ($outputPath) { $arguments += @('--output-dir', $outputPath) }
    $arguments += $options
    & $bash @arguments | Out-Null
} else {
    if ($ContainerRuntime -eq 'auto') {
        foreach ($candidate in @('docker', 'podman')) {
            if (Get-Command $candidate -CommandType Application -ErrorAction SilentlyContinue) {
                & $candidate info *> $null
                if ($LASTEXITCODE -eq 0) { $ContainerRuntime = $candidate; break }
            }
        }
    }
    if ($ContainerRuntime -eq 'auto' -or -not (Get-Command $ContainerRuntime -ErrorAction SilentlyContinue)) {
        throw 'Certificate generation requires Git Bash, Bash, or a reachable Docker/Podman engine.'
    }
    [System.IO.Directory]::CreateDirectory($caPath) | Out-Null
    . (Join-Path $PSScriptRoot '../../.devcontainer/ensure_image.ps1')
    Ensure-ToolboxImage -Runtime $ContainerRuntime -Image 'wiremock/wiremock:latest'
    $mountMode = if ($ContainerRuntime -eq 'podman') { 'rw,z' } else { 'rw' }
    $scriptMountMode = if ($ContainerRuntime -eq 'podman') { 'ro,z' } else { 'ro' }
    $arguments = @('run', '--rm', '--network', 'none', '--entrypoint', 'bash',
        '--env', 'TOOLBOX_LOCAL_HTTPS_PFX_PASSWORD',
        '--volume', "${scriptPath}:/tmp/local-https.sh:$scriptMountMode",
        '--volume', "${caPath}:/tmp/local-ca:$mountMode")
    if ($ContainerRuntime -eq 'podman') {
        $arguments += @('--userns', 'keep-id')
    } elseif (-not $isWindowsPlatform) {
        $userId = (& id -u).Trim()
        $groupId = (& id -g).Trim()
        $arguments += @('--user', "${userId}:${groupId}")
    }
    $scriptArguments = @($actionName, '--ca-dir', '/tmp/local-ca')
    if ($outputPath) {
        [System.IO.Directory]::CreateDirectory($outputPath) | Out-Null
        $arguments += @('--volume', "${outputPath}:/tmp/local-output:$mountMode")
        $scriptArguments += @('--output-dir', '/tmp/local-output')
    }
    & $ContainerRuntime @arguments 'wiremock/wiremock:latest' '/tmp/local-https.sh' @scriptArguments @options | Out-Null
}
if ($LASTEXITCODE -ne 0) { throw "Local HTTPS $actionName failed with exit code $LASTEXITCODE." }
if ($Action -eq 'Initialize') { return $rootPath }
return (Join-Path $outputPath "$Name.crt")
