[CmdletBinding()]
param(
    [ValidateSet("SystemUptimeTracker", "Shared")]
    [string]$Target = "SystemUptimeTracker",

    [string]$SystemUptimeTrackerEnvironmentFile = "devops/docker/systemuptimetracker.production.env",

    [string]$SharedEnvironmentFile = "containers/.env"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Read-DotEnvValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Environment file '$Path' was not found."
    }

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match "^\s*$([Regex]::Escape($Name))\s*=(?<value>.*)$") {
            $value = $Matches.value.Trim()
            if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
                ($value.StartsWith("'") -and $value.EndsWith("'"))) {
                $value = $value.Substring(1, $value.Length - 2)
            }

            if (-not [string]::IsNullOrWhiteSpace($value)) {
                return $value
            }
        }
    }

    throw "Required value '$Name' is missing from '$Path'."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$appHostProject = Join-Path $repoRoot "src/SystemUptimeTracker/SystemUptimeTracker.AppHost/SystemUptimeTracker.AppHost.csproj"
$apiProject = Join-Path $repoRoot "src/SystemUptimeTracker/SystemUptimeTracker.Api/SystemUptimeTracker.Api.csproj"

if ($Target -eq "SystemUptimeTracker") {
    $environmentPath = if ([IO.Path]::IsPathRooted($SystemUptimeTrackerEnvironmentFile)) {
        $SystemUptimeTrackerEnvironmentFile
    }
    else {
        Join-Path $repoRoot $SystemUptimeTrackerEnvironmentFile
    }
    $password = Read-DotEnvValue -Path $environmentPath -Name "SYSTEMUPTIMETRACKER_SQL_SA_PASSWORD"
    $server = "127.0.0.1,11433"
}
else {
    $environmentPath = if ([IO.Path]::IsPathRooted($SharedEnvironmentFile)) {
        $SharedEnvironmentFile
    }
    else {
        Join-Path $repoRoot $SharedEnvironmentFile
    }
    $password = Read-DotEnvValue -Path $environmentPath -Name "MSSQL_SA_PASSWORD"
    $server = "127.0.0.1,10433"
}

$escapedPassword = $password.Replace('"', '""')
$connectionString = "Server=$server;Database=SystemUptimeTracker;User Id=sa;Password=`"$escapedPassword`";Encrypt=True;MultipleActiveResultSets=true;TrustServerCertificate=True"

& dotnet user-secrets set "ConnectionStrings:DefaultConnection" $connectionString --project $appHostProject | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Failed to update the AppHost local connection string."
}

& dotnet user-secrets set "AppHost:Server:EnvironmentVariables:ConnectionStrings__DefaultConnection" $connectionString --project $appHostProject | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Failed to update the AppHost API resource connection string."
}

& dotnet user-secrets set "ConnectionStrings:DefaultConnection" $connectionString --project $apiProject | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Failed to update the API local connection string."
}

Write-Host "Local SystemUptimeTracker database target is now '$Target' at $server."
