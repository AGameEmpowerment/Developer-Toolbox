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
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptRoot)
$solutionPath = Join-Path $repoRoot "SystemUptimeTracker.sln"
$apiProjectPath = Join-Path $repoRoot "src/SystemUptimeTracker/SystemUptimeTracker.Api/SystemUptimeTracker.Api.csproj"
$apiProjectRoot = Split-Path -Parent $apiProjectPath
$dataProjectPath = Join-Path $repoRoot "src/SystemUptimeTracker/SystemUptimeTracker.Data/SystemUptimeTracker.Data.csproj"
$webProjectRoot = Join-Path $repoRoot "src/SystemUptimeTracker/SystemUptimeTracker.Web"
$appHostRoot = Join-Path $repoRoot "src/SystemUptimeTracker/SystemUptimeTracker.AppHost"
$webEnvTemplatePath = Join-Path $webProjectRoot ".env.example"
$deployScriptPath = Join-Path $repoRoot "devops/iis/Deploy-SystemUptimeTrackerIisPackage.ps1"
$webEnvironmentScriptPath = Join-Path $repoRoot "devops/iis/Initialize-SystemUptimeTrackerWebEnvironment.ps1"
$runtimeScriptPath = Join-Path $repoRoot "devops/iis/Set-SystemUptimeTrackerIisRuntime.ps1"
$smokeScriptPath = Join-Path $repoRoot "devops/iis/Invoke-SystemUptimeTrackerSmokeChecks.ps1"
$runbookPath = Join-Path $repoRoot "docs/devops/02-iis-release-runbook.md"

$laneRoot = Join-Path $repoRoot $OutputRoot
$laneRoot = Join-Path $laneRoot $Configuration
$apiArtifactRoot = Join-Path $laneRoot "server-package"
$webArtifactRoot = Join-Path $laneRoot "web-package"
$databaseArtifactRoot = Join-Path $laneRoot "database"
$deploymentArtifactRoot = Join-Path $laneRoot "deployment-assets"
$iisArtifactRoot = Join-Path $laneRoot "iis-package"
$serverZipPath = Join-Path $laneRoot "server-package.zip"
$webZipPath = Join-Path $laneRoot "web-package.zip"
$iisZipPath = Join-Path $laneRoot "iis-package.zip"
$manifestPath = Join-Path $laneRoot "release-manifest.json"
$toolRoot = Join-Path $laneRoot ".tools"
$designTimeConnectionString = "Server=127.0.0.1,1433;Database=SystemUptimeTracker_DesignTime;Integrated Security=true;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=true"

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

Assert-CommandExists -CommandName "dotnet"
Assert-CommandExists -CommandName "npm"

Invoke-Step -Message "Preparing output folders" -Action {
    New-Item -ItemType Directory -Path $laneRoot -Force | Out-Null
    Remove-PathIfExists -LiteralPath $apiArtifactRoot
    Remove-PathIfExists -LiteralPath $webArtifactRoot
    Remove-PathIfExists -LiteralPath $databaseArtifactRoot
    Remove-PathIfExists -LiteralPath $deploymentArtifactRoot
    Remove-PathIfExists -LiteralPath $iisArtifactRoot
    Remove-PathIfExists -LiteralPath $serverZipPath
    Remove-PathIfExists -LiteralPath $webZipPath
    Remove-PathIfExists -LiteralPath $iisZipPath

    New-Item -ItemType Directory -Path $apiArtifactRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $webArtifactRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $databaseArtifactRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $deploymentArtifactRoot -Force | Out-Null
}

if (-not $SkipDotnetRestore) {
    Invoke-Step -Message "Restoring SystemUptimeTracker solution" -Action {
        dotnet restore $solutionPath
    }
}

Invoke-Step -Message "Publishing SystemUptimeTracker.Api for $Configuration" -Action {
    dotnet publish $apiProjectPath `
        -c $Configuration `
        -o $apiArtifactRoot `
        /p:BuildVersion=$resolvedBuildVersion
}

Push-Location $webProjectRoot
try {
    if (-not $SkipNpmCi) {
        Invoke-Step -Message "Installing SystemUptimeTracker.Web dependencies with npm ci" -Action {
            npm ci
        }
    }

    Invoke-Step -Message "Building SystemUptimeTracker.Web standalone output" -Action {
        npm run build
    }

    Invoke-Step -Message "Staging SystemUptimeTracker.Web artifact" -Action {
        $standaloneRoot = Join-Path $webProjectRoot ".next/standalone"
        $staticRoot = Join-Path $webProjectRoot ".next/static"
        $publicRoot = Join-Path $webProjectRoot "public"
        $artifactEnvPath = Join-Path $webArtifactRoot ".env.example"

        if (-not (Test-Path -LiteralPath $standaloneRoot)) {
            throw "Expected Next.js standalone output was not found at '$standaloneRoot'."
        }

        Copy-Item -Path (Join-Path $standaloneRoot "*") -Destination $webArtifactRoot -Recurse -Force

        if (Test-Path -LiteralPath $staticRoot) {
            $webNextRoot = Join-Path $webArtifactRoot ".next"
            New-Item -ItemType Directory -Path $webNextRoot -Force | Out-Null
            Copy-Item -LiteralPath $staticRoot -Destination (Join-Path $webNextRoot "static") -Recurse -Force
        }

        if (Test-Path -LiteralPath $publicRoot) {
            Copy-Item -LiteralPath $publicRoot -Destination (Join-Path $webArtifactRoot "public") -Recurse -Force
        }

        if (-not (Test-Path -LiteralPath $webEnvTemplatePath)) {
            throw "Expected web environment template was not found at '$webEnvTemplatePath'."
        }

        Copy-Item -LiteralPath $webEnvTemplatePath -Destination $artifactEnvPath -Force

        if (-not (Test-Path -LiteralPath $webEnvironmentScriptPath)) {
            throw "Expected web environment deployment script was not found at '$webEnvironmentScriptPath'."
        }

        $webArtifactDevopsRoot = Join-Path $webArtifactRoot "devops"
        New-Item -ItemType Directory -Path $webArtifactDevopsRoot -Force | Out-Null
        Copy-Item -LiteralPath $webEnvironmentScriptPath -Destination (Join-Path $webArtifactDevopsRoot "Initialize-SystemUptimeTrackerWebEnvironment.ps1") -Force
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

Invoke-Step -Message "Staging IIS deployment assets" -Action {
    Copy-Item -LiteralPath $deployScriptPath -Destination (Join-Path $deploymentArtifactRoot "Deploy-SystemUptimeTrackerIisPackage.ps1") -Force
    Copy-Item -LiteralPath $webEnvironmentScriptPath -Destination (Join-Path $deploymentArtifactRoot "Initialize-SystemUptimeTrackerWebEnvironment.ps1") -Force
    Copy-Item -LiteralPath $runtimeScriptPath -Destination (Join-Path $deploymentArtifactRoot "Set-SystemUptimeTrackerIisRuntime.ps1") -Force
    Copy-Item -LiteralPath $smokeScriptPath -Destination (Join-Path $deploymentArtifactRoot "Invoke-SystemUptimeTrackerSmokeChecks.ps1") -Force

    if (Test-Path -LiteralPath $runbookPath) {
        Copy-Item -LiteralPath $runbookPath -Destination (Join-Path $deploymentArtifactRoot "IIS-Release-Runbook.md") -Force
    }
}

Invoke-Step -Message "Writing publish manifest" -Action {
    $apiConfigFiles = Get-ChildItem -LiteralPath $apiProjectRoot -Filter "appsettings*.json" | Sort-Object Name | Select-Object -ExpandProperty Name
    $appHostConfigFiles = Get-ChildItem -LiteralPath $appHostRoot -Filter "appsettings*.json" | Sort-Object Name | Select-Object -ExpandProperty Name
    $publishedApiConfigFiles = Get-ChildItem -LiteralPath $apiArtifactRoot -Filter "appsettings*.json" | Sort-Object Name | Select-Object -ExpandProperty Name
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
        solution = "SystemUptimeTracker.sln"
        appHost = [ordered]@{
            project = "src/SystemUptimeTracker/SystemUptimeTracker.AppHost/SystemUptimeTracker.AppHost.csproj"
            role = "Local-only Aspire orchestration"
            configFiles = $appHostConfigFiles
        }
        api = [ordered]@{
            project = "src/SystemUptimeTracker/SystemUptimeTracker.Api/SystemUptimeTracker.Api.csproj"
            publishPath = [IO.Path]::GetRelativePath($repoRoot, $apiArtifactRoot)
            packageZip = [IO.Path]::GetRelativePath($repoRoot, $serverZipPath)
            configFiles = $apiConfigFiles
            publishedConfigFiles = $publishedApiConfigFiles
            startCommand = "dotnet SystemUptimeTracker.Api.dll"
        }
        web = [ordered]@{
            project = "src/SystemUptimeTracker/SystemUptimeTracker.Web"
            artifactPath = [IO.Path]::GetRelativePath($repoRoot, $webArtifactRoot)
            packageZip = [IO.Path]::GetRelativePath($repoRoot, $webZipPath)
            buildId = $webBuildId
            startCommand = "node server.js"
            includes = @(
                ".env.example",
                "server.js",
                ".next/static",
                "public",
                "node_modules"
            )
            outputMode = "Next.js standalone"
        }
        database = [ordered]@{
            artifactPath = [IO.Path]::GetRelativePath($repoRoot, $databaseArtifactRoot)
            scripts = @(
                [IO.Path]::GetRelativePath($repoRoot, (Join-Path $databaseArtifactRoot "ApplicationDbContext.sql")),
                [IO.Path]::GetRelativePath($repoRoot, (Join-Path $databaseArtifactRoot "SystemUptimeTrackerSchemaContext.sql"))
            )
            mode = "Idempotent EF Core SQL scripts"
        }
        deployment = [ordered]@{
            artifactPath = [IO.Path]::GetRelativePath($repoRoot, $deploymentArtifactRoot)
            deployScript = [IO.Path]::GetRelativePath($repoRoot, (Join-Path $deploymentArtifactRoot "Deploy-SystemUptimeTrackerIisPackage.ps1"))
            smokeScript = [IO.Path]::GetRelativePath($repoRoot, (Join-Path $deploymentArtifactRoot "Invoke-SystemUptimeTrackerSmokeChecks.ps1"))
            runbook = if (Test-Path -LiteralPath (Join-Path $deploymentArtifactRoot "IIS-Release-Runbook.md")) {
                [IO.Path]::GetRelativePath($repoRoot, (Join-Path $deploymentArtifactRoot "IIS-Release-Runbook.md"))
            }
            else {
                ""
            }
        }
    }

    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath
}

Invoke-Step -Message "Packaging release zips" -Action {
    Compress-Archive -Path (Join-Path $apiArtifactRoot "*") -DestinationPath $serverZipPath -CompressionLevel Optimal
    Compress-Archive -Path (Join-Path $webArtifactRoot "*") -DestinationPath $webZipPath -CompressionLevel Optimal
    Remove-PathIfExists -LiteralPath $toolRoot
}

Write-Host ""
Write-Host "Publish shape created successfully." -ForegroundColor Green
Write-Host "Lane: $Configuration"
Write-Host "API artifact: $apiArtifactRoot"
Write-Host "Web artifact: $webArtifactRoot"
Write-Host "Database artifact: $databaseArtifactRoot"
Write-Host "Manifest: $manifestPath"
