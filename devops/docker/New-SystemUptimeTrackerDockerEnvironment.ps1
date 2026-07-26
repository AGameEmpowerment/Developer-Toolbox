[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TemplatePath,

    [Parameter(Mandatory = $true)]
    [string]$DestinationPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$lines = [System.Collections.Generic.List[string]]::new()
foreach ($line in Get-Content -LiteralPath $TemplatePath) {
    if ($line -match "^(?<name>[A-Z][A-Z0-9_]+)=") {
        $value = [Environment]::GetEnvironmentVariable($Matches.name, "Process")
        if ($null -ne $value) {
            $lines.Add("$($Matches.name)=$value")
            continue
        }
    }

    $lines.Add($line)
}

$required = @(
    "SYSTEMUPTIMETRACKER_SQL_SA_PASSWORD",
    "SYSTEMUPTIMETRACKER_SQL_APP_USERNAME",
    "SYSTEMUPTIMETRACKER_SQL_APP_PASSWORD",
    "UI_AUTH_COOKIE_SECRET",
    "UI_IMPERSONATE_ENCRYPTION_KEY",
    "API_REDACTION_KEY"
)

foreach ($name in $required) {
    $value = [Environment]::GetEnvironmentVariable($name, "Process")
    if ([string]::IsNullOrWhiteSpace($value) -or $value -match "^\$\(.+\)$" -or $value -match "^replace_") {
        throw "Required Docker deployment environment variable '$name' was not provided."
    }
}

$parent = Split-Path -Parent ([System.IO.Path]::GetFullPath($DestinationPath))
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$lines | Set-Content -LiteralPath $DestinationPath -Encoding utf8
