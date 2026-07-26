[CmdletBinding()]
param(
    [ValidateSet("Development", "Testing", "Staging", "Production")]
    [string]$Configuration = "Production",

    [string]$OutputRoot = "artifacts/publish",

    [string]$BuildVersion = "",

    [switch]$SkipDotnetRestore,

    [switch]$SkipNpmCi
)

$ErrorActionPreference = "Stop"

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
    & $Action
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

function Remove-PathIfExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    if (Test-Path -LiteralPath $LiteralPath) {
        Remove-Item -LiteralPath $LiteralPath -Recurse -Force
    }
}

function Copy-DirectoryContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        return
    }

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
    Copy-Item -Path (Join-Path $SourcePath "*") -Destination $DestinationPath -Recurse -Force
}

function Get-DotnetEfCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ToolRoot
    )

    $existingCommand = Get-Command "dotnet-ef" -ErrorAction SilentlyContinue
    if ($null -ne $existingCommand) {
        return $existingCommand.Source
    }

    New-Item -ItemType Directory -Path $ToolRoot -Force | Out-Null
    dotnet tool install dotnet-ef --tool-path $ToolRoot --version 10.* | Out-Host

    $commandName = if ($IsWindows) { "dotnet-ef.exe" } else { "dotnet-ef" }
    $resolvedCommandPath = Join-Path $ToolRoot $commandName
    if (-not (Test-Path -LiteralPath $resolvedCommandPath)) {
        throw "The dotnet-ef tool could not be resolved at '$resolvedCommandPath'."
    }

    return $resolvedCommandPath
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$devopsRoot = Split-Path -Parent $scriptRoot
$repoRoot = Split-Path -Parent $devopsRoot
$solutionPath = Join-Path $repoRoot "SystemUptimeTracker.sln"
$apiProjectPath = Join-Path $repoRoot "src/SystemUptimeTracker/SystemUptimeTracker.Api/SystemUptimeTracker.Api.csproj"
$dataProjectPath = Join-Path $repoRoot "src/SystemUptimeTracker/SystemUptimeTracker.Data/SystemUptimeTracker.Data.csproj"
$webProjectRoot = Join-Path $repoRoot "src/SystemUptimeTracker/SystemUptimeTracker.Web"
$composeFilePath = Join-Path $repoRoot "devops/docker/docker-compose.yml"
$composeDebugFilePath = Join-Path $repoRoot "devops/docker/docker-compose.backend-debug.yml"
$composeEnvTemplatePath = Join-Path $repoRoot "devops/docker/systemuptimetracker.production.env.example"
$composeEnvironmentHelperPath = Join-Path $repoRoot "devops/docker/SystemUptimeTracker.Environment.ps1"
$packageDeploymentScriptPath = Join-Path $repoRoot "devops/docker/Deploy-SystemUptimeTrackerDockerPackage.ps1"
$environmentWriterPath = Join-Path $repoRoot "devops/docker/New-SystemUptimeTrackerDockerEnvironment.ps1"
$iisProxyScriptPath = Join-Path $repoRoot "devops/docker/Configure-SystemUptimeTrackerDockerIisProxy.ps1"
$databaseBackupScriptPath = Join-Path $repoRoot "devops/docker/Backup-SystemUptimeTrackerDatabase.ps1"
$sqlApplicationLoginScriptPath = Join-Path $repoRoot "devops/docker/Set-SystemUptimeTrackerSqlApplicationLogin.ps1"
$databaseResetScriptPath = Join-Path $repoRoot "devops/docker/Reset-SystemUptimeTrackerDockerDatabase.ps1"
$databaseMigrationTestScriptPath = Join-Path $repoRoot "devops/docker/Test-SystemUptimeTrackerDockerMigrations.ps1"
$webEnvTemplatePath = Join-Path $webProjectRoot ".env.example"

$laneRoot = Join-Path (Join-Path $repoRoot $OutputRoot) $Configuration
$dockerArtifactRoot = Join-Path $laneRoot "docker-package"
$backendArtifactRoot = Join-Path $dockerArtifactRoot "backend"
$backendAppRoot = Join-Path $backendArtifactRoot "app"
$frontendArtifactRoot = Join-Path $dockerArtifactRoot "frontend"
$frontendAppRoot = Join-Path $frontendArtifactRoot "app"
$databaseArtifactRoot = Join-Path $dockerArtifactRoot "database"
$manifestPath = Join-Path $dockerArtifactRoot "docker-artifact-manifest.json"
$dockerZipPath = Join-Path $laneRoot "docker-package.zip"
$toolRoot = Join-Path $laneRoot ".tools"
$designTimeConnectionString = "Server=127.0.0.1,1433;Database=SystemUptimeTracker_DesignTime;User Id=sa;Password=P@ssword123!;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=true"

$resolvedBuildVersion = if ([string]::IsNullOrWhiteSpace($BuildVersion)) {
    "0.0.1-local"
}
else {
    $BuildVersion
}

$gitSha = ""
try {
    $gitSha = (git -C $repoRoot rev-parse HEAD).Trim()
}
catch {
    $gitSha = ""
}

$backendDockerfile = @'
# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8002 \
    ASPNETCORE_HTTP_PORTS=8002 \
    DOTNET_EnableDiagnostics=0

COPY app/ ./

EXPOSE 8002

USER $APP_UID

ENTRYPOINT ["dotnet", "SystemUptimeTracker.Api.dll"]
'@

$frontendDockerfile = @'
# syntax=docker/dockerfile:1.7

FROM node:24-alpine AS runtime
WORKDIR /app

ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3001 \
    PATH=/app/node_modules/.bin:$PATH

RUN apk add --no-cache libc6-compat \
    && addgroup -S appgroup \
    && adduser -S appuser -G appgroup -h /app

COPY app/ ./

RUN chown -R appuser:appgroup /app

EXPOSE 3001

USER appuser

CMD ["node", "server.js"]
'@

Assert-CommandExists -CommandName "dotnet"
Assert-CommandExists -CommandName "npm"

Invoke-Step -Message "Preparing Docker artifact folders" -Action {
    New-Item -ItemType Directory -Path $laneRoot -Force | Out-Null

    Remove-PathIfExists -LiteralPath $dockerArtifactRoot
    Remove-PathIfExists -LiteralPath $dockerZipPath

    New-Item -ItemType Directory -Path $backendAppRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $frontendAppRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $databaseArtifactRoot -Force | Out-Null
}

if (-not $SkipDotnetRestore) {
    Invoke-Step -Message "Restoring SystemUptimeTracker solution" -Action {
        dotnet restore $solutionPath
    }
}

Invoke-Step -Message "Publishing SystemUptimeTracker.Api runtime" -Action {
    dotnet publish $apiProjectPath `
        -c $Configuration `
        -o $backendAppRoot `
        /p:BuildVersion=$resolvedBuildVersion `
        /p:UseAppHost=false
}

Push-Location $webProjectRoot
try {
    if (-not $SkipNpmCi) {
        Invoke-Step -Message "Installing SystemUptimeTracker.Web dependencies with npm ci" -Action {
            npm ci
        }
    }

    Invoke-Step -Message "Building SystemUptimeTracker.Web standalone runtime" -Action {
        npm run build
    }

    Invoke-Step -Message "Staging SystemUptimeTracker.Web Docker context" -Action {
        $standaloneRoot = Join-Path $webProjectRoot ".next/standalone"
        $staticRoot = Join-Path $webProjectRoot ".next/static"
        $publicRoot = Join-Path $webProjectRoot "public"

        if (-not (Test-Path -LiteralPath $standaloneRoot)) {
            throw "Expected Next.js standalone output was not found at '$standaloneRoot'."
        }

        Copy-Item -Path (Join-Path $standaloneRoot "*") -Destination $frontendAppRoot -Recurse -Force

        if (Test-Path -LiteralPath $staticRoot) {
            $artifactNextRoot = Join-Path $frontendAppRoot ".next"
            New-Item -ItemType Directory -Path $artifactNextRoot -Force | Out-Null
            Copy-Item -LiteralPath $staticRoot -Destination (Join-Path $artifactNextRoot "static") -Recurse -Force
        }

        if (Test-Path -LiteralPath $publicRoot) {
            Copy-Item -LiteralPath $publicRoot -Destination (Join-Path $frontendAppRoot "public") -Recurse -Force
        }
    }
}
finally {
    Pop-Location
}

Invoke-Step -Message "Generating database migration scripts" -Action {
    $dotnetEfCommand = Get-DotnetEfCommand -ToolRoot $toolRoot
    $originalDefaultConnection = $env:ConnectionStrings__DefaultConnection

    if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__DefaultConnection)) {
        $env:ConnectionStrings__DefaultConnection = $designTimeConnectionString
    }

    try {
        & $dotnetEfCommand migrations script `
            --project $dataProjectPath `
            --startup-project $apiProjectPath `
            --context "ApplicationDbContext" `
            --idempotent `
            --output (Join-Path $databaseArtifactRoot "ApplicationDbContext.sql")

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet-ef failed to generate ApplicationDbContext.sql with exit code $LASTEXITCODE."
        }

        & $dotnetEfCommand migrations script `
            --project $dataProjectPath `
            --startup-project $apiProjectPath `
            --context "SystemUptimeTrackerSchemaContext" `
            --idempotent `
            --output (Join-Path $databaseArtifactRoot "SystemUptimeTrackerSchemaContext.sql")

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet-ef failed to generate SystemUptimeTrackerSchemaContext.sql with exit code $LASTEXITCODE."
        }
    }
    finally {
        $env:ConnectionStrings__DefaultConnection = $originalDefaultConnection
    }
}

Invoke-Step -Message "Writing Docker build files and compose metadata" -Action {
    if (-not (Test-Path -LiteralPath $composeFilePath)) {
        throw "Expected compose file was not found at '$composeFilePath'."
    }

    if (-not (Test-Path -LiteralPath $composeEnvTemplatePath)) {
        throw "Expected Docker environment template was not found at '$composeEnvTemplatePath'."
    }

    foreach ($deploymentFilePath in @(
        $composeDebugFilePath,
        $composeEnvironmentHelperPath,
        $packageDeploymentScriptPath,
        $environmentWriterPath,
        $iisProxyScriptPath,
        $databaseBackupScriptPath,
        $sqlApplicationLoginScriptPath,
        $databaseResetScriptPath,
        $databaseMigrationTestScriptPath
    )) {
        if (-not (Test-Path -LiteralPath $deploymentFilePath)) {
            throw "Expected Docker deployment file was not found at '$deploymentFilePath'."
        }
    }

    if (-not (Test-Path -LiteralPath $webEnvTemplatePath)) {
        throw "Expected web environment template was not found at '$webEnvTemplatePath'."
    }

    Set-Content -LiteralPath (Join-Path $backendArtifactRoot "Dockerfile") -Value $backendDockerfile
    Set-Content -LiteralPath (Join-Path $frontendArtifactRoot "Dockerfile") -Value $frontendDockerfile

    Copy-Item -LiteralPath $composeFilePath -Destination (Join-Path $dockerArtifactRoot "docker-compose.production.yml") -Force
    Copy-Item -LiteralPath $composeDebugFilePath -Destination (Join-Path $dockerArtifactRoot "docker-compose.backend-debug.yml") -Force
    Copy-Item -LiteralPath $composeEnvTemplatePath -Destination (Join-Path $dockerArtifactRoot "systemuptimetracker.production.env.example") -Force
    Copy-Item -LiteralPath $composeEnvironmentHelperPath -Destination (Join-Path $dockerArtifactRoot "SystemUptimeTracker.Environment.ps1") -Force
    Copy-Item -LiteralPath $packageDeploymentScriptPath -Destination (Join-Path $dockerArtifactRoot "Deploy-SystemUptimeTrackerDockerPackage.ps1") -Force
    Copy-Item -LiteralPath $environmentWriterPath -Destination (Join-Path $dockerArtifactRoot "New-SystemUptimeTrackerDockerEnvironment.ps1") -Force
    Copy-Item -LiteralPath $iisProxyScriptPath -Destination (Join-Path $dockerArtifactRoot "Configure-SystemUptimeTrackerDockerIisProxy.ps1") -Force
    Copy-Item -LiteralPath $databaseBackupScriptPath -Destination (Join-Path $dockerArtifactRoot "Backup-SystemUptimeTrackerDatabase.ps1") -Force
    Copy-Item -LiteralPath $sqlApplicationLoginScriptPath -Destination (Join-Path $dockerArtifactRoot "Set-SystemUptimeTrackerSqlApplicationLogin.ps1") -Force
    Copy-Item -LiteralPath $databaseResetScriptPath -Destination (Join-Path $dockerArtifactRoot "Reset-SystemUptimeTrackerDockerDatabase.ps1") -Force
    Copy-Item -LiteralPath $databaseMigrationTestScriptPath -Destination (Join-Path $dockerArtifactRoot "Test-SystemUptimeTrackerDockerMigrations.ps1") -Force
    Copy-Item -LiteralPath $webEnvTemplatePath -Destination (Join-Path $frontendArtifactRoot ".env.example") -Force
}

Invoke-Step -Message "Writing Docker artifact manifest" -Action {
    $webBuildIdPath = Join-Path $webProjectRoot ".next/BUILD_ID"
    $webBuildId = if (Test-Path -LiteralPath $webBuildIdPath) {
        (Get-Content -LiteralPath $webBuildIdPath -Raw).Trim()
    }
    else {
        ""
    }

    $manifest = [ordered]@{
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
        configuration = $Configuration
        buildVersion = $resolvedBuildVersion
        gitCommitSha = $gitSha
        composeFile = [IO.Path]::GetRelativePath($repoRoot, (Join-Path $dockerArtifactRoot "docker-compose.production.yml"))
        composeEnvironmentTemplate = [IO.Path]::GetRelativePath($repoRoot, (Join-Path $dockerArtifactRoot "systemuptimetracker.production.env.example"))
        deploymentScript = [IO.Path]::GetRelativePath($repoRoot, (Join-Path $dockerArtifactRoot "Deploy-SystemUptimeTrackerDockerPackage.ps1"))
        images = [ordered]@{
            backend = [ordered]@{
                imageName = "systemuptimetracker-backend"
                buildContext = [IO.Path]::GetRelativePath($repoRoot, $backendArtifactRoot)
                dockerfile = [IO.Path]::GetRelativePath($repoRoot, (Join-Path $backendArtifactRoot "Dockerfile"))
                publishedRuntimePath = [IO.Path]::GetRelativePath($repoRoot, $backendAppRoot)
                suggestedBuildCommand = "docker build -t systemuptimetracker-backend:<tag> -f backend/Dockerfile backend"
            }
            frontend = [ordered]@{
                imageName = "systemuptimetracker-frontend"
                buildContext = [IO.Path]::GetRelativePath($repoRoot, $frontendArtifactRoot)
                dockerfile = [IO.Path]::GetRelativePath($repoRoot, (Join-Path $frontendArtifactRoot "Dockerfile"))
                publishedRuntimePath = [IO.Path]::GetRelativePath($repoRoot, $frontendAppRoot)
                buildId = $webBuildId
                suggestedBuildCommand = "docker build -t systemuptimetracker-frontend:<tag> -f frontend/Dockerfile frontend"
            }
        }
        database = [ordered]@{
            artifactPath = [IO.Path]::GetRelativePath($repoRoot, $databaseArtifactRoot)
            scripts = @(
                [IO.Path]::GetRelativePath($repoRoot, (Join-Path $databaseArtifactRoot "ApplicationDbContext.sql")),
                [IO.Path]::GetRelativePath($repoRoot, (Join-Path $databaseArtifactRoot "SystemUptimeTrackerSchemaContext.sql"))
            )
        }
        notes = @(
            "This package intentionally does not contain built Docker images.",
            "Run Deploy-SystemUptimeTrackerDockerPackage.ps1 from the extracted package to build the staged images, apply pending migrations, and deploy the stack."
        )
    }

    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath
}

Invoke-Step -Message "Packaging Docker artifact zip" -Action {
    Compress-Archive -Path (Join-Path $dockerArtifactRoot "*") -DestinationPath $dockerZipPath -CompressionLevel Optimal
    Remove-PathIfExists -LiteralPath $toolRoot
}

Write-Host ""
Write-Host "Docker artifact shape created successfully." -ForegroundColor Green
