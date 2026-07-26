[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release", "Development", "Testing", "Staging", "Production")]
    [string]$Configuration = "Production",

    [string]$Tag = "latest",

    [string]$ComposeFile = "devops/docker/docker-compose.yml",

    [string]$EnvironmentFile = "devops/docker/systemuptimetracker.production.env",

    [switch]$SkipEnvironmentFile,

    [string]$ProjectName = "systemuptimetracker-production",

    [switch]$NoCache,

    [switch]$SkipBuild,

    [switch]$SkipStartupMigrations,

    [switch]$Detached = $true,

    [ValidateRange(1, 65535)]
    [int]$UiPublicPort = 8001,

    [ValidateNotNullOrEmpty()]
    [string]$UiBindHost = "0.0.0.0",

    [ValidateRange(1, 65535)]
    [int]$ApiPublicPort = 8002,

    [ValidateNotNullOrEmpty()]
    [string]$ApiBindHost = "127.0.0.1",

    [switch]$DebugAccess,

    [string]$UiBaseUrl
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

        [Parameter(Mandatory = $true)]
        [string]$ComposeFilePath,

        [Parameter(Mandatory = $true)]
        [string]$ProjectName,

        [string]$ResolvedUiBaseUrl,

        [string]$ComposeExecutable,

        [string[]]$ComposePrefix = @()
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
    Write-Host "  Compose file: $ComposeFilePath" -ForegroundColor Gray
    if (-not [string]::IsNullOrWhiteSpace($EnvironmentFilePath)) {
        Write-Host "  Environment file: $EnvironmentFilePath" -ForegroundColor Gray
    }
    else {
        Write-Host "  Environment source: current shell environment variables" -ForegroundColor Gray
    }
    Write-Host "  Project name: $ProjectName" -ForegroundColor Gray
    if (-not [string]::IsNullOrWhiteSpace($ResolvedUiBaseUrl)) {
        Write-Host "  Expected UI URL: $ResolvedUiBaseUrl" -ForegroundColor Gray
    }

    $composeSharedArguments = @("--project-name", $ProjectName)
    if (-not [string]::IsNullOrWhiteSpace($EnvironmentFilePath)) {
        $composeSharedArguments += @("--env-file", $EnvironmentFilePath)
    }

    $composeSharedArguments += @("-f", $ComposeFilePath)
    $composePsArguments = @($ComposePrefix + $composeSharedArguments + @("ps"))
    $composeLogsArguments = @($ComposePrefix + $composeSharedArguments + @("logs", "--tail", "200"))
    $composeConfigArguments = @($ComposePrefix + $composeSharedArguments + @("config"))

    Write-Host ""
    Write-Host "Suggested next steps:" -ForegroundColor Yellow
    Write-Host "  1. Confirm Docker Desktop is running and Linux containers are enabled: docker info" -ForegroundColor Gray
    if (-not [string]::IsNullOrWhiteSpace($EnvironmentFilePath)) {
        Write-Host "  2. Validate the environment file values against the example: $EnvironmentFilePath.example" -ForegroundColor Gray
    }
    else {
        Write-Host "  2. Confirm the required SystemUptimeTracker environment variables are set in this shell before rerunning." -ForegroundColor Gray
    }
    Write-Host "  3. Inspect the effective compose configuration: $(Format-CommandText -Executable $ComposeExecutable -Arguments $composeConfigArguments)" -ForegroundColor Gray
    Write-Host "  4. Inspect service status: $(Format-CommandText -Executable $ComposeExecutable -Arguments $composePsArguments)" -ForegroundColor Gray
    Write-Host "  5. Inspect recent container logs: $(Format-CommandText -Executable $ComposeExecutable -Arguments $composeLogsArguments)" -ForegroundColor Gray
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

function Test-DockerComposePlugin {
    & docker compose version *> $null
    return $LASTEXITCODE -eq 0
}

function Get-DockerComposeCommand {
    if (Test-DockerComposePlugin) {
        return [pscustomobject]@{
            Executable = 'docker'
            Prefix = @('compose')
        }
    }

    if (Get-Command docker-compose -ErrorAction SilentlyContinue) {
        return [pscustomobject]@{
            Executable = 'docker-compose'
            Prefix = @()
        }
    }

    throw "Neither 'docker compose' nor 'docker-compose' is available in PATH."
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
        throw "This deployment requires Docker to run Linux containers. Current Docker daemon OSType: '$($dockerOsType.Trim())' ($($dockerOperatingSystem.Trim()))."
    }
}

function Resolve-UiHostName {
    param(
        [AllowEmptyString()]
        [string]$BindHost
    )

    if ([string]::IsNullOrWhiteSpace($BindHost)) {
        return 'localhost'
    }

    switch ($BindHost.Trim()) {
        '0.0.0.0' { return 'localhost' }
        '::' { return 'localhost' }
        '[::]' { return 'localhost' }
        '*' { return 'localhost' }
        default { return $BindHost.Trim() }
    }
}

function Resolve-ApiHostName {
    param(
        [AllowEmptyString()]
        [string]$BindHost
    )

    if ([string]::IsNullOrWhiteSpace($BindHost)) {
        return 'localhost'
    }

    switch ($BindHost.Trim()) {
        '0.0.0.0' { return 'localhost' }
        '::' { return 'localhost' }
        '[::]' { return 'localhost' }
        '*' { return 'localhost' }
        default { return $BindHost.Trim() }
    }
}

function Format-UriHost {
    param(
        [Parameter(Mandatory = $true)]
        [string]$HostName
    )

    if ($HostName.Contains(':') -and -not ($HostName.StartsWith('[') -and $HostName.EndsWith(']'))) {
        return "[$HostName]"
    }

    return $HostName
}

function Resolve-UiBaseUrl {
    param(
        [Parameter(Mandatory = $true)]
        [int]$PublicPort,

        [Parameter(Mandatory = $true)]
        [string]$BindHost,

        [string]$RequestedBaseUrl
    )

    if ([string]::IsNullOrWhiteSpace($RequestedBaseUrl)) {
        $uiHostName = Resolve-UiHostName -BindHost $BindHost
        $formattedUiHostName = Format-UriHost -HostName $uiHostName
        return "http://${formattedUiHostName}:$PublicPort"
    }

    $resolvedBaseUrl = $null
    if (-not [Uri]::TryCreate($RequestedBaseUrl, [UriKind]::Absolute, [ref]$resolvedBaseUrl)) {
        throw "UiBaseUrl must be an absolute URI when provided. Received '$RequestedBaseUrl'."
    }

    return $RequestedBaseUrl.TrimEnd("/")
}

$scriptRoot = $PSScriptRoot
if (-not $scriptRoot) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptRoot)

$composeFilePath = Resolve-InputPath -BasePath $repoRoot -InputPath $ComposeFile
$environmentFilePath = $null
$environmentTemplatePath = $null
$backendDebugComposeFilePath = Join-Path $repoRoot "devops/docker/docker-compose.backend-debug.yml"
$buildDockerScriptPath = Join-Path $repoRoot "devops/docker/Build-SystemUptimeTrackerDockerImages.ps1"
$environmentHelperScriptPath = Join-Path $repoRoot "devops/docker/SystemUptimeTracker.Environment.ps1"
$dockerComposeCommand = $null
$resolvedUiBaseUrl = $null
$originalUiPublicPort = $env:UI_PUBLIC_PORT
$originalUiBindHost = $env:UI_BIND_HOST
$originalUiAppBaseUrl = $env:UI_APP_BASE_URL
$originalApiPublicPort = $env:API_PUBLIC_PORT
$originalApiBindHost = $env:API_BIND_HOST
$originalApplyStartupMigrations = $env:API_APPLY_STARTUP_MIGRATIONS

if (-not $SkipEnvironmentFile) {
    $environmentFilePath = Resolve-InputPath -BasePath $repoRoot -InputPath $EnvironmentFile
    $environmentTemplatePath = "$environmentFilePath.example"
}

try {
    $script:CurrentStep = "Preparing deployment prerequisites"
    Sync-DockerClientEnvironment
    Assert-CommandExists -CommandName "docker"
    Assert-LinuxContainerEngine
    $dockerComposeCommand = Get-DockerComposeCommand

    if (-not (Test-Path -LiteralPath $composeFilePath)) {
        throw "Compose file was not found at '$composeFilePath'."
    }

    if (-not (Test-Path -LiteralPath $buildDockerScriptPath)) {
        throw "Docker build script was not found at '$buildDockerScriptPath'."
    }

    if (-not (Test-Path -LiteralPath $environmentHelperScriptPath)) {
        throw "Environment helper script was not found at '$environmentHelperScriptPath'."
    }

    if ($DebugAccess -and -not (Test-Path -LiteralPath $backendDebugComposeFilePath)) {
        throw "Backend debug compose override was not found at '$backendDebugComposeFilePath'."
    }

    . $environmentHelperScriptPath

    if ($SkipEnvironmentFile) {
        $script:CurrentStep = "Using shell environment variables for deployment"
        Write-Step -Message $script:CurrentStep
        Write-Detail -Message "Compose file: $composeFilePath"
        Write-Warning "Skipping environment file validation. Docker Compose will rely on current shell environment variables and explicit script parameters."
    }
    else {
        $script:CurrentStep = "Validating deployment environment file"
        Write-Step -Message $script:CurrentStep
        Write-Detail -Message "Environment file: $environmentFilePath"
        Write-Detail -Message "Compose file: $composeFilePath"
        [void](Initialize-SystemUptimeTrackerProductionEnvironmentFile -EnvironmentFilePath $environmentFilePath -TemplateFilePath $environmentTemplatePath)
    }

    $resolvedUiBaseUrl = Resolve-UiBaseUrl -PublicPort $UiPublicPort -BindHost $UiBindHost -RequestedBaseUrl $UiBaseUrl

    $env:UI_PUBLIC_PORT = $UiPublicPort.ToString()
    $env:UI_BIND_HOST = $UiBindHost
    $env:UI_APP_BASE_URL = $resolvedUiBaseUrl
    $env:API_PUBLIC_PORT = $ApiPublicPort.ToString()
    $env:API_BIND_HOST = $ApiBindHost
    $env:API_APPLY_STARTUP_MIGRATIONS = if ($SkipStartupMigrations) { "false" } else { "true" }

    if (-not $SkipBuild) {
        $script:CurrentStep = "Building Docker images"
        Write-Step -Message $script:CurrentStep
        Write-Detail -Message "Invoking: $buildDockerScriptPath"
        $buildArguments = @{
            Configuration = $Configuration
            Tag = $Tag
        }

        if (-not $SkipEnvironmentFile) {
            $buildArguments.EnvironmentFile = $EnvironmentFile
        }

        if ($NoCache) {
            $buildArguments.NoCache = $true
        }

        if ($SkipEnvironmentFile) {
            $buildArguments.SkipEnvironmentFile = $true
        }

        if ($DebugAccess) {
            $buildArguments.DebugAccess = $true
        }

        & $buildDockerScriptPath @buildArguments
    }

    $script:CurrentStep = "Deploying Docker Desktop stack"
    Write-Step -Message $script:CurrentStep

    $composeArgs = @("--project-name", $ProjectName)
    if (-not $SkipEnvironmentFile) {
        $composeArgs += @("--env-file", $environmentFilePath)
    }

    $composeArgs += @("-f", $composeFilePath)
    if ($DebugAccess) {
        $composeArgs += @("-f", $backendDebugComposeFilePath)
    }

    $composeArgs += @("up", "--remove-orphans")

    if ($Detached) {
        $composeArgs += "-d"
    }

    Write-Detail -Message ("Command: {0}" -f (Format-CommandText -Executable $dockerComposeCommand.Executable -Arguments @($dockerComposeCommand.Prefix + $composeArgs)))
    & $dockerComposeCommand.Executable @($dockerComposeCommand.Prefix + $composeArgs)

    if ($LASTEXITCODE -ne 0) {
        throw "docker compose up failed with exit code $LASTEXITCODE. Command: $(Format-CommandText -Executable $dockerComposeCommand.Executable -Arguments @($dockerComposeCommand.Prefix + $composeArgs))"
    }
}
catch {
    Write-FailureReport `
        -ErrorRecord $_ `
        -ScriptName "Deploy-SystemUptimeTrackerDocker.ps1" `
        -EnvironmentFilePath $environmentFilePath `
        -ComposeFilePath $composeFilePath `
        -ProjectName $ProjectName `
        -ResolvedUiBaseUrl $resolvedUiBaseUrl `
        -ComposeExecutable $(if ($null -ne $dockerComposeCommand) { $dockerComposeCommand.Executable } else { "docker" }) `
        -ComposePrefix $(if ($null -ne $dockerComposeCommand) { $dockerComposeCommand.Prefix } else { @("compose") })
    exit 1
}
finally {
    $env:UI_PUBLIC_PORT = $originalUiPublicPort
    $env:UI_BIND_HOST = $originalUiBindHost
    $env:UI_APP_BASE_URL = $originalUiAppBaseUrl
    $env:API_PUBLIC_PORT = $originalApiPublicPort
    $env:API_BIND_HOST = $originalApiBindHost
    $env:API_APPLY_STARTUP_MIGRATIONS = $originalApplyStartupMigrations
}

Write-Host ""
Write-Host "SystemUptimeTracker deployment is running." -ForegroundColor Green
Write-Host "  UI URL: $resolvedUiBaseUrl" -ForegroundColor Gray
Write-Host "  UI bind host: $UiBindHost" -ForegroundColor Gray
Write-Host "  Public UI port: $UiPublicPort" -ForegroundColor Gray
if ($DebugAccess) {
    $apiHostName = Format-UriHost -HostName (Resolve-ApiHostName -BindHost $ApiBindHost)
    Write-Host "  Backend API URL: http://${apiHostName}:$ApiPublicPort/" -ForegroundColor Gray
    Write-Host "  API bind host: $ApiBindHost" -ForegroundColor Gray
    Write-Host "  Debug access: enabled" -ForegroundColor Yellow
}
Write-Host "  Compose file: $ComposeFile" -ForegroundColor Gray
Write-Host "  Apply pending database migrations: $(-not $SkipStartupMigrations)" -ForegroundColor Gray
if ($SkipEnvironmentFile) {
    Write-Host "  Environment source: current shell environment variables" -ForegroundColor Gray
}
else {
    Write-Host "  Environment file: $EnvironmentFile" -ForegroundColor Gray
}
Write-Host ""
if ($DebugAccess) {
    Write-Host "The backend API is published to the host only for this debug deployment. Do not use -DebugAccess for production-style runs." -ForegroundColor Yellow
}
else {
    Write-Host "The backend API is intentionally not published to the host. The UI reaches it over the shared Docker network as http://systemuptimetracker-backend:8002/." -ForegroundColor Yellow
}
