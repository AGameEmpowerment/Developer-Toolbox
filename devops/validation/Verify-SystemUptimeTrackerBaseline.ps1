<#
.SYNOPSIS
Validates the current SystemUptimeTracker solution seams for Story 001.

.DESCRIPTION
Checks that the authoritative SystemUptimeTracker solution file, project files,
project folders, and key AppHost wiring are present. The script emits a
structured summary object and throws when the baseline does not match the
expected scaffold.

.EXAMPLE
pwsh ./devops/validation/Verify-SystemUptimeTrackerBaseline.ps1

Runs the Story 001 structural validation from the repository root.

.OUTPUTS
System.Management.Automation.PSCustomObject

.NOTES
This script is intentionally narrow. It validates the current baseline seams
without trying to remove the remaining sample/demo behavior that later
stories own.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptRoot)
$solutionPath = Join-Path $repoRoot 'SystemUptimeTracker.sln'
$appHostSettingsPath = Join-Path $repoRoot 'src\SystemUptimeTracker\SystemUptimeTracker.AppHost\appsettings.json'
$apiLaunchSettingsPath = Join-Path $repoRoot 'src\SystemUptimeTracker\SystemUptimeTracker.Api\Properties\launchSettings.json'

$expectedProjects = @(
    @{
        Name = 'SystemUptimeTracker.AppHost'
        SolutionPath = 'src\SystemUptimeTracker\SystemUptimeTracker.AppHost\SystemUptimeTracker.AppHost.csproj'
        FolderPath = 'src\SystemUptimeTracker\SystemUptimeTracker.AppHost'
    },
    @{
        Name = 'SystemUptimeTracker.ServiceDefaults'
        SolutionPath = 'src\SystemUptimeTracker\SystemUptimeTracker.ServiceDefaults\SystemUptimeTracker.ServiceDefaults.csproj'
        FolderPath = 'src\SystemUptimeTracker\SystemUptimeTracker.ServiceDefaults'
    },
    @{
        Name = 'SystemUptimeTracker.Common'
        SolutionPath = 'src\SystemUptimeTracker\SystemUptimeTracker.Common\SystemUptimeTracker.Common.csproj'
        FolderPath = 'src\SystemUptimeTracker\SystemUptimeTracker.Common'
    },
    @{
        Name = 'SystemUptimeTracker.Api'
        SolutionPath = 'src\SystemUptimeTracker\SystemUptimeTracker.Api\SystemUptimeTracker.Api.csproj'
        FolderPath = 'src\SystemUptimeTracker\SystemUptimeTracker.Api'
    },
    @{
        Name = 'SystemUptimeTracker.Web'
        SolutionPath = 'src\SystemUptimeTracker\SystemUptimeTracker.Web\SystemUptimeTracker.Web.esproj'
        FolderPath = 'src\SystemUptimeTracker\SystemUptimeTracker.Web'
    },
    @{
        Name = 'SystemUptimeTracker.Tests'
        SolutionPath = 'src\SystemUptimeTracker\SystemUptimeTracker.Tests\SystemUptimeTracker.Tests.csproj'
        FolderPath = 'src\SystemUptimeTracker\SystemUptimeTracker.Tests'
    },
    @{
        Name = 'SystemUptimeTracker.Qa.Automation'
        SolutionPath = 'src\SystemUptimeTracker\SystemUptimeTracker.Qa.Automation\SystemUptimeTracker.Qa.Automation.csproj'
        FolderPath = 'src\SystemUptimeTracker\SystemUptimeTracker.Qa.Automation'
    }
)

$failures = [System.Collections.Generic.List[string]]::new()
$solutionContent = if (Test-Path -LiteralPath $solutionPath) {
    Get-Content -LiteralPath $solutionPath -Raw
}
else {
    $failures.Add("Missing solution file: $solutionPath")
    ''
}

$projectResults = foreach ($project in $expectedProjects) {
    $projectFilePath = Join-Path $repoRoot $project.SolutionPath
    $projectFolderPath = Join-Path $repoRoot $project.FolderPath

    $projectFileExists = Test-Path -LiteralPath $projectFilePath
    $projectFolderExists = Test-Path -LiteralPath $projectFolderPath
    $solutionEntryExists = $solutionContent.Contains($project.SolutionPath)

    if (-not $projectFileExists) {
        $failures.Add("Missing project file for $($project.Name): $projectFilePath")
    }

    if (-not $projectFolderExists) {
        $failures.Add("Missing project folder for $($project.Name): $projectFolderPath")
    }

    if (-not $solutionEntryExists) {
        $failures.Add("Solution file is missing the expected entry for $($project.Name): $($project.SolutionPath)")
    }

    [PSCustomObject]@{
        Name = $project.Name
        ProjectFileExists = $projectFileExists
        ProjectFolderExists = $projectFolderExists
        SolutionEntryExists = $solutionEntryExists
    }
}

$appHostSettings = Get-Content -LiteralPath $appHostSettingsPath -Raw | ConvertFrom-Json -AsHashtable
$apiLaunchSettings = Get-Content -LiteralPath $apiLaunchSettingsPath -Raw | ConvertFrom-Json -AsHashtable

$expectedServerProjectPath = '../SystemUptimeTracker.Api/SystemUptimeTracker.Api.csproj'
$expectedClientAppDirectory = '../SystemUptimeTracker.Web'
$expectedServerLaunchProfile = 'aspire-https'
$expectedServerHealthPath = '/_health'

$serverProjectPathMatches = $appHostSettings.AppHost.Server.ProjectPath -eq $expectedServerProjectPath
$clientAppDirectoryMatches = $appHostSettings.AppHost.Client.AppDirectory -eq $expectedClientAppDirectory
$serverLaunchProfileMatches = $appHostSettings.AppHost.Server.LaunchProfileName -eq $expectedServerLaunchProfile
$serverHealthPathMatches = $appHostSettings.AppHost.Server.HealthCheckPath -eq $expectedServerHealthPath
$apiLaunchProfileExists = $apiLaunchSettings.profiles.ContainsKey($expectedServerLaunchProfile)

if (-not $serverProjectPathMatches) {
    $failures.Add("AppHost server project path is '$($appHostSettings.AppHost.Server.ProjectPath)' instead of '$expectedServerProjectPath'.")
}

if (-not $clientAppDirectoryMatches) {
    $failures.Add("AppHost client app directory is '$($appHostSettings.AppHost.Client.AppDirectory)' instead of '$expectedClientAppDirectory'.")
}

if (-not $serverLaunchProfileMatches) {
    $failures.Add("AppHost server launch profile is '$($appHostSettings.AppHost.Server.LaunchProfileName)' instead of '$expectedServerLaunchProfile'.")
}

if (-not $serverHealthPathMatches) {
    $failures.Add("AppHost server health path is '$($appHostSettings.AppHost.Server.HealthCheckPath)' instead of '$expectedServerHealthPath'.")
}

if (-not $apiLaunchProfileExists) {
    $failures.Add("SystemUptimeTracker.Api launch settings do not contain the '$expectedServerLaunchProfile' profile required by AppHost.")
}

$result = [PSCustomObject]@{
    ValidatedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
    RepositoryRoot = $repoRoot
    SolutionExists = Test-Path -LiteralPath $solutionPath
    Projects = $projectResults
    AppHostWiring = [PSCustomObject]@{
        ServerProjectPathMatches = $serverProjectPathMatches
        ClientAppDirectoryMatches = $clientAppDirectoryMatches
        ServerLaunchProfileMatches = $serverLaunchProfileMatches
        ServerHealthPathMatches = $serverHealthPathMatches
        ApiLaunchProfileExists = $apiLaunchProfileExists
    }
    FailureCount = $failures.Count
    Failures = $failures.ToArray()
}

Write-Output $result

if ($failures.Count -gt 0) {
    throw "SystemUptimeTracker baseline validation failed with $($failures.Count) issue(s)."
}
