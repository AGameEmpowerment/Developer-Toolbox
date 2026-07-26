function Get-EnvironmentFileState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $entries = [ordered]@{}
    $lineIndexes = @{}

    if (Test-Path -LiteralPath $Path) {
        foreach ($line in Get-Content -LiteralPath $Path) {
            [void]$lines.Add($line)

            if ($line -match '^\s*([A-Za-z0-9_]+)\s*=\s*(.*)\s*$') {
                $name = $matches[1].Trim()
                $value = $matches[2]

                if ($value.Length -ge 2) {
                    $isDoubleQuoted = $value.StartsWith('"') -and $value.EndsWith('"')
                    $isSingleQuoted = $value.StartsWith("'") -and $value.EndsWith("'")
                    if ($isDoubleQuoted -or $isSingleQuoted) {
                        $value = $value.Substring(1, $value.Length - 2)
                    }
                }

                $entries[$name] = $value
                $lineIndexes[$name] = $lines.Count - 1
            }
        }
    }

    [pscustomobject]@{
        Path = $Path
        Lines = $lines
        Entries = $entries
        LineIndexes = $lineIndexes
    }
}

function Set-EnvironmentEntryValue {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$State,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [AllowEmptyString()]
        [string]$Value
    )

    $line = "$Name=$Value"
    if ($State.LineIndexes.ContainsKey($Name)) {
        $State.Lines[$State.LineIndexes[$Name]] = $line
    }
    else {
        [void]$State.Lines.Add($line)
        $State.LineIndexes[$Name] = $State.Lines.Count - 1
    }

    $State.Entries[$Name] = $Value
}

function Save-EnvironmentFileState {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$State
    )

    Set-Content -LiteralPath $State.Path -Value $State.Lines
}

function Test-EnvironmentValueMissing {
    param(
        [AllowEmptyString()]
        [string]$Value
    )

    return [string]::IsNullOrWhiteSpace($Value)
}

function Test-EnvironmentValuePlaceholder {
    param(
        [AllowEmptyString()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $true
    }

    $trimmedValue = $Value.Trim()
    return $trimmedValue.StartsWith('replace_with_', [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-EnvironmentValueMissingOrPlaceholder {
    param(
        [AllowEmptyString()]
        [string]$Value
    )

    return (Test-EnvironmentValueMissing -Value $Value) -or (Test-EnvironmentValuePlaceholder -Value $Value)
}

function Assert-SystemUptimeTrackerSqlEnvironmentCanReuseVolume {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentFilePath,

        [string]$VolumeName = "systemuptimetracker-sql-data"
    )

    & docker volume inspect $VolumeName *> $null
    if ($LASTEXITCODE -ne 0) {
        return
    }

    $state = Get-EnvironmentFileState -Path $EnvironmentFilePath
    if (Test-EnvironmentValueMissingOrPlaceholder -Value $state.Entries["SYSTEMUPTIMETRACKER_SQL_SA_PASSWORD"]) {
        throw "Docker volume '$VolumeName' already contains a persistent SQL instance, but '$EnvironmentFilePath' does not contain its SA password. Restore the ignored environment file or explicitly reset the local database before recreating it."
    }
}

function Test-EnvironmentFlagEnabled {
    param(
        [AllowEmptyString()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    switch -Regex ($Value.Trim()) {
        '^(1|true|yes|on)$' { return $true }
        default { return $false }
    }
}

function Test-StorageProviderValue {
    param(
        [AllowEmptyString()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $true
    }

    $validValues = @('FILE_SYSTEM', 'AZURE_BLOB')
    return $validValues.Contains($Value.Trim())
}

function Test-RedactionKeyValue {
    param(
        [AllowEmptyString()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    $guidPattern = '[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}'
    return $Value.Trim() -match "^\{$guidPattern\}\{$guidPattern\}$"
}

function New-HexSecret {
    param(
        [ValidateRange(1, 1024)]
        [int]$ByteCount = 32
    )

    $randomBytes = [byte[]]::new($ByteCount)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($randomBytes)
    return [Convert]::ToHexString($randomBytes).ToLowerInvariant()
}

function New-RandomToken {
    param(
        [ValidateRange(1, 1024)]
        [int]$ByteCount = 24
    )

    return New-HexSecret -ByteCount $ByteCount
}

function Add-MissingEnvironmentRequirement {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$MissingRequirements,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Reason
    )

    $MissingRequirements.Add("$Name ($Reason)")
}

function Initialize-SystemUptimeTrackerProductionEnvironmentFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentFilePath,

        [Parameter(Mandatory = $true)]
        [string]$TemplateFilePath
    )

    $wasCreated = $false
    if (-not (Test-Path -LiteralPath $EnvironmentFilePath)) {
        if (-not (Test-Path -LiteralPath $TemplateFilePath)) {
            throw "Environment file was not found at '$EnvironmentFilePath', and template '$TemplateFilePath' does not exist."
        }

        Copy-Item -LiteralPath $TemplateFilePath -Destination $EnvironmentFilePath
        $wasCreated = $true
        Write-Host "Created '$EnvironmentFilePath' from '$TemplateFilePath'." -ForegroundColor Yellow
    }

    $state = Get-EnvironmentFileState -Path $EnvironmentFilePath
    $generatedValues = [System.Collections.Generic.List[string]]::new()
    $missingRequirements = [System.Collections.Generic.List[string]]::new()
    $fileWasUpdated = $false

    $autoGeneratedEntries = @(
        @{
            Name = 'UI_AUTH_COOKIE_SECRET'
            ShouldGenerate = {
                param($currentValue, $currentState, $currentWasCreated)
                return $currentWasCreated -or (Test-EnvironmentValueMissingOrPlaceholder -Value $currentValue)
            }
            GenerateValue = { New-HexSecret -ByteCount 32 }
        },
        @{
            Name = 'UI_IMPERSONATE_ENCRYPTION_KEY'
            ShouldGenerate = {
                param($currentValue, $currentState, $currentWasCreated)
                return $currentWasCreated -or (Test-EnvironmentValueMissingOrPlaceholder -Value $currentValue)
            }
            GenerateValue = { New-HexSecret -ByteCount 32 }
        },
        @{
            Name = 'API_REDACTION_KEY'
            ShouldGenerate = {
                param($currentValue, $currentState, $currentWasCreated)
                return $currentWasCreated -or (Test-EnvironmentValueMissingOrPlaceholder -Value $currentValue) -or (-not (Test-RedactionKeyValue -Value $currentValue))
            }
            GenerateValue = { "{0}{1}" -f ([Guid]::NewGuid().ToString('B').ToUpperInvariant()), ([Guid]::NewGuid().ToString('B').ToUpperInvariant()) }
        },
        @{
            Name = 'SYSTEMUPTIMETRACKER_SQL_SA_PASSWORD'
            ShouldGenerate = {
                param($currentValue, $currentState, $currentWasCreated)
                return $currentWasCreated -or (Test-EnvironmentValueMissingOrPlaceholder -Value $currentValue)
            }
            GenerateValue = { "Sql!{0}" -f (New-HexSecret -ByteCount 24) }
        }
    )

    foreach ($entry in $autoGeneratedEntries) {
        $currentValue = $state.Entries[$entry.Name]
        $shouldGenerate = & $entry.ShouldGenerate $currentValue $state $wasCreated
        if ($shouldGenerate) {
            $generatedValue = & $entry.GenerateValue
            Set-EnvironmentEntryValue -State $state -Name $entry.Name -Value $generatedValue
            $generatedValues.Add($entry.Name)
            $fileWasUpdated = $true
        }
    }

    if (Test-EnvironmentValueMissing -Value $state.Entries['API_APPLY_STARTUP_MIGRATIONS']) {
        Add-MissingEnvironmentRequirement -MissingRequirements $missingRequirements -Name 'API_APPLY_STARTUP_MIGRATIONS' -Reason 'required by devops/docker/docker-compose.yml'
    }

    if (Test-EnvironmentValueMissingOrPlaceholder -Value $state.Entries['SYSTEMUPTIMETRACKER_SQL_SA_PASSWORD']) {
        Add-MissingEnvironmentRequirement -MissingRequirements $missingRequirements -Name 'SYSTEMUPTIMETRACKER_SQL_SA_PASSWORD' -Reason 'required by the standalone SystemUptimeTracker SQL service'
    }

    if (-not (Test-StorageProviderValue -Value $state.Entries['API_STORAGE_PROVIDER'])) {
        Add-MissingEnvironmentRequirement -MissingRequirements $missingRequirements -Name 'API_STORAGE_PROVIDER' -Reason 'must be FILE_SYSTEM or AZURE_BLOB'
    }

    if (Test-EnvironmentValueMissing -Value $state.Entries['UI_AUTH_COOKIE_SECRET']) {
        Add-MissingEnvironmentRequirement -MissingRequirements $missingRequirements -Name 'UI_AUTH_COOKIE_SECRET' -Reason 'required for frontend auth cookies'
    }

    if (Test-EnvironmentValueMissing -Value $state.Entries['UI_IMPERSONATE_ENCRYPTION_KEY']) {
        Add-MissingEnvironmentRequirement -MissingRequirements $missingRequirements -Name 'UI_IMPERSONATE_ENCRYPTION_KEY' -Reason 'required for impersonation cookie encryption'
    }

    if (-not (Test-RedactionKeyValue -Value $state.Entries['API_REDACTION_KEY'])) {
        Add-MissingEnvironmentRequirement -MissingRequirements $missingRequirements -Name 'API_REDACTION_KEY' -Reason 'must be formatted as two GUID values wrapped in braces'
    }

    $apiOpenTelemetryEnabled = (Test-EnvironmentFlagEnabled -Value $state.Entries['API_FEATURE_OPENTELEMETRY_ENABLED']) -or (Test-EnvironmentFlagEnabled -Value $state.Entries['API_FEATURE_OPENTELEMETRY_SEQ_ENABLED'])
    if ($apiOpenTelemetryEnabled -and (Test-EnvironmentValueMissingOrPlaceholder -Value $state.Entries['API_OPENTELEMETRY_APIKEY'])) {
        Add-MissingEnvironmentRequirement -MissingRequirements $missingRequirements -Name 'API_OPENTELEMETRY_APIKEY' -Reason 'required when API OpenTelemetry export is enabled'
    }

    $uiOpenTelemetryEnabled = (Test-EnvironmentFlagEnabled -Value $state.Entries['UI_APP_OPENTELEMETRY_ENABLED']) -or (Test-EnvironmentFlagEnabled -Value $state.Entries['UI_APP_OPENTELEMETRY_SEQ_ENABLED'])
    if ($uiOpenTelemetryEnabled -and (Test-EnvironmentValueMissingOrPlaceholder -Value $state.Entries['UI_APP_OPENTELEMETRY_SEQ_API_KEY'])) {
        Add-MissingEnvironmentRequirement -MissingRequirements $missingRequirements -Name 'UI_APP_OPENTELEMETRY_SEQ_API_KEY' -Reason 'required when UI Seq OpenTelemetry export is enabled'
    }

    if ($fileWasUpdated) {
        Save-EnvironmentFileState -State $state
        Write-Host "Updated '$EnvironmentFilePath' with generated environment values for: $($generatedValues -join ', ')." -ForegroundColor Yellow
    }

    if ($missingRequirements.Count -gt 0) {
        $messageLines = @(
            "The environment file '$EnvironmentFilePath' is missing required values:",
            ($missingRequirements | ForEach-Object { " - $_" }),
            '',
            'Update the file and rerun the command.'
        )

        throw ($messageLines -join [Environment]::NewLine)
    }

    return [pscustomobject]@{
        Path = $EnvironmentFilePath
        WasCreated = $wasCreated
        WasUpdated = $fileWasUpdated
        GeneratedValues = $generatedValues.ToArray()
    }
}
