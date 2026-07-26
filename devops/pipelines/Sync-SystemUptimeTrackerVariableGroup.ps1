[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Organization = "your-organization",
    [string]$Project = "your-project",
    [int]$BaseVariableGroupId = 0,
    [string]$DevVariableGroupName = "System-Uptime-Tracker-Dev",
    [string]$ProdVariableGroupName = "System-Uptime-Tracker-Prod",

    [Parameter(Mandatory = $true)]
    [string]$AccessToken,

    [ValidateSet("Bearer", "Pat")]
    [string]$AuthenticationScheme = "Bearer",

    [switch]$LeaveSecretsUnlocked
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function ConvertTo-VariableTable {
    param([object]$Group)

    $result = @{}
    if ($null -eq $Group) {
        return $result
    }

    foreach ($property in $Group.variables.PSObject.Properties) {
        $isSecretProperty = $property.Value.PSObject.Properties["isSecret"]
        $result[$property.Name] = @{
            value = $property.Value.value
            isSecret = $null -ne $isSecretProperty -and [bool]$isSecretProperty.Value
        }
    }
    return $result
}

function Get-SourceVariable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [hashtable[]]$Sources,
        [switch]$RequireReadableValue,
        [AllowEmptyString()]
        [string]$DefaultValue = ""
    )

    $unreadableSecret = $null
    foreach ($source in $Sources) {
        if ($source.ContainsKey($Name)) {
            $candidate = @{
                value = $source[$Name].value
                isSecret = [bool]$source[$Name].isSecret
            }
            if ($RequireReadableValue -and
                $candidate.isSecret -and
                [string]::IsNullOrWhiteSpace([string]$candidate.value)) {
                if ($null -eq $unreadableSecret) {
                    $unreadableSecret = $candidate
                }
                continue
            }
            return $candidate
        }
    }

    if ($null -ne $unreadableSecret) {
        return $unreadableSecret
    }

    return @{
        value = $DefaultValue
        isSecret = $false
    }
}

function Select-Variables {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Names,
        [Parameter(Mandatory = $true)]
        [hashtable[]]$Sources,
        [switch]$Unlock
    )

    $result = @{}
    foreach ($name in $Names) {
        $sourceVariable = Get-SourceVariable `
            -Name $name `
            -Sources $Sources `
            -RequireReadableValue:$Unlock
        $isUnreadableSecret = [bool]$sourceVariable.isSecret -and
            [string]::IsNullOrWhiteSpace([string]$sourceVariable.value)
        $result[$name] = @{
            value = $sourceVariable.value
            isSecret = if ($Unlock -and -not $isUnreadableSecret) {
                $false
            }
            else {
                [bool]$sourceVariable.isSecret
            }
        }
    }
    return $result
}

function Set-VariableValue {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Variables,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [AllowEmptyString()]
        [string]$Value,
        [switch]$Secret
    )

    $Variables[$Name] = @{
        value = $Value
        isSecret = if ($LeaveSecretsUnlocked) { $false } else { $Secret.IsPresent }
    }
}

function Get-VariableGroupByName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $encodedName = [Uri]::EscapeDataString($Name)
    $result = Invoke-RestMethod `
        -Method Get `
        -Uri "$variableGroupsUri&groupName=$encodedName" `
        -Headers $headers
    $matches = @($result.value | Where-Object { $_.name -eq $Name } | Select-Object -First 1)
    if ($matches.Count -eq 0) {
        return $null
    }
    return Invoke-RestMethod `
        -Method Get `
        -Uri "$variableGroupsBaseUri/$($matches[0].id)?api-version=7.1" `
        -Headers $headers
}

function Save-VariableGroup {
    param(
        [object]$ExistingGroup,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Description,
        [Parameter(Mandatory = $true)]
        [hashtable]$Variables
    )

    $references = @(
        if ($null -ne $ExistingGroup) {
            $ExistingGroup.variableGroupProjectReferences
        }
        else {
            foreach ($reference in $baseGroup.variableGroupProjectReferences) {
                @{
                    name = $Name
                    description = $Description
                    projectReference = $reference.projectReference
                }
            }
        }
    )

    $body = @{
        type = "Vsts"
        name = $Name
        description = $Description
        variables = $Variables
        variableGroupProjectReferences = $references
    }
    if ($null -ne $ExistingGroup) {
        $body.id = $ExistingGroup.id
    }

    $action = if ($null -eq $ExistingGroup) { "Create" } else { "Update" }
    if (-not $PSCmdlet.ShouldProcess(
            "$Organization/$Project variable group '$Name'",
            "$action SystemUptimeTracker variable group"
        )) {
        return $ExistingGroup
    }

    $method = if ($null -eq $ExistingGroup) { "Post" } else { "Put" }
    $uri = if ($null -eq $ExistingGroup) {
        $variableGroupsUri
    }
    else {
        "$variableGroupsBaseUri/$($ExistingGroup.id)?api-version=7.1"
    }

    return Invoke-RestMethod `
        -Method $method `
        -Uri $uri `
        -Headers $headers `
        -ContentType "application/json" `
        -Body ($body | ConvertTo-Json -Depth 12)
}

$encodedProject = [Uri]::EscapeDataString($Project)
$variableGroupsBaseUri = "https://dev.azure.com/$Organization/$encodedProject/_apis/distributedtask/variablegroups"
$variableGroupsUri = "${variableGroupsBaseUri}?api-version=7.1"
$authorization = if ($AuthenticationScheme -eq "Pat") {
    "Basic $([Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$AccessToken")))"
}
else {
    "Bearer $AccessToken"
}
$headers = @{ Authorization = $authorization }

$baseGroup = Invoke-RestMethod `
    -Method Get `
    -Uri "${variableGroupsBaseUri}/${BaseVariableGroupId}?api-version=7.1" `
    -Headers $headers
if ($baseGroup.name -ne "System-Uptime-Tracker") {
    throw "Variable group $BaseVariableGroupId is '$($baseGroup.name)', not 'System-Uptime-Tracker'."
}

$devGroup = Get-VariableGroupByName -Name $DevVariableGroupName
$prodGroup = Get-VariableGroupByName -Name $ProdVariableGroupName
$originalBaseVariables = ConvertTo-VariableTable -Group $baseGroup
$existingDevVariables = ConvertTo-VariableTable -Group $devGroup
$existingProdVariables = ConvertTo-VariableTable -Group $prodGroup

$baseVariableNames = @(
    "ADO_AGENT_NAME",
    "ADO_AGENT_POOL",
    "APPLICATION_NAME",
    "DOTNET_VERSION",
    "NODE_VERSION",
    "ReleaseVersion"
)
$sharedDeploymentVariableNames = @(
    "API_FEATURE_OPENTELEMETRY_ENABLED",
    "API_FEATURE_OPENTELEMETRY_SEQ_ENABLED",
    "API_REDACTION_KEY",
    "OpenTelemetry.ApiKey",
    "SYSTEMUPTIMETRACKER_SQL_APP_PASSWORD",
    "SYSTEMUPTIMETRACKER_SQL_APP_USERNAME",
    "UI_APP_OPENTELEMETRY_ENABLED",
    "UI_APP_OPENTELEMETRY_SEQ_ENABLED",
    "UI_AUTH_COOKIE_SECRET",
    "UI_IMPERSONATE_ENCRYPTION_KEY"
)
$devVariableNames = @(
    $sharedDeploymentVariableNames
    "API_APPLY_STARTUP_MIGRATIONS"
    "API_STORAGE_FILESYSTEM_ROOT"
    "DOCKER_APP_BASE_URL"
    "DOCKER_CERT_PATH"
    "DOCKER_COMPOSE_VERSION"
    "DOCKER_HOST"
    "DOCKER_RELEASE_ROOT"
    "DOCKER_TLS_VERIFY"
    "SYSTEMUPTIMETRACKER_SQL_BACKUP_VOLUME_NAME"
    "SYSTEMUPTIMETRACKER_SQL_BIND_HOST"
    "SYSTEMUPTIMETRACKER_SQL_EDITION"
    "SYSTEMUPTIMETRACKER_SQL_PUBLIC_PORT"
    "SYSTEMUPTIMETRACKER_SQL_SA_PASSWORD"
    "SYSTEMUPTIMETRACKER_SQL_VOLUME_NAME"
    "UI_APP_BASE_URL"
    "UI_BIND_HOST"
    "UI_MICROSOFT_CLIENT_SECRET"
    "UI_PUBLIC_PORT"
) | Sort-Object -Unique
$prodVariableNames = @(
    $sharedDeploymentVariableNames
    "ConnectionStrings.DefaultConnection"
    "IIS_API_BASE_URL"
    "IIS_APP_BASE_URL"
    "IIS_CERTIFICATE_THUMBPRINT"
    "IIS_DATA_ROOT"
    "IIS_RELEASE_ROOT"
    "IIS_SQL_DATABASE"
    "IIS_SQL_SERVER"
) | Sort-Object -Unique

$devOnlyVariableNames = @(
    $devVariableNames |
        Where-Object { $sharedDeploymentVariableNames -notcontains $_ }
)
$prodOnlyVariableNames = @(
    $prodVariableNames |
        Where-Object { $sharedDeploymentVariableNames -notcontains $_ }
)
$unclassifiedBaseDeploymentVariableNames = @(
    $originalBaseVariables.Keys |
        Where-Object {
            $baseVariableNames -notcontains $_ -and
            $devOnlyVariableNames -notcontains $_ -and
            $prodOnlyVariableNames -notcontains $_
        }
)
$preservedDevVariableNames = @(
    $existingDevVariables.Keys |
        Where-Object {
            $baseVariableNames -notcontains $_ -and
            $prodOnlyVariableNames -notcontains $_
        }
)
$preservedProdVariableNames = @(
    $existingProdVariables.Keys |
        Where-Object {
            $baseVariableNames -notcontains $_ -and
            $devOnlyVariableNames -notcontains $_
        }
)
$devVariableNames = @(
    $devVariableNames
    $unclassifiedBaseDeploymentVariableNames
    $preservedDevVariableNames
) | Sort-Object -Unique
$prodVariableNames = @(
    $prodVariableNames
    $unclassifiedBaseDeploymentVariableNames
    $preservedProdVariableNames
) | Sort-Object -Unique

$baseVariables = Select-Variables `
    -Names $baseVariableNames `
    -Sources @($originalBaseVariables) `
    -Unlock:$LeaveSecretsUnlocked
$devVariables = Select-Variables `
    -Names $devVariableNames `
    -Sources @($existingDevVariables, $originalBaseVariables) `
    -Unlock:$LeaveSecretsUnlocked
$prodVariables = Select-Variables `
    -Names $prodVariableNames `
    -Sources @($existingProdVariables, $originalBaseVariables) `
    -Unlock:$LeaveSecretsUnlocked

Set-VariableValue -Variables $baseVariables -Name "APPLICATION_NAME" -Value "System-Uptime-Tracker"
Set-VariableValue -Variables $baseVariables -Name "DOTNET_VERSION" -Value "10.0.x"
Set-VariableValue -Variables $baseVariables -Name "NODE_VERSION" -Value "24.x"
Set-VariableValue -Variables $baseVariables -Name "ReleaseVersion" -Value "1"
Set-VariableValue -Variables $baseVariables -Name "ADO_AGENT_POOL" -Value "Default"
Set-VariableValue -Variables $baseVariables -Name "ADO_AGENT_NAME" -Value "replace-with-agent-name"

Set-VariableValue -Variables $devVariables -Name "API_APPLY_STARTUP_MIGRATIONS" -Value "true"
Set-VariableValue -Variables $devVariables -Name "DOCKER_APP_BASE_URL" -Value "https://docker-app.example.com"
Set-VariableValue -Variables $devVariables -Name "DOCKER_COMPOSE_VERSION" -Value "v5.1.2"
Set-VariableValue -Variables $devVariables -Name "DOCKER_HOST" -Value "tcp://127.0.0.1:2375"
Set-VariableValue -Variables $devVariables -Name "DOCKER_RELEASE_ROOT" -Value "C:\Apps\SystemUptimeTracker\docker"
Set-VariableValue -Variables $devVariables -Name "SYSTEMUPTIMETRACKER_SQL_BACKUP_VOLUME_NAME" -Value "systemuptimetracker-production-sql-backups"
Set-VariableValue -Variables $devVariables -Name "SYSTEMUPTIMETRACKER_SQL_BIND_HOST" -Value "127.0.0.1"
Set-VariableValue -Variables $devVariables -Name "SYSTEMUPTIMETRACKER_SQL_EDITION" -Value "Express"
Set-VariableValue -Variables $devVariables -Name "SYSTEMUPTIMETRACKER_SQL_PUBLIC_PORT" -Value "11433"
Set-VariableValue -Variables $devVariables -Name "SYSTEMUPTIMETRACKER_SQL_VOLUME_NAME" -Value "systemuptimetracker-production-sql-data"
Set-VariableValue -Variables $devVariables -Name "UI_APP_BASE_URL" -Value "https://docker-app.example.com"
Set-VariableValue -Variables $devVariables -Name "UI_BIND_HOST" -Value "127.0.0.1"
Set-VariableValue -Variables $devVariables -Name "UI_PUBLIC_PORT" -Value "8001"

Set-VariableValue -Variables $prodVariables -Name "IIS_API_BASE_URL" -Value "https://api.example.com"
Set-VariableValue -Variables $prodVariables -Name "IIS_APP_BASE_URL" -Value "https://app.example.com"
Set-VariableValue -Variables $prodVariables -Name "IIS_DATA_ROOT" -Value "C:\AppData\SystemUptimeTracker"
Set-VariableValue -Variables $prodVariables -Name "IIS_RELEASE_ROOT" -Value "C:\Apps\SystemUptimeTracker\releases"
Set-VariableValue -Variables $prodVariables -Name "IIS_SQL_DATABASE" -Value "SystemUptimeTracker"
Set-VariableValue -Variables $prodVariables -Name "IIS_SQL_SERVER" -Value "replace-with-sql-server,1433"

$prodSqlUsername = [string]$prodVariables["SYSTEMUPTIMETRACKER_SQL_APP_USERNAME"].value
$prodSqlPassword = [string]$prodVariables["SYSTEMUPTIMETRACKER_SQL_APP_PASSWORD"].value
if (-not [string]::IsNullOrWhiteSpace($prodSqlUsername) -and
    -not [string]::IsNullOrWhiteSpace($prodSqlPassword)) {
    $prodSqlServer = [string]$prodVariables["IIS_SQL_SERVER"].value
    $prodConnectionString = "Server=$prodSqlServer;Database=SystemUptimeTracker;User Id=$prodSqlUsername;Password=`"$prodSqlPassword`";Encrypt=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
    Set-VariableValue `
        -Variables $prodVariables `
        -Name "ConnectionStrings.DefaultConnection" `
        -Value $prodConnectionString `
        -Secret
}
else {
    Write-Verbose "Preserving ConnectionStrings.DefaultConnection because Azure DevOps did not return readable SQL application credentials."
}

$devGroup = Save-VariableGroup `
    -ExistingGroup $devGroup `
    -Name $DevVariableGroupName `
    -Description "SystemUptimeTracker Docker development deployment configuration for pubd." `
    -Variables $devVariables
$prodGroup = Save-VariableGroup `
    -ExistingGroup $prodGroup `
    -Name $ProdVariableGroupName `
    -Description "System Uptime Tracker IIS deployment template configuration." `
    -Variables $prodVariables
$baseGroup = Save-VariableGroup `
    -ExistingGroup $baseGroup `
    -Name "System-Uptime-Tracker" `
    -Description "SystemUptimeTracker common build, version, and shared deployment-agent configuration." `
    -Variables $baseVariables

@(
    foreach ($group in @($baseGroup, $devGroup, $prodGroup)) {
        if ($null -ne $group) {
            [pscustomobject]@{
                Id = $group.id
                Name = $group.name
                VariableCount = @($group.variables.PSObject.Properties).Count
            }
        }
    }
)
