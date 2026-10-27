#Requires -Version 7.3
<#
.SYNOPSIS
Validates the public local OIDC source contracts without a container engine.
.DESCRIPTION
Checks agreement between native default realm, startup contract and external
browser/service descriptors. These source checks do not execute Keycloak or
establish protocol, application authorization or coverage results.
.EXAMPLE
pwsh -NoProfile -File tests/test_oidc_contract.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$script:cases = 0

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "OIDC source contract failed: $Message" }
}

function Test-Case {
    param([string]$Name, [scriptblock]$Action)
    & $Action
    $script:cases++
    Write-Output "PASS: $Name"
}

function Test-SameSet {
    param([object[]]$Actual, [object[]]$Expected)
    # Compare counts as well as values so duplicate registrations cannot hide.
    return $Actual.Count -eq $Expected.Count -and
        @((Compare-Object -ReferenceObject @($Expected | Sort-Object) -DifferenceObject @($Actual | Sort-Object) -CaseSensitive)).Count -eq 0
}

$documents = @{}
Test-Case 'all-public-json-documents-parse' {
    foreach ($entry in @{
        contract = 'containers/oidc/keycloak/contract.json'
        realm = 'containers/oidc/keycloak/default-realm.template.json'
        profiles = 'containers/oidc/contracts/protocol-profiles.json'
        project = 'containers/oidc/examples/profile.json'
        evidence = 'containers/oidc/contracts/evidence-format.json'
    }.GetEnumerator()) {
        $documents[$entry.Key] = Get-Content -LiteralPath (Join-Path $repositoryRoot $entry.Value) -Raw | ConvertFrom-Json -AsHashtable
    }
}
$contract = $documents.contract
$realm = $documents.realm
$profiles = $documents.profiles
$project = $documents.project

Test-Case 'contract-versions-and-project-replacement-agree' {
    Assert-Condition ($contract.contractVersion -eq 1 -and $profiles.contractVersion -eq $contract.contractVersion -and $project.contractVersion -eq $contract.contractVersion) 'Version declarations disagree.'
    Assert-Condition ($documents.evidence.formatVersion -eq 1) 'Unsupported receipt format.'
    Assert-Condition ($contract.sourceOnly -and $profiles.sourceOnly) 'Contract documents must remain source-only.'
    Assert-Condition ($contract.scope -ceq 'local-development-only' -and $profiles.scope -ceq $contract.scope -and $project.scope -ceq $contract.scope) 'Local-only scope must agree.'
    Assert-Condition ((Test-SameSet $contract.startup.profiles @('default', 'project')) -and $project.profile -ceq 'project') 'Unsupported startup profile.'
    Assert-Condition (-not $contract.project.defaultUsersImported -and -not $contract.project.defaultSecretsRequired -and -not $contract.project.runtimeDescriptorParser -and -not $contract.project.automaticRealmMerge) 'Project mode must replace the default profile.'
}

Test-Case 'dockerfile-upstream-default-is-accepted-latest' {
    $argument = $contract.image.upstreamBuildArgument
    $dockerfile = Get-Content -LiteralPath (Join-Path $repositoryRoot 'containers/oidc/keycloak/Dockerfile')
    $defaults = @($dockerfile | Where-Object { $_ -cmatch "^ARG $argument=(\S+)$" } | ForEach-Object { ($_ -split '=', 2)[1] })
    Assert-Condition ($contract.image.upstreamDefault -ceq 'quay.io/keycloak/keycloak:latest') 'Upstream default must be the accepted quay.io/keycloak/keycloak:latest.'
    Assert-Condition ($defaults.Count -eq 1 -and $defaults[0] -ceq $contract.image.upstreamDefault) 'Dockerfile and contract upstream defaults disagree.'
}

Test-Case 'default-personas-own-only-their-generic-realm-roles' {
    Assert-Condition ($realm.realm -ceq $contract.default.realm -and $realm.enabled) 'Default realm identity disagrees.'
    Assert-Condition (Test-SameSet @($realm.users.username) $contract.default.usersAndRealmRoles) 'Default user inventory differs.'
    Assert-Condition (Test-SameSet @($realm.roles.realm.name) $contract.default.usersAndRealmRoles) 'Default role inventory differs.'
    Assert-Condition (-not $realm.roles.Contains('client') -and -not $realm.Contains('defaultRole') -and -not $realm.Contains('defaultRoles')) 'Unexpected client or default role grants.'
    foreach ($role in $realm.roles.realm) {
        Assert-Condition (-not $role.Contains('composites') -and -not $role.Contains('composite')) 'Synthetic roles cannot inherit grants.'
    }
    foreach ($user in $realm.users) {
        Assert-Condition ($user.enabled -and (Test-SameSet $user.realmRoles @($user.username))) 'A persona must own only its named role.'
        Assert-Condition (-not $user.Contains('clientRoles') -and -not $user.Contains('groups') -and -not $user.Contains('attributes')) 'Unexpected management, group or domain grants.'
        Assert-Condition ($user.credentials.Count -eq 1 -and $user.credentials[0].type -ceq 'password' -and $user.credentials[0].temporary -eq $false) 'Each persona needs one runtime password.'
    }
    Assert-Condition (-not $contract.default.managementGrants -and -not $contract.default.applicationDomainGrants) 'Generic profile cannot promise application grants.'
}

Test-Case 'default-client-is-confidential-code-with-required-s256' {
    Assert-Condition ($realm.clients.Count -eq 1) 'Default realm must register one application client.'
    $client = $realm.clients[0]
    Assert-Condition ($client.clientId -ceq $contract.default.clientId -and $client.protocol -ceq 'openid-connect' -and $client.enabled) 'Default client identity disagrees.'
    Assert-Condition ($client.publicClient -eq $false -and $client.clientAuthenticatorType -ceq 'client-secret' -and $client.standardFlowEnabled -eq $true) 'Confidential authorization code is required.'
    Assert-Condition ($client.directAccessGrantsEnabled -eq $false -and $client.serviceAccountsEnabled -eq $false -and $client.implicitFlowEnabled -eq $false) 'Unexpected browser, password or service grant.'
    Assert-Condition ($client.attributes.'pkce.code.challenge.method' -ceq 'S256' -and $contract.default.pkce -ceq 'S256') 'S256 must be required.'
    Assert-Condition ($client.redirectUris.Count -eq 1 -and $client.redirectUris[0] -ceq '${TOOLBOX_OIDC_CALLBACK_URI}' -and $client.webOrigins.Count -eq 0) 'Callback must be exact and runtime-owned.'
}

Test-Case 'default-secrets-use-only-the-fixed-native-environment-placeholders' {
    $text = Get-Content -LiteralPath (Join-Path $repositoryRoot 'containers/oidc/keycloak/default-realm.template.json') -Raw
    $names = @([regex]::Matches($text, '\$\{([^}]+)\}') | ForEach-Object { $_.Groups[1].Value })
    $expectedNames = @($contract.default.requiredRuntimeSecrets) + @($contract.default.callbackEnvironment)
    Assert-Condition (Test-SameSet $names $expectedNames) 'Unexpected or missing environment placeholder.'
    Assert-Condition (Test-SameSet $contract.default.requiredRuntimeSecrets @('TOOLBOX_OIDC_ADMIN_PASSWORD', 'TOOLBOX_OIDC_USER_PASSWORD', 'TOOLBOX_OIDC_CLIENT_SECRET')) 'Unexpected secret input contract.'
    Assert-Condition ($realm.clients[0].secret -ceq '${TOOLBOX_OIDC_CLIENT_SECRET}') 'Client secret cannot be embedded.'
    foreach ($user in $realm.users) {
        $expected = if ($user.username -ceq 'admin') { '${TOOLBOX_OIDC_ADMIN_PASSWORD}' } else { '${TOOLBOX_OIDC_USER_PASSWORD}' }
        Assert-Condition ($user.credentials[0].value -ceq $expected) 'Persona secret cannot be embedded or exchanged.'
        Assert-Condition (-not $user.credentials[0].Contains('secretData') -and -not $user.credentials[0].Contains('credentialData')) 'Hashed credentials cannot be baked into the template.'
    }
}

Test-Case 'neutral-browser-descriptors-agree-with-their-startup-profiles' {
    Assert-Condition (Test-SameSet @($profiles.profiles.name) @('default-browser', 'project-browser', 'project-service')) 'Unexpected protocol profile inventory.'
    $defaultBrowser = @($profiles.profiles | Where-Object name -CEQ 'default-browser')[0]
    Assert-Condition ($defaultBrowser.startupProfile -ceq 'default' -and $defaultBrowser.clientId -ceq $contract.default.clientId -and $defaultBrowser.pkce -ceq $contract.default.pkce) 'Default browser descriptor disagrees.'
    Assert-Condition ((Test-SameSet $defaultBrowser.personas $contract.default.usersAndRealmRoles) -and (Test-SameSet $defaultBrowser.grantTypes $contract.default.grantTypes)) 'Default browser capabilities disagree.'
    $browser = @($profiles.profiles | Where-Object name -CEQ 'project-browser')[0]
    $client = @($project.clients | Where-Object clientId -CEQ $browser.clientId)
    Assert-Condition ($client.Count -eq 1 -and $browser.startupProfile -ceq $project.profile) 'Project browser is not registered exactly once.'
    Assert-Condition ((Test-SameSet $browser.grantTypes @('authorization_code', 'refresh_token')) -and (Test-SameSet $client[0].grantTypes $browser.grantTypes)) 'Project browser grants disagree.'
    Assert-Condition ($browser.pkce -ceq 'S256' -and $client[0].pkce -ceq $browser.pkce -and $client[0].tokenEndpointAuthMethod -ceq $browser.tokenEndpointAuthMethod) 'Project browser authentication disagrees.'
    foreach ($check in @('refresh-rotation', 'refresh-replay-rejected', 'logout', 'new-persona-login')) {
        Assert-Condition ($browser.requiredChecks -ccontains $check) 'Missing required browser lifecycle target.'
    }
}

Test-Case 'neutral-service-descriptors-exclude-browser-and-refresh-grants' {
    $service = @($profiles.profiles | Where-Object name -CEQ 'project-service')[0]
    $client = @($project.clients | Where-Object clientId -CEQ $service.clientId)
    Assert-Condition ($client.Count -eq 1 -and $service.startupProfile -ceq $project.profile) 'Project service is not registered exactly once.'
    Assert-Condition ((Test-SameSet $service.grantTypes @('client_credentials')) -and (Test-SameSet $client[0].grantTypes $service.grantTypes)) 'Project service grants disagree.'
    Assert-Condition ($client[0].browserLogin -eq $false -and $client[0].refreshToken -eq $false -and $client[0].tokenEndpointAuthMethod -ceq $service.tokenEndpointAuthMethod) 'Service client cannot inherit browser or refresh behavior.'
    Assert-Condition ((Test-SameSet @($project.clients.clientId) @($profiles.profiles | Where-Object startupProfile -CEQ 'project' | ForEach-Object clientId))) 'Project descriptor contains an unqualified client.'
    foreach ($check in @('service-audience', 'service-scopes', 'browser-grant-rejected', 'refresh-token-absent')) {
        Assert-Condition ($service.requiredChecks -ccontains $check) 'Missing required service isolation target.'
    }
    Assert-Condition (Test-SameSet $project.requiredClaims @('iss', 'aud', 'exp', 'sub')) 'Neutral token identity claim contract changed.'
}

Write-Output "Passed $cases source contract checks. Keycloak and protocol flows were not executed; coverage unavailable."
