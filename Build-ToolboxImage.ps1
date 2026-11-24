#Requires -Version 7.3
<#
.SYNOPSIS
Builds and tags an image in the selected local Docker or Podman image store.
.DESCRIPTION
Local developer tooling. Uses a local build context and Dockerfile, without
starting application services or publishing images. Run without arguments to
build the default oidc profile. Relative Dockerfile paths are resolved against
ContextPath; ContextPath is relative to the caller.
.PARAMETER Profile
Named Toolbox build profile; defaults to oidc when no custom context is given.
oidc builds the reusable local Keycloak base from this checkout as
localhost/toolbox-oidc-keycloak:latest, reusing its cached upstream image.
.PARAMETER ContextPath
Existing local directory to send as the build context.
.PARAMETER ImageName
Local repository name without a tag. The script always builds its latest tag.
.PARAMETER Dockerfile
Dockerfile path, relative to ContextPath unless absolute. Defaults to Dockerfile.
.PARAMETER ContainerRuntime
Docker, Podman, or auto (first reachable engine, Docker preferred).
Defaults to CONTAINER_RUNTIME when set, otherwise auto.
.PARAMETER BuildArgument
Non-secret build arguments as a hashtable. Values are passed as literal arguments.
.PARAMETER Platform
Optional single target platform, for example linux/amd64.
.PARAMETER Target
Optional Dockerfile build stage.
.PARAMETER NoCache
Build without reusing cached layers.
.PARAMETER PassThru
Return image ID, tag, engine and source paths after a successful local build.
.EXAMPLE
./Build-ToolboxImage.ps1
.EXAMPLE
./Build-ToolboxImage.ps1 -Profile oidc -ContainerRuntime docker -PassThru
.EXAMPLE
./Build-ToolboxImage.ps1 -ContextPath ./containers/aws -ImageName localhost/toolbox-toolbox-aws -ContainerRuntime docker
.EXAMPLE
./Build-ToolboxImage.ps1 -ContextPath ../MyProject/devops/identity -ImageName localhost/my-project-identity -ContainerRuntime podman -BuildArgument @{ OIDC_BASE_IMAGE = 'localhost/toolbox-oidc-keycloak:latest' }
.OUTPUTS
None by default. PSCustomObject with PassThru. Build logs go to the host.
.NOTES
Requires the selected engine to be running. The base and derived images must
use the same engine, connection/context and image store. No registry push occurs.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low', DefaultParameterSetName = 'Profile')]
param(
    [Parameter(ParameterSetName = 'Profile')]
    [ValidateSet('oidc')]
    [string]$Profile = 'oidc',

    [Parameter(Mandatory, ParameterSetName = 'Custom')]
    [ValidateNotNullOrEmpty()]
    [string]$ContextPath,

    [Parameter(Mandatory, ParameterSetName = 'Custom')]
    [ValidatePattern('^[a-z0-9][a-z0-9._:/-]*$')]
    [string]$ImageName,

    [Parameter(ParameterSetName = 'Custom')]
    [ValidateNotNullOrEmpty()]
    [string]$Dockerfile = 'Dockerfile',

    [ValidateSet('auto', 'docker', 'podman')]
    [string]$ContainerRuntime = $(if ($env:CONTAINER_RUNTIME) { $env:CONTAINER_RUNTIME } else { 'auto' }),

    [hashtable]$BuildArgument = @{},

    [ValidatePattern('^[a-z0-9]+/[a-z0-9_]+(/[A-Za-z0-9_-]+)?$')]
    [string]$Platform,

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_.-]*$')]
    [string]$Target,

    [switch]$NoCache,
    [switch]$PassThru
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
# Preserve embedded quotes even when the caller uses legacy native parsing.
$PSNativeCommandArgumentPassing = 'Standard'

if ($PSCmdlet.ParameterSetName -eq 'Profile') {
    $ContextPath = Join-Path $PSScriptRoot 'containers/oidc/keycloak'
    $ImageName = 'localhost/toolbox-oidc-keycloak'
    # Keep caller arguments untouched and allow explicit provenance overrides.
    $BuildArgument = $BuildArgument.Clone()
    if (-not $BuildArgument.ContainsKey('SOURCE_REVISION')) {
        $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($git) {
            $revision = & $git.Source -C $PSScriptRoot rev-parse --verify HEAD 2>$null
            if ($LASTEXITCODE -eq 0 -and [string]$revision -match '^[a-fA-F0-9]{40,64}$') {
                $BuildArgument.SOURCE_REVISION = ([string]$revision).Trim()
            }
        }
    }
}

if (($ImageName -split '/')[-1].Contains(':')) {
    throw 'ImageName must omit a tag; this tool always builds :latest.'
}
$imageTag = $ImageName + ':latest'

$contextItem = Get-Item -LiteralPath $ContextPath
if ($contextItem.PSProvider.Name -ne 'FileSystem' -or -not $contextItem.PSIsContainer) {
    throw 'ContextPath must be an existing local directory.'
}
$contextDirectory = $contextItem.FullName
$dockerfilePath = if ([System.IO.Path]::IsPathRooted($Dockerfile)) {
    $Dockerfile
} else {
    Join-Path $contextDirectory $Dockerfile
}
$dockerfileItem = Get-Item -LiteralPath $dockerfilePath
if ($dockerfileItem.PSProvider.Name -ne 'FileSystem' -or $dockerfileItem.PSIsContainer) {
    throw 'Dockerfile must be an existing local file.'
}

$buildArguments = [System.Collections.Generic.List[string]]::new()
$buildArguments.Add('build')
$buildArguments.Add('--file')
$buildArguments.Add($dockerfileItem.FullName)
$buildArguments.Add('--tag')
$buildArguments.Add($imageTag)
foreach ($key in @($BuildArgument.Keys | Sort-Object)) {
    if ([string]$key -notmatch '^[A-Za-z_][A-Za-z0-9_]*$' -or $null -eq $BuildArgument[$key]) {
        throw 'BuildArgument requires valid variable names and non-null values.'
    }
    $buildArguments.Add('--build-arg')
    $buildArguments.Add(('{0}={1}' -f $key, $BuildArgument[$key]))
}
if ($Platform) { $buildArguments.Add('--platform'); $buildArguments.Add($Platform) }
if ($Target) { $buildArguments.Add('--target'); $buildArguments.Add($Target) }
if ($NoCache) { $buildArguments.Add('--no-cache') }

if (-not $PSCmdlet.ShouldProcess($imageTag, "Build local image from '$contextDirectory' using '$ContainerRuntime'")) {
    return
}

# Image builds need an engine, but no Compose provider or shared service startup.
$candidates = if ($ContainerRuntime -eq 'auto') { @('docker', 'podman') } else { @($ContainerRuntime) }
$selectedRuntime = $null
foreach ($candidate in $candidates) {
    if (-not (Get-Command $candidate -ErrorAction SilentlyContinue)) { continue }
    try {
        & $candidate info *> $null
        if ($LASTEXITCODE -eq 0) { $selectedRuntime = $candidate; break }
    } catch {
        Write-Verbose "The '$candidate' engine is unavailable."
    }
}
if (-not $selectedRuntime) {
    throw "No reachable '$ContainerRuntime' container engine. Start Docker or Podman, or select a running engine with -ContainerRuntime."
}

if ($selectedRuntime -eq 'docker') {
    # Ensure the result is loaded locally even when a BuildKit builder is selected.
    $buildArguments.Add('--load')
}
if ($PSCmdlet.ParameterSetName -eq 'Profile') {
    . (Join-Path $PSScriptRoot '.devcontainer/ensure_image.ps1')
    $upstream = if ($BuildArgument.ContainsKey('UPSTREAM_IMAGE')) { $BuildArgument.UPSTREAM_IMAGE } else { 'quay.io/keycloak/keycloak:latest' }
    Ensure-ToolboxImage -Runtime $selectedRuntime -Image $upstream
}
$buildArguments.Add($contextDirectory)

Write-Verbose "Building '$imageTag' in '$selectedRuntime' from '$($dockerfileItem.FullName)'."
& $selectedRuntime @buildArguments | Out-Host
$buildExitCode = $LASTEXITCODE
if ($buildExitCode -ne 0) {
    throw "The '$selectedRuntime' image build failed with exit code $buildExitCode."
}
$imageId = & $selectedRuntime image inspect --format '{{.Id}}' $imageTag
if ($LASTEXITCODE -ne 0 -or -not $imageId) {
    throw "Build completed but '$imageTag' is unavailable in the selected local image store."
}
if ($PassThru) {
    [pscustomobject]@{
        Tag = $imageTag
        ImageId = ([string]$imageId).Trim()
        ContainerRuntime = $selectedRuntime
        ContextPath = $contextDirectory
        Dockerfile = $dockerfileItem.FullName
    }
}
