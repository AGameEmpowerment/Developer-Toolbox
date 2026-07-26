param(
    [string[]]$AdditionalArgs = @()
)

$projectPath = Join-Path $PSScriptRoot "SystemUptimeTracker.Qa.Automation.csproj"
$settingsPath = Join-Path $PSScriptRoot "SystemUptimeTracker.Qa.Automation.full.runsettings"

$commandArgs = @("test", $projectPath, "--settings", $settingsPath)
if ($AdditionalArgs.Count -gt 0) {
    $commandArgs += $AdditionalArgs
}

dotnet @commandArgs
exit $LASTEXITCODE
