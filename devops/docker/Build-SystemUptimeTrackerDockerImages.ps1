[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release", "Development", "Testing", "Staging", "Production")]
    [string]$Configuration = "Production",

    [string]$Tag = "latest",

    [string]$EnvironmentFile = "devops/docker/systemuptimetracker.production.env",

    [switch]$SkipEnvironmentFile,

    [switch]$DebugAccess,

    [switch]$NoCache
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$script:CurrentStep = "Initializing"

$isWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows
)

function Write-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Detail {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host "    $Message" -ForegroundColor DarkGray
}

function Format-CommandText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Executable,

        [string[]]$Arguments = @()
    )

    $segments = @($Executable) + $Arguments
    return (($segments | ForEach-Object {
                if ($_ -match '\s') {
                    '"{0}"' -f $_.Replace('"', '\"')
                }
                else {
                    $_
                }
            }) -join ' ')
}

function Get-ExceptionMessageChain {
    param(
        [Parameter(Mandatory = $true)]
        [System.Exception]$Exception
    )

    $messages = [System.Collections.Generic.List[string]]::new()
    $currentException = $Exception

    while ($null -ne $currentException) {
        if (-not [string]::IsNullOrWhiteSpace($currentException.Message)) {
            $messages.Add($currentException.Message.Trim())
        }

        $currentException = $currentException.InnerException
    }

    return $messages.ToArray()
}

function Resolve-InputPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$InputPath
    )

    if ([System.IO.Path]::IsPathRooted($InputPath)) {
        return $InputPath
    }

    return (Join-Path $BasePath $InputPath)
}

function Write-FailureReport {
    param(
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.ErrorRecord]$ErrorRecord,

        [Parameter(Mandatory = $true)]
        [string]$ScriptName,

        [string]$EnvironmentFilePath,

        [string]$BackendImageTag,

        [string]$FrontendImageTag
    )

    Write-Host ""
    Write-Host "$ScriptName failed." -ForegroundColor Red
    Write-Host "  Step: $script:CurrentStep" -ForegroundColor Yellow

    $exceptionMessages = @(Get-ExceptionMessageChain -Exception $ErrorRecord.Exception)
    if ($exceptionMessages.Count -gt 0) {
        Write-Host "  Error: $($exceptionMessages[0])" -ForegroundColor Red

        for ($messageIndex = 1; $messageIndex -lt $exceptionMessages.Count; $messageIndex++) {
            Write-Host "  Caused by: $($exceptionMessages[$messageIndex])" -ForegroundColor DarkRed
        }
    }

    if ($ErrorRecord.InvocationInfo -and -not [string]::IsNullOrWhiteSpace($ErrorRecord.InvocationInfo.PositionMessage)) {
        Write-Host ""
        Write-Host "Location:" -ForegroundColor Yellow
        Write-Host $ErrorRecord.InvocationInfo.PositionMessage -ForegroundColor DarkGray
    }

    Write-Host ""
    Write-Host "Context:" -ForegroundColor Yellow
    if (-not [string]::IsNullOrWhiteSpace($EnvironmentFilePath)) {
        Write-Host "  Environment file: $EnvironmentFilePath" -ForegroundColor Gray
    }
    else {
        Write-Host "  Environment source: current shell environment variables" -ForegroundColor Gray
    }
    if (-not [string]::IsNullOrWhiteSpace($BackendImageTag)) {
        Write-Host "  Backend image tag: $BackendImageTag" -ForegroundColor Gray
    }

    if (-not [string]::IsNullOrWhiteSpace($FrontendImageTag)) {
        Write-Host "  Frontend image tag: $FrontendImageTag" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "Suggested next steps:" -ForegroundColor Yellow
    Write-Host "  1. Confirm Docker Desktop is running and Linux containers are enabled: docker info" -ForegroundColor Gray
    if (-not [string]::IsNullOrWhiteSpace($EnvironmentFilePath)) {
        Write-Host "  2. Validate the environment file values against the example: $EnvironmentFilePath.example" -ForegroundColor Gray
    }
    else {
        Write-Host "  2. Confirm the required SystemUptimeTracker environment variables are set in this shell before rerunning." -ForegroundColor Gray
    }

    if ($script:CurrentStep -like "Building *") {
        Write-Host "  3. Re-run with -NoCache if you suspect stale layers: .\\devops\\docker\\Build-SystemUptimeTrackerDockerImages.ps1 -NoCache" -ForegroundColor Gray
        Write-Host "  4. Review the Dockerfile and restore/build output shown above for the failing image." -ForegroundColor Gray
    }
    else {
        Write-Host "  3. Re-run the script after fixing the configuration issue above." -ForegroundColor Gray
    }
}

function Assert-CommandExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName
    )

    if (-not (Get-Command $CommandName -ErrorAction SilentlyContinue)) {
        throw "Required command '$CommandName' was not found in PATH."
    }
}

function Sync-DockerClientEnvironment {
    if (-not $isWindowsPlatform) {
        return
    }

    $persistedDockerHost = [Environment]::GetEnvironmentVariable('DOCKER_HOST', 'User')
    if (-not $persistedDockerHost) {
        $persistedDockerHost = [Environment]::GetEnvironmentVariable('DOCKER_HOST', 'Machine')
    }

    if (-not $env:DOCKER_HOST -and $persistedDockerHost) {
        $env:DOCKER_HOST = $persistedDockerHost.Trim()
    }

    if ((Test-Path Env:DOCKER_CONTEXT) -and [string]::IsNullOrWhiteSpace($env:DOCKER_CONTEXT)) {
        Remove-Item Env:DOCKER_CONTEXT -ErrorAction SilentlyContinue
    }
}

function Assert-LinuxContainerEngine {
    $dockerOsType = docker info --format '{{.OSType}}' 2>$null
    $dockerOperatingSystem = docker info --format '{{.OperatingSystem}}' 2>$null

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to determine the Docker daemon container OS type. Run 'docker info' to verify Docker Desktop is running and accessible from this shell."
    }

    if ($dockerOsType.Trim() -ne 'linux') {
        throw "This build requires Docker to run Linux containers. Current Docker daemon OSType: '$($dockerOsType.Trim())' ($($dockerOperatingSystem.Trim()))."
    }
}

function Invoke-DockerBuild {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DockerfilePath,

        [Parameter(Mandatory = $true)]
        [string]$ContextPath,

        [Parameter(Mandatory = $true)]
        [string]$ImageTag,

        [string[]]$BuildArguments = @()
    )

    $commandArgs = @("build", "--pull", "--file", $DockerfilePath, "--tag", $ImageTag)

    if ($NoCache) {
        $commandArgs += "--no-cache"
    }

    $commandArgs += $BuildArguments
    $commandArgs += $ContextPath

    Write-Detail -Message ("Command: {0}" -f (Format-CommandText -Executable 'docker' -Arguments $commandArgs))
    & docker @commandArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Docker build failed for image '$ImageTag' with exit code $LASTEXITCODE. Command: $(Format-CommandText -Executable 'docker' -Arguments $commandArgs)"
    }
}

$scriptRoot = $PSScriptRoot
if (-not $scriptRoot) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptRoot)

$environmentFilePath = $null
$environmentTemplatePath = $null
$environmentHelperScriptPath = Join-Path $repoRoot "devops/docker/SystemUptimeTracker.Environment.ps1"
$backendDockerfilePath = Join-Path $repoRoot "src/SystemUptimeTracker/SystemUptimeTracker.Api/Dockerfile"
$frontendDockerfilePath = Join-Path $repoRoot "src/SystemUptimeTracker/SystemUptimeTracker.Web/Dockerfile"
$backendImageTag = $null
$frontendImageTag = $null

if (-not $SkipEnvironmentFile) {
    $environmentFilePath = Resolve-InputPath -BasePath $repoRoot -InputPath $EnvironmentFile
    $environmentTemplatePath = "$environmentFilePath.example"
}

try {
    $script:CurrentStep = "Preparing Docker build prerequisites"
    Sync-DockerClientEnvironment
    Assert-CommandExists -CommandName "docker"
    Assert-LinuxContainerEngine

    if (-not (Test-Path -LiteralPath $environmentHelperScriptPath)) {
        throw "Environment helper script was not found at '$environmentHelperScriptPath'."
    }

    . $environmentHelperScriptPath

    if ($SkipEnvironmentFile) {
        $script:CurrentStep = "Using shell environment variables for Docker build"
        Write-Step -Message $script:CurrentStep
        Write-Warning "Skipping environment file validation. Docker build will rely on current shell environment variables and explicit script parameters."
    }
    else {
        $script:CurrentStep = "Validating Docker build environment file"
        Write-Step -Message $script:CurrentStep
        Write-Detail -Message "Environment file: $environmentFilePath"
        [void](Initialize-SystemUptimeTrackerProductionEnvironmentFile -EnvironmentFilePath $environmentFilePath -TemplateFilePath $environmentTemplatePath)
    }

    if (-not (Test-Path -LiteralPath $backendDockerfilePath)) {
        throw "Backend Dockerfile was not found at '$backendDockerfilePath'."
    }

    if (-not (Test-Path -LiteralPath $frontendDockerfilePath)) {
        throw "Frontend Dockerfile was not found at '$frontendDockerfilePath'."
    }

    # Docker repository names must be lowercase. Preserve the requested names semantically.
    $backendImageRepository = "systemuptimetracker-backend"
    $frontendImageRepository = "systemuptimetracker-frontend"
    $backendImageTag = "${backendImageRepository}:${Tag}"
    $frontendImageTag = "${frontendImageRepository}:${Tag}"

    $script:CurrentStep = "Building backend image $backendImageTag"
    Write-Step -Message $script:CurrentStep
    Write-Detail -Message "Dockerfile: $backendDockerfilePath"
    Write-Detail -Message "Context: $repoRoot"
    $backendBuildConfiguration = $Configuration
    $backendBuildArguments = @("--target", "runtime", "--build-arg", "CONFIGURATION=$backendBuildConfiguration")

    if ($DebugAccess) {
        $backendBuildConfiguration = "Debug"
        $backendBuildArguments = @("--target", "runtime-debug", "--build-arg", "CONFIGURATION=$backendBuildConfiguration")
        Write-Warning "Debug access is enabled. Building a Debug backend image with .NET diagnostics enabled."
    }

    Invoke-DockerBuild `
        -DockerfilePath $backendDockerfilePath `
        -ContextPath $repoRoot `
        -ImageTag $backendImageTag `
        -BuildArguments $backendBuildArguments

    $script:CurrentStep = "Building frontend image $frontendImageTag"
    Write-Step -Message $script:CurrentStep
    Write-Detail -Message "Dockerfile: $frontendDockerfilePath"
    Write-Detail -Message ("Context: {0}" -f (Join-Path $repoRoot "src/SystemUptimeTracker/SystemUptimeTracker.Web"))
    Invoke-DockerBuild `
        -DockerfilePath $frontendDockerfilePath `
        -ContextPath (Join-Path $repoRoot "src/SystemUptimeTracker/SystemUptimeTracker.Web") `
        -ImageTag $frontendImageTag

    Write-Host ""
    Write-Host "Docker images built successfully." -ForegroundColor Green
    Write-Host "  Backend : $backendImageTag" -ForegroundColor Gray
    Write-Host "  Frontend: $frontendImageTag" -ForegroundColor Gray
    Write-Host ""
}
catch {
    Write-FailureReport `
        -ErrorRecord $_ `
        -ScriptName "Build-SystemUptimeTrackerDockerImages.ps1" `
        -EnvironmentFilePath $environmentFilePath `
        -BackendImageTag $backendImageTag `
        -FrontendImageTag $frontendImageTag
    exit 1
}
