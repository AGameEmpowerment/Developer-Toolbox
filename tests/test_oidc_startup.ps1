#Requires -Version 7.3
<#
.SYNOPSIS
Exercises the local-only OIDC image against a real Docker or Podman engine.
.DESCRIPTION
Requires a previously built image in the selected engine's local store. Uses
PowerShell's HTTP client and native container commands; no host application SDK
or test framework is required. Each run owns its containers and network. Public
receipts contain fixed check names, never imported documents, passwords or tokens.
Cold provider initialization has a five-minute readiness budget by default.
.EXAMPLE
./tests/test_oidc_startup.ps1 -ContainerRuntime docker -OutputPath ./artifacts/oidc/startup
#>
[CmdletBinding()]
param(
    [ValidateSet('docker', 'podman')]
    [string]$ContainerRuntime = 'docker',
    [ValidateNotNullOrEmpty()]
    [string]$ImageName = 'localhost/toolbox-oidc-keycloak:latest',
    [string]$DerivedImageName,
    [ValidateNotNullOrEmpty()]
    [string]$UpstreamImage = 'quay.io/keycloak/keycloak:latest',
    [string]$OutputPath,
    [ValidateRange(20, 600)]
    [int]$ReadinessTimeoutSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runId = 'oidc-startup-' + [guid]::NewGuid().ToString('N')
$ownedContainers = [System.Collections.Generic.List[string]]::new()
$checks = [System.Collections.Generic.List[object]]::new()
$networkCreated = $false
$fixtureRoot = $null
$failure = $null
$activeCheck = 'prerequisites'
$base = $null
$upstream = $null
$derived = $null
$engineIdentity = $null
$contextIdentity = $null
$runtimeExecutable = $null
$cleanupPassed = $true
$startedAt = [DateTimeOffset]::UtcNow
$revision = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$dirty = @(& git -C $repositoryRoot status --porcelain).Count -gt 0
$inputHashes = @('tests/test_oidc_startup.ps1', 'containers/oidc/keycloak/Dockerfile', 'containers/oidc/keycloak/startup.sh', 'containers/oidc/keycloak/default-realm.template.json', 'containers/oidc/keycloak/contract.json', 'containers/oidc/examples/project.Dockerfile', 'containers/oidc/examples/profile.json') | ForEach-Object {
    $inputPath = Join-Path $repositoryRoot $_
    if (Test-Path -LiteralPath $inputPath) { [pscustomobject]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash.ToLowerInvariant() } }
}
if (-not $OutputPath) { $OutputPath = Join-Path $repositoryRoot "artifacts/oidc/$runId" }
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) { throw 'OutputPath must be a new directory.' }
New-Item -ItemType Directory -Path $OutputPath | Out-Null

function Assert-Condition {
    param([bool]$Condition)
    # Do not interpolate assertions: response bodies and engine output may contain secrets.
    if (-not $Condition) { throw 'OIDC_STARTUP_ASSERTION_FAILED' }
}

function Invoke-Engine {
    param([string[]]$Argument, [hashtable]$Environment = @{}, [switch]$AllowFailure, [ValidateRange(1, 600)][int]$TimeoutSeconds = 60)
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($runtimeExecutable)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($item in $Argument) { $startInfo.ArgumentList.Add($item) }
    foreach ($key in $Environment.Keys) { $startInfo.Environment[$key] = $Environment[$key] }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        $null = $process.Start()
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            throw 'OIDC_STARTUP_ENGINE_TIMEOUT'
        }
        $result = [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $outputTask.GetAwaiter().GetResult()
            Error = $errorTask.GetAwaiter().GetResult()
        }
        if ($result.ExitCode -ne 0 -and -not $AllowFailure) { throw 'OIDC_STARTUP_ENGINE_COMMAND_FAILED' }
        return $result
    } finally { $process.Dispose() }
}

function Invoke-Check {
    param([string]$Name, [scriptblock]$Action)
    $script:activeCheck = $Name
    & $Action
    $checks.Add([pscustomobject]@{ name = $Name; result = 'passed' })
    Write-Output "PASS: $Name"
}

function New-Secret {
    return [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-Container {
    param([hashtable]$Environment, [string[]]$ExtraArgument = @(), [string[]]$Command = @(), [string]$RuntimeImageId = $base.Id)
    $name = "$runId-$($ownedContainers.Count)"
    # Register before creation so cancellation or a failed CLI response still gets cleanup.
    $ownedContainers.Add($name)
    $arguments = @('create', '--name', $name, '--label', "org.developertoolbox.oidc.test-run=$runId", '--network', $runId)
    foreach ($key in $Environment.Keys) { $arguments += @('--env', $key) }
    $arguments += $ExtraArgument
    $arguments += $RuntimeImageId
    $arguments += $Command
    $null = Invoke-Engine -Argument $arguments -Environment $Environment
    return $name
}

function New-DefaultEnvironment {
    return @{
        KC_HOSTNAME = 'http://localhost:18080'
        TOOLBOX_OIDC_PROFILE = 'default'
        TOOLBOX_OIDC_ADMIN_PASSWORD = New-Secret
        TOOLBOX_OIDC_USER_PASSWORD = New-Secret
        TOOLBOX_OIDC_CLIENT_SECRET = New-Secret
        TOOLBOX_OIDC_CALLBACK_URI = 'http://localhost:18081/callback'
    }
}

function Test-RejectedStartup {
    param([hashtable]$Environment, [string]$Code, [string]$Setup)
    $extra = @()
    $command = @()
    if ($Setup) {
        $extra = @('--entrypoint', '/bin/bash')
        $command = @('-ec', "$Setup; exec /opt/toolbox-oidc/startup.sh start-dev --import-realm")
    }
    $container = New-Container -Environment $Environment -ExtraArgument $extra -Command $command
    $null = Invoke-Engine -Argument @('start', $container)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    do {
        $state = (Invoke-Engine -Argument @('inspect', '--format', '{{json .State}}', $container)).Output | ConvertFrom-Json
        if (-not $state.Running) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    Assert-Condition (-not $state.Running -and $state.ExitCode -eq 78)
    $logs = (Invoke-Engine -Argument @('logs', $container)).Error + (Invoke-Engine -Argument @('logs', $container)).Output
    Assert-Condition ($logs.Trim() -ceq $Code)
    foreach ($key in $Environment.Keys | Where-Object { $_ -match 'PASSWORD|SECRET' }) {
        Assert-Condition (-not $logs.Contains($Environment[$key]))
    }
    $null = Invoke-Engine -Argument @('rm', $container)
}

function Get-FreePort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}

function Start-Provider {
    param([hashtable]$Environment, [string]$Fixture)
    $port = Get-FreePort
    do { $managementPort = Get-FreePort } while ($managementPort -eq $port)
    $Environment.KC_HOSTNAME = "http://127.0.0.1:$port"
    $providerImage = if ($Environment.TOOLBOX_OIDC_PROFILE -eq 'project' -and $derived) { $derived.Id } else { $base.Id }
    $container = New-Container -RuntimeImageId $providerImage -Environment $Environment -ExtraArgument @('-p', "127.0.0.1:${port}:8080", '-p', "127.0.0.1:${managementPort}:9000")
    if ($Fixture) { $null = Invoke-Engine -Argument @('cp', (Split-Path -Parent $Fixture), "${container}:/opt/keycloak/data/import") }
    $null = Invoke-Engine -Argument @('start', $container)
    $realm = if ($Environment.TOOLBOX_OIDC_PROFILE -eq 'project') { $Environment.TOOLBOX_OIDC_REALM } else { 'toolbox-local' }
    $issuer = "$($Environment.KC_HOSTNAME)/realms/$realm"
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($ReadinessTimeoutSeconds)
    $ready = $false
    do {
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$managementPort/health/ready" -TimeoutSec 3
            $discovery = Invoke-RestMethod -Uri "$issuer/.well-known/openid-configuration" -TimeoutSec 2
            Assert-Condition ($discovery.issuer -ceq $issuer)
            $expectedEndpoints = @{
                authorization_endpoint = "$issuer/protocol/openid-connect/auth"
                token_endpoint = "$issuer/protocol/openid-connect/token"
                userinfo_endpoint = "$issuer/protocol/openid-connect/userinfo"
                jwks_uri = "$issuer/protocol/openid-connect/certs"
                end_session_endpoint = "$issuer/protocol/openid-connect/logout"
            }
            foreach ($endpoint in $expectedEndpoints.Keys) { Assert-Condition ($discovery.$endpoint -ceq $expectedEndpoints[$endpoint]) }
            $jwks = Invoke-RestMethod -Uri $expectedEndpoints.jwks_uri -TimeoutSec 2
            $signingKeys = @($jwks.keys | Where-Object { $_.use -eq 'sig' -and $_.kty -eq 'RSA' -and $_.n -and $_.e })
            $ready = $health.status -eq 'UP' -and $discovery.issuer -ceq $issuer -and $signingKeys.Count -gt 0
        } catch {
            $ready = $false
            Write-Verbose "Readiness retry: $($_.Exception.GetType().Name), script line $($_.InvocationInfo.ScriptLineNumber)."
        }
        if ($ready) { break }
        $state = (Invoke-Engine -Argument @('inspect', '--format', '{{json .State}}', $container)).Output | ConvertFrom-Json
        if (-not $state.Running) { throw 'OIDC_STARTUP_PROVIDER_EXITED' }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    Assert-Condition $ready
    foreach ($endpoint in @('authorization_endpoint', 'token_endpoint', 'userinfo_endpoint', 'jwks_uri', 'end_session_endpoint')) {
        Assert-Condition ($discovery.$endpoint.StartsWith("$issuer/", [StringComparison]::Ordinal))
    }
    return [pscustomobject]@{ Container = $container; Issuer = $issuer; Origin = $Environment.KC_HOSTNAME; Discovery = $discovery }
}

function Read-TokenPayload {
    param([string]$Token)
    # Inspect claims only. Signature/issuer/audience verification belongs to protocol qualification.
    $encoded = $Token.Split('.')[1].Replace('-', '+').Replace('_', '/')
    $encoded = $encoded.PadRight($encoded.Length + ((4 - $encoded.Length % 4) % 4), '=')
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded)) | ConvertFrom-Json
}

function Invoke-UserLogin {
    param([object]$Provider, [hashtable]$Environment, [string]$Username, [string]$Password)
    $verifier = New-Secret
    $challenge = [Convert]::ToBase64String([Security.Cryptography.SHA256]::HashData([Text.Encoding]::ASCII.GetBytes($verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    $state = New-Secret
    $nonce = New-Secret
    $callback = $Environment.TOOLBOX_OIDC_CALLBACK_URI
    $query = "client_id=toolbox-local-web&response_type=code&scope=openid%20profile%20email&redirect_uri=$([uri]::EscapeDataString($callback))&state=$state&nonce=$nonce&code_challenge=$challenge&code_challenge_method=S256"
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $false
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(10)
    try {
        $login = $client.GetAsync("$($Provider.Discovery.authorization_endpoint)?$query").GetAwaiter().GetResult()
        Assert-Condition ([int]$login.StatusCode -eq 200)
        $html = $login.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $match = [regex]::Match($html, '<form[^>]+action="([^"]+)"')
        Assert-Condition $match.Success
        $action = [System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
        Assert-Condition ($action.StartsWith("$($Provider.Origin)/", [StringComparison]::Ordinal))
        $originUri = [uri]$Provider.Origin
        Assert-Condition ($originUri.Scheme -ceq 'http' -and $originUri.IsLoopback)
        Assert-Condition (([uri]$action).AbsolutePath -ceq "$($originUri.AbsolutePath.TrimEnd('/'))/realms/toolbox-local/login-actions/authenticate")
        # Native Keycloak marks pre-auth cookies Secure on loopback. Browsers
        # permit their local loopback exception; .NET's HTTP cookie selector
        # does not. Use an HTTPS URI view only to select native cookie paths,
        # then send them explicitly to this validated owned loopback action.
        # This is an HTTP startup smoke, not browser/SDK session qualification.
        $cookieOrigin = [UriBuilder]::new($originUri)
        $cookieOrigin.Scheme = 'https'
        $cookieOrigin.Port = $originUri.Port
        $cookieAction = [UriBuilder]::new([uri]$action)
        $cookieAction.Scheme = 'https'
        $cookieAction.Port = $originUri.Port
        $cookieJar = [System.Net.CookieContainer]::new()
        foreach ($setCookie in $login.Headers.GetValues('Set-Cookie')) { $cookieJar.SetCookies($cookieOrigin.Uri, $setCookie) }
        $cookieHeader = $cookieJar.GetCookieHeader($cookieAction.Uri)
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($cookieHeader))
        $fields = [System.Collections.Generic.Dictionary[string,string]]::new()
        $fields['username'] = $Username
        $fields['password'] = $Password
        $fields['credentialId'] = ''
        $content = [System.Net.Http.FormUrlEncodedContent]::new($fields)
        $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $action)
        $request.Content = $content
        $null = $request.Headers.TryAddWithoutValidation('Cookie', $cookieHeader)
        try { $result = $client.SendAsync($request).GetAwaiter().GetResult() } finally { $request.Dispose() }
        if ([int]$result.StatusCode -ne 302) {
            $responseBody = $result.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $category = if ($responseBody -match 'Invalid username or password|invalidUsernameOrPasswordMessage') { 'invalid-credentials' }
                elseif ($responseBody -match 'required-action|UPDATE_PROFILE|Update Account Information') { 'required-action' }
                elseif ($responseBody -match 'expired|restart') { 'expired-session' }
                else { 'unclassified' }
            Write-Verbose "login-response-status=$([int]$result.StatusCode); category=$category"
        }
        Assert-Condition ([int]$result.StatusCode -eq 302)
        $location = $result.Headers.Location.AbsoluteUri
        Assert-Condition ($location.StartsWith("${callback}?", [StringComparison]::Ordinal))
        $parameters = @{}
        foreach ($pair in ([uri]$location).Query.TrimStart('?').Split('&')) {
            $parts = $pair.Split('=', 2)
            $parameters[$parts[0]] = [uri]::UnescapeDataString($parts[1])
        }
        Assert-Condition ($parameters.state -ceq $state -and $parameters.ContainsKey('code'))
        $tokens = Invoke-RestMethod -Uri $Provider.Discovery.token_endpoint -Method Post -Body @{
            grant_type = 'authorization_code'; client_id = 'toolbox-local-web'; client_secret = $Environment.TOOLBOX_OIDC_CLIENT_SECRET
            code = $parameters.code; code_verifier = $verifier; redirect_uri = $callback
        } -TimeoutSec 10
        $claims = Read-TokenPayload $tokens.access_token
        $idClaims = Read-TokenPayload $tokens.id_token
        Assert-Condition ($idClaims.nonce -ceq $nonce -and $idClaims.iss -ceq $Provider.Issuer)
        Assert-Condition (@($claims.aud) -contains 'toolbox-local-web')
        Assert-Condition ($claims.preferred_username -ceq $Username -and @($claims.realm_access.roles) -contains $Username)
        Assert-Condition (-not $claims.PSObject.Properties['resource_access'] -or -not $claims.resource_access.PSObject.Properties['realm-management'])
        $userinfo = Invoke-RestMethod -Uri $Provider.Discovery.userinfo_endpoint -Headers @{ Authorization = "Bearer $($tokens.access_token)" } -TimeoutSec 10
        Assert-Condition ($userinfo.preferred_username -ceq $Username)
        $denied = Invoke-WebRequest -Uri "$($Provider.Origin)/admin/realms/toolbox-local/users" -Headers @{ Authorization = "Bearer $($tokens.access_token)" } -SkipHttpErrorCheck -TimeoutSec 10
        Assert-Condition ([int]$denied.StatusCode -eq 403)
    } finally { $client.Dispose(); $handler.Dispose() }
}

try {
    $runtimeExecutable = (Get-Command $ContainerRuntime -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    # Podman's remote info endpoint on WSL can take just over one minute even
    # when builds and container commands are fast. Keep that probe bounded.
    $metadataTimeout = if ($ContainerRuntime -eq 'podman') { 120 } else { 60 }
    $engineIdentity = (Invoke-Engine -Argument @('info', '--format', '{{json .}}') -TimeoutSeconds $metadataTimeout).Output | ConvertFrom-Json
    # Preserve only non-secret engine/context identity, never the complete info document.
    $engineIdentity = if ($ContainerRuntime -eq 'docker') {
        [pscustomobject]@{ name = $engineIdentity.Name; os = $engineIdentity.OSType; architecture = $engineIdentity.Architecture; version = $engineIdentity.ServerVersion }
    } else {
        [pscustomobject]@{ name = $engineIdentity.host.hostname; os = $engineIdentity.host.os; architecture = $engineIdentity.host.arch; version = $engineIdentity.version.Version }
    }
    $contextIdentity = if ($ContainerRuntime -eq 'docker') {
        (Invoke-Engine -Argument @('context', 'show')).Output.Trim()
    } else {
        $connections = @((Invoke-Engine -Argument @('system', 'connection', 'list', '--format=json')).Output | ConvertFrom-Json)
        $selectedConnection = if ($env:CONTAINER_CONNECTION) {
            @($connections | Where-Object Name -CEQ $env:CONTAINER_CONNECTION)
        } else { @($connections | Where-Object Default) }
        Assert-Condition ($selectedConnection.Count -eq 1)
        # Connection URIs can contain user/host information; retain only its name.
        $selectedConnection[0].Name
    }
    $base = @((Invoke-Engine -Argument @('image', 'inspect', $ImageName)).Output | ConvertFrom-Json)[0]
    $upstream = @((Invoke-Engine -Argument @('image', 'inspect', $UpstreamImage)).Output | ConvertFrom-Json)[0]
    if ($DerivedImageName) { $derived = @((Invoke-Engine -Argument @('image', 'inspect', $DerivedImageName)).Output | ConvertFrom-Json)[0] }
    $networkCreated = $true
    $null = Invoke-Engine -Argument @('network', 'create', '--label', "org.developertoolbox.oidc.test-run=$runId", $runId)

    Invoke-Check 'image-metadata-and-lean-layer-lineage' {
        Assert-Condition ($base.Config.User -eq $upstream.Config.User -and $base.Config.User -notin @('', '0', 'root'))
        Assert-Condition ($base.Config.Labels.'org.developertoolbox.oidc.contract' -eq '1')
        Assert-Condition (($base.Config.Entrypoint -join ' ') -ceq '/opt/toolbox-oidc/startup.sh')
        Assert-Condition (($base.Config.Cmd -join ' ') -ceq 'start-dev --import-realm')
        Assert-Condition ($base.Os -eq 'linux' -and $base.Architecture -eq 'amd64')
        Assert-Condition ($base.RootFS.Layers.Count -gt $upstream.RootFS.Layers.Count)
        for ($index = 0; $index -lt $upstream.RootFS.Layers.Count; $index++) {
            Assert-Condition ($base.RootFS.Layers[$index] -ceq $upstream.RootFS.Layers[$index])
        }
        $script:addedBytes = $base.Size - $upstream.Size
        $script:addedLayers = $base.RootFS.Layers.Count - $upstream.RootFS.Layers.Count
        Assert-Condition ($addedBytes -gt 0 -and $addedBytes -lt 1MB)
        $history = (Invoke-Engine -Argument @('history', '--no-trunc', '--format', '{{.CreatedBy}}', $base.Id)).Output.Trim().Split("`n")
        $upstreamHistory = (Invoke-Engine -Argument @('history', '--no-trunc', '--format', '{{.CreatedBy}}', $upstream.Id)).Output.Trim().Split("`n")
        $addedHistory = $history | Select-Object -First ($history.Count - $upstreamHistory.Count)
        Assert-Condition (($addedHistory -join "`n") -notmatch '(?im)\b(?:apt-get|microdnf|dnf|yum|apk|npm|pip)\s+(?:install|add)\b')
        if ($derived) {
            Assert-Condition ($derived.Config.User -ceq $base.Config.User)
            Assert-Condition (($derived.Config.Entrypoint -join ' ') -ceq ($base.Config.Entrypoint -join ' '))
            for ($index = 0; $index -lt $base.RootFS.Layers.Count; $index++) {
                Assert-Condition ($derived.RootFS.Layers[$index] -ceq $base.RootFS.Layers[$index])
            }
        }
    }
    Invoke-Check 'native-version-without-startup-inputs' {
        $container = New-Container -Environment @{} -Command @('--version')
        $result = Invoke-Engine -Argument @('start', '--attach', $container)
        Assert-Condition (($result.Output + $result.Error) -match 'Keycloak')
    }
    Invoke-Check 'image-files-match-current-source-hashes' {
        $container = New-Container -Environment @{} -ExtraArgument @('--entrypoint', '/bin/bash') -Command @('-ec', 'sha256sum /opt/toolbox-oidc/startup.sh /opt/toolbox-oidc/default-realm.template.json')
        $lines = (Invoke-Engine -Argument @('start', '--attach', $container)).Output.Trim().Split("`n")
        foreach ($sourceName in @('startup.sh', 'default-realm.template.json')) {
            $expected = ($inputHashes | Where-Object path -eq "containers/oidc/keycloak/$sourceName").sha256
            Assert-Condition (@($lines | Where-Object { $_.Trim() -ceq "$expected  /opt/toolbox-oidc/$sourceName" }).Count -eq 1)
        }
        if ($derived) {
            $container = New-Container -RuntimeImageId $derived.Id -Environment @{} -ExtraArgument @('--entrypoint', '/bin/bash') -Command @('-ec', 'sha256sum /opt/project-identity/profile.json')
            $actual = (Invoke-Engine -Argument @('start', '--attach', $container)).Output.Trim()
            $expected = ($inputHashes | Where-Object path -eq 'containers/oidc/examples/profile.json').sha256
            Assert-Condition ($actual -ceq "$expected  /opt/project-identity/profile.json")
        }
    }
    Invoke-Check 'upstream-missing-import-starts-without-requested-realm' {
        # Native Keycloak succeeds with an empty import directory. The maintained
        # wrapper must instead reject this mistake before running the provider.
        $port = Get-FreePort
        $container = New-Container -RuntimeImageId $upstream.Id -Environment @{
            KC_HOSTNAME = "http://127.0.0.1:$port"; KC_HTTP_ENABLED = 'true'
        } -ExtraArgument @('-p', "127.0.0.1:${port}:8080") -Command @('start-dev', '--import-realm')
        $null = Invoke-Engine -Argument @('start', $container)
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($ReadinessTimeoutSeconds)
        $ready = $false
        do {
            try {
                $master = Invoke-RestMethod -Uri "http://127.0.0.1:$port/realms/master/.well-known/openid-configuration" -TimeoutSec 3
                $ready = $master.issuer -ceq "http://127.0.0.1:$port/realms/master"
            } catch { $ready = $false }
            if ($ready) { break }
            Start-Sleep -Milliseconds 500
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        Assert-Condition $ready
        $missing = Invoke-WebRequest -Uri "http://127.0.0.1:$port/realms/fixture/.well-known/openid-configuration" -SkipHttpErrorCheck -TimeoutSec 10
        Assert-Condition ([int]$missing.StatusCode -eq 404)
        $null = Invoke-Engine -Argument @('stop', '--time', '20', $container)
    }
    $rejections = @(
        @{ Name = 'invalid-profile'; Code = 'TOOLBOX_OIDC_E_PROFILE'; Change = { param($e) $e.TOOLBOX_OIDC_PROFILE = 'unexpected' } },
        @{ Name = 'missing-hostname'; Code = 'TOOLBOX_OIDC_E_HOSTNAME'; Change = { param($e) $e.Remove('KC_HOSTNAME') } },
        @{ Name = 'hostname-with-path'; Code = 'TOOLBOX_OIDC_E_HOSTNAME'; Change = { param($e) $e.KC_HOSTNAME = 'http://localhost:18080/not-origin' } },
        @{ Name = 'missing-secret'; Code = 'TOOLBOX_OIDC_E_SECRET'; Change = { param($e) $e.Remove('TOOLBOX_OIDC_ADMIN_PASSWORD') } },
        @{ Name = 'short-secret'; Code = 'TOOLBOX_OIDC_E_SECRET'; Change = { param($e) $e.TOOLBOX_OIDC_USER_PASSWORD = 'short' } },
        @{ Name = 'unsafe-secret'; Code = 'TOOLBOX_OIDC_E_SECRET'; Change = { param($e) $e.TOOLBOX_OIDC_CLIENT_SECRET = ('x' * 30) + '"' } },
        @{ Name = 'wildcard-callback'; Code = 'TOOLBOX_OIDC_E_CALLBACK'; Change = { param($e) $e.TOOLBOX_OIDC_CALLBACK_URI = 'http://localhost:18081/*' } },
        @{ Name = 'conflicting-default-realm'; Code = 'TOOLBOX_OIDC_E_REALM'; Change = { param($e) $e.TOOLBOX_OIDC_REALM = 'other' } },
        @{ Name = 'invalid-project-realm'; Code = 'TOOLBOX_OIDC_E_REALM'; Change = { param($e) $e.TOOLBOX_OIDC_PROFILE = 'project'; $e.TOOLBOX_OIDC_REALM = '../outside' } },
        @{ Name = 'missing-project-import'; Code = 'TOOLBOX_OIDC_E_IMPORT_DIRECTORY'; Change = { param($e) $e.TOOLBOX_OIDC_PROFILE = 'project'; $e.TOOLBOX_OIDC_REALM = 'fixture' } }
    )
    foreach ($rejection in $rejections) {
        Invoke-Check "preflight-$($rejection.Name)" {
            $environment = New-DefaultEnvironment
            & $rejection.Change $environment
            Test-RejectedStartup -Environment $environment -Code $rejection.Code
        }
    }
    $importRejections = @(
        @{ Name = 'directory-import'; Code = 'TOOLBOX_OIDC_E_IMPORT'; Setup = 'mkdir -p /opt/keycloak/data/import/fixture-realm.json' },
        @{ Name = 'symlink-import'; Code = 'TOOLBOX_OIDC_E_IMPORT'; Setup = 'mkdir -p /opt/keycloak/data/import; printf "{}" > /opt/keycloak/data/other.json; ln -s /opt/keycloak/data/other.json /opt/keycloak/data/import/fixture-realm.json' },
        @{ Name = 'unreadable-import'; Code = 'TOOLBOX_OIDC_E_IMPORT'; Setup = 'mkdir -p /opt/keycloak/data/import; printf "{}" > /opt/keycloak/data/import/fixture-realm.json; chmod 000 /opt/keycloak/data/import/fixture-realm.json' },
        @{ Name = 'extra-import'; Code = 'TOOLBOX_OIDC_E_IMPORT_COUNT'; Setup = 'mkdir -p /opt/keycloak/data/import; printf "{}" > /opt/keycloak/data/import/fixture-realm.json; printf "{}" > /opt/keycloak/data/import/other-realm.json' }
    )
    foreach ($rejection in $importRejections) {
        Invoke-Check "preflight-$($rejection.Name)" {
            Test-RejectedStartup -Environment @{ KC_HOSTNAME = 'http://localhost:18080'; TOOLBOX_OIDC_PROFILE = 'project'; TOOLBOX_OIDC_REALM = 'fixture' } -Code $rejection.Code -Setup $rejection.Setup
        }
    }

    $environment = New-DefaultEnvironment
    Invoke-Check 'default-readiness-discovery-and-signing-jwks' { $script:provider = Start-Provider -Environment $environment }
    Invoke-Check 'effective-non-root-pid-one-and-private-import' {
        $result = Invoke-Engine -Argument @('exec', $provider.Container, '/bin/bash', '-ec', 'test "$(id -u)" != 0; test "$(stat -c %a /opt/keycloak/data/import/toolbox-local-realm.json)" = 600; IFS= read -r command < /proc/1/comm; test "$command" = java')
        Assert-Condition ($result.ExitCode -eq 0)
    }
    foreach ($username in @('admin', 'user')) {
        Invoke-Check "default-$username-code-login-and-management-denial" {
            $password = if ($username -eq 'admin') { $environment.TOOLBOX_OIDC_ADMIN_PASSWORD } else { $environment.TOOLBOX_OIDC_USER_PASSWORD }
            Invoke-UserLogin -Provider $provider -Environment $environment -Username $username -Password $password
        }
    }
    Invoke-Check 'native-sigterm-shutdown' {
        $null = Invoke-Engine -Argument @('stop', '--time', '20', $provider.Container)
        $state = (Invoke-Engine -Argument @('inspect', '--format', '{{json .State}}', $provider.Container)).Output | ConvertFrom-Json
        Assert-Condition (-not $state.Running -and $state.ExitCode -eq 143)
    }

    # The fixture contains native environment placeholders, never runtime credentials.
    $fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) $runId
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    $fixture = Join-Path $fixtureRoot 'project-fixture-realm.json'
    @{
        realm = 'project-fixture'; enabled = $true; sslRequired = 'none'; revokeRefreshToken = $true; refreshTokenMaxReuse = 0
        clients = @(
            @{
                clientId = 'toolbox-example-web'; protocol = 'openid-connect'; enabled = $true; publicClient = $false; clientAuthenticatorType = 'client-secret'
                secret = '${OIDC_FIXTURE_BROWSER_SECRET}'; standardFlowEnabled = $true; directAccessGrantsEnabled = $false; serviceAccountsEnabled = $false; implicitFlowEnabled = $false
                redirectUris = @('${OIDC_FIXTURE_CALLBACK_URI}'); webOrigins = @(); attributes = @{ 'pkce.code.challenge.method' = 'S256'; 'use.refresh.tokens' = 'true' }
                defaultClientScopes = @('profile', 'email', 'roles')
            },
            @{
                clientId = 'toolbox-example-service'; protocol = 'openid-connect'; enabled = $true; publicClient = $false; clientAuthenticatorType = 'client-secret'
                secret = '${OIDC_FIXTURE_SERVICE_SECRET}'; standardFlowEnabled = $false; directAccessGrantsEnabled = $false; serviceAccountsEnabled = $true; implicitFlowEnabled = $false
                attributes = @{ 'client_credentials.use_refresh_token' = 'false' }
                protocolMappers = @(@{ name = 'service-audience'; protocol = 'openid-connect'; protocolMapper = 'oidc-audience-mapper'; config = @{ 'included.client.audience' = 'toolbox-example-service'; 'access.token.claim' = 'true'; 'id.token.claim' = 'false' } })
            }
        )
        users = @(@{ username = 'project-persona'; enabled = $true; credentials = @(@{ type = 'password'; value = '${OIDC_FIXTURE_PASSWORD}'; temporary = $false }) })
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $fixture -Encoding utf8NoBOM
    $projectEnvironment = @{
        TOOLBOX_OIDC_PROFILE = 'project'; TOOLBOX_OIDC_REALM = 'project-fixture'; OIDC_FIXTURE_PASSWORD = New-Secret
        KC_BOOTSTRAP_ADMIN_USERNAME = 'owned-bootstrap'; KC_BOOTSTRAP_ADMIN_PASSWORD = New-Secret
        OIDC_FIXTURE_BROWSER_SECRET = New-Secret; OIDC_FIXTURE_SERVICE_SECRET = New-Secret; OIDC_FIXTURE_CALLBACK_URI = 'http://127.0.0.1:18081/callback'
    }
    Invoke-Check 'project-readiness-without-default-secrets' { $script:projectProvider = Start-Provider -Environment $projectEnvironment -Fixture $fixture }
    Invoke-Check 'project-replaces-default-users-and-realm' {
        $token = Invoke-RestMethod -Uri "$($projectProvider.Origin)/realms/master/protocol/openid-connect/token" -Method Post -Body @{
            grant_type = 'password'; client_id = 'admin-cli'; username = $projectEnvironment.KC_BOOTSTRAP_ADMIN_USERNAME; password = $projectEnvironment.KC_BOOTSTRAP_ADMIN_PASSWORD
        } -TimeoutSec 10
        $headers = @{ Authorization = "Bearer $($token.access_token)" }
        # Invoke-RestMethod emits a JSON array as one pipeline object. Assign it
        # before enumerating so a single returned user is not a nested array.
        $users = Invoke-RestMethod -Uri "$($projectProvider.Origin)/admin/realms/project-fixture/users" -Headers $headers -TimeoutSec 10
        $personas = @($users | Where-Object { $_.username -notlike 'service-account-*' })
        Assert-Condition ($personas.Count -eq 1 -and $personas[0].username -ceq 'project-persona')
        foreach ($user in $users) { Assert-Condition ($user.username -cin @('project-persona', 'service-account-toolbox-example-service')) }
        Assert-Condition (@($users.username) -notcontains 'admin' -and @($users.username) -notcontains 'user')
        $missing = Invoke-WebRequest -Uri "$($projectProvider.Origin)/realms/toolbox-local/.well-known/openid-configuration" -SkipHttpErrorCheck -TimeoutSec 10
        Assert-Condition ([int]$missing.StatusCode -eq 404)
        $descriptor = Get-Content -LiteralPath (Join-Path $repositoryRoot 'containers/oidc/examples/profile.json') -Raw | ConvertFrom-Json
        $clients = Invoke-RestMethod -Uri "$($projectProvider.Origin)/admin/realms/project-fixture/clients" -Headers $headers -TimeoutSec 10
        foreach ($expectedClient in $descriptor.clients) {
            $actualClient = @($clients | Where-Object clientId -CEQ $expectedClient.clientId)
            Assert-Condition ($actualClient.Count -eq 1 -and $actualClient[0].enabled -and -not $actualClient[0].publicClient)
            $configuration = Invoke-RestMethod -Uri "$($projectProvider.Origin)/admin/realms/project-fixture/clients/$($actualClient[0].id)" -Headers $headers -TimeoutSec 10
            Assert-Condition (-not $configuration.implicitFlowEnabled -and -not $configuration.directAccessGrantsEnabled)
            if ($expectedClient.grantTypes -contains 'authorization_code') {
                Assert-Condition ($configuration.standardFlowEnabled -and -not $configuration.serviceAccountsEnabled)
                Assert-Condition ($configuration.attributes.'pkce.code.challenge.method' -ceq 'S256')
                Assert-Condition ($configuration.redirectUris.Count -eq 1 -and $configuration.redirectUris[0] -ceq $projectEnvironment.OIDC_FIXTURE_CALLBACK_URI)
                Assert-Condition ($configuration.attributes.'use.refresh.tokens' -ceq 'true')
            } else {
                Assert-Condition ($configuration.serviceAccountsEnabled -and -not $configuration.standardFlowEnabled)
                Assert-Condition ($configuration.attributes.'client_credentials.use_refresh_token' -ceq 'false')
                $serviceAccount = Invoke-RestMethod -Uri "$($projectProvider.Origin)/admin/realms/project-fixture/clients/$($actualClient[0].id)/service-account-user" -Headers $headers -TimeoutSec 10
                Assert-Condition ($serviceAccount.username -ceq 'service-account-toolbox-example-service')
            }
        }
    }
} catch {
    $failure = 'OIDC_STARTUP_CHECK_FAILED'
    $checks.Add([pscustomobject]@{ name = $activeCheck; result = 'failed' })
    Write-Output "FAIL: $activeCheck"
    Write-Verbose "Failure type $($_.Exception.GetType().Name) at script line $($_.InvocationInfo.ScriptLineNumber)."
    Write-Verbose $_.ScriptStackTrace
} finally {
    foreach ($name in $ownedContainers) {
        try {
            $exists = Invoke-Engine -Argument @('inspect', '--format', '{{.Name}}', $name) -AllowFailure
            if ($exists.ExitCode -eq 0) {
                $removed = Invoke-Engine -Argument @('rm', '--force', $name) -AllowFailure
                if ($removed.ExitCode -ne 0) { $cleanupPassed = $false }
            }
        } catch { $cleanupPassed = $false }
    }
    if ($networkCreated) {
        try {
            $removed = Invoke-Engine -Argument @('network', 'rm', $runId) -AllowFailure
            if ($removed.ExitCode -ne 0) { $cleanupPassed = $false }
        } catch { $cleanupPassed = $false }
    }
    if ($ownedContainers.Count -gt 0 -or $networkCreated) {
        try {
            $remainingContainers = Invoke-Engine -Argument @('ps', '--all', '--filter', "label=org.developertoolbox.oidc.test-run=$runId", '--format', '{{.Names}}')
            $remainingNetworks = Invoke-Engine -Argument @('network', 'ls', '--filter', "label=org.developertoolbox.oidc.test-run=$runId", '--format', '{{.Name}}')
            if ($remainingContainers.Output.Trim() -or $remainingNetworks.Output.Trim()) { $cleanupPassed = $false }
        } catch { $cleanupPassed = $false }
    }
    if ($fixtureRoot -and (Test-Path -LiteralPath $fixtureRoot)) {
        $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
        $expectedFixture = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) $runId))
        if ($resolvedFixture -ceq $expectedFixture -and (Split-Path -Leaf $resolvedFixture) -ceq $runId) {
            try { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force } catch { $cleanupPassed = $false }
        } else { $cleanupPassed = $false }
    }
    $receipt = [ordered]@{
        schemaVersion = 1; kind = 'toolbox-oidc-startup'; runId = $runId; timestampUtc = [DateTimeOffset]::UtcNow.ToString('o')
        startedAtUtc = $startedAt.ToString('o'); completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        sources = @(@{ repository = 'Developer-Toolbox'; commit = $revision; dirty = $dirty; inputHashes = @($inputHashes) })
        engine = @{ runtime = $ContainerRuntime; context = $contextIdentity; measured = $engineIdentity; hostOs = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription }
        platform = $(if ($base) { "$($base.Os)/$($base.Architecture)" } else { $null })
        images = @{
            upstreamReference = $UpstreamImage; upstreamId = $(if ($upstream) { $upstream.Id } else { $null })
            baseReference = $ImageName; baseId = $(if ($base) { $base.Id } else { $null }); derivedReference = $DerivedImageName; derivedId = $(if ($derived) { $derived.Id } else { $null }); derivedQualification = $(if ($derived) { 'selected for project startup' } else { 'not run: no DerivedImageName supplied' })
            platform = $(if ($base) { "$($base.Os)/$($base.Architecture)" } else { $null })
            contractVersion = '1'; sourceRevisionLabel = $(if ($base) { $base.Config.Labels.'org.opencontainers.image.revision' } else { $null }); addedBytes = $(if (Get-Variable addedBytes -ErrorAction SilentlyContinue) { $addedBytes } else { $null })
            addedLayers = $(if (Get-Variable addedLayers -ErrorAction SilentlyContinue) { $addedLayers } else { $null })
        }
        profile = @{ selections = @('default', 'project', 'upstream-empty-import-comparison'); topology = 'owned bridge, host loopback published ports'; issuerReachability = 'host PowerShell HTTP smoke' }
        command = @{ executable = 'pwsh'; script = 'tests/test_oidc_startup.ps1'; containerRuntime = $ContainerRuntime; imageName = $ImageName; derivedImageName = $DerivedImageName; upstreamImage = $UpstreamImage; readinessTimeoutSeconds = $ReadinessTimeoutSeconds; outputPath = $OutputPath; prerequisites = @('PowerShell 7.3+', 'reachable selected engine', 'prebuilt base and upstream images in that engine store') }
        inventory = @{ framework = 'standalone PowerShell'; area = 'real-container startup'; standaloneScripts = 1; authoredDefinitions = 12; authoredDefinitionMethod = 'literal Invoke-Check call sites, excluding function declaration'; discoveredExpandedCases = 25; browserExecutions = 0; discoveredHttpPersonaCases = 2; executedHttpPersonaCases = @($checks | Where-Object name -like 'default-*-code-login-and-management-denial').Count; snapshots = 0; parameterExpansion = '10 input rejections, 4 import rejections, 2 HTTP persona logins' }
        checks = @($checks.ToArray())
        results = @{ framework = 'standalone PowerShell'; area = 'real-container startup'; passed = @($checks | Where-Object { $_.result -eq 'passed' -and $_.name -ne 'prerequisites' }).Count; failed = @($checks | Where-Object { $_.result -eq 'failed' -and $_.name -ne 'prerequisites' }).Count; environmentFailures = @($checks | Where-Object name -eq 'prerequisites').Count; skipped = 0; notRun = 25 - @($checks | Where-Object name -ne 'prerequisites').Count; unexecutedAfterFailure = $(if ($failure) { 'remaining cases not run' } else { 'none' }) }
        coverage = 'unavailable'; cleanup = @{ passed = $cleanupPassed; scope = 'uniquely owned containers, network and non-secret fixture directory' }
    }
    $receipt | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputPath 'startup-receipt.json') -Encoding utf8NoBOM
}
if ($failure -or -not $cleanupPassed) { throw 'OIDC startup qualification failed; inspect fixed check results in startup-receipt.json.' }
Write-Output "Passed $($checks.Count) real-container startup checks. Receipt: $OutputPath"
