# Local development only. Generate managed placeholders without printing secrets.
function Get-ToolboxWireMockPassword {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$EnvFile,
        [Parameter(Mandatory)][ValidateSet('docker', 'podman')][string]$ContainerRuntime
    )
    . (Join-Path $PSScriptRoot '../.devcontainer/ensure_image.ps1')
    Ensure-ToolboxImage -Runtime $ContainerRuntime -Image 'wiremock/wiremock:latest'
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ('toolbox-cert-' + [guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path $temporary | Out-Null
        $isPosix = [IO.Path]::DirectorySeparatorChar -eq '/'
        if ($isPosix) {
            & chmod 700 $temporary
            if ($LASTEXITCODE -ne 0) { throw 'Unable to protect the certificate utility directory.' }
        }
        $service = @{
            image = 'wiremock/wiremock:latest'
            pull_policy = 'never'
            network_mode = 'none'
            environment = @{ TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD = '${WIREMOCK_KEYSTORE_PASSWORD:-changeit}' }
            entrypoint = @('/bin/sh', '-c')
            command = @('umask 077; printf %s "$${TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD}" > /toolbox-secret/value')
            volumes = @("${temporary}:/toolbox-secret:rw,z")
        }
        if ($ContainerRuntime -eq 'podman') {
            $service.userns_mode = 'keep-id'
        } elseif ($isPosix) {
            $userId = (& id -u).Trim()
            $groupId = (& id -g).Trim()
            if ($userId -notmatch '^\d+$' -or $groupId -notmatch '^\d+$') { throw 'Cannot determine certificate utility ownership.' }
            $service.user = "${userId}:${groupId}"
        }
        $definition = @{ 'x-podman' = @{ in_pod = $false }; services = @{ password = $service } }
        $composeFile = Join-Path $temporary 'compose.json'
        [IO.File]::WriteAllText($composeFile, ($definition | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
        & $ContainerRuntime compose --env-file $EnvFile -f $composeFile -p (Split-Path -Leaf $temporary) run --rm -T password *> $null
        if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve the WireMock password with the selected Compose provider.' }
        $valueFile = Join-Path $temporary 'value'
        if (-not (Test-Path -LiteralPath $valueFile -PathType Leaf)) { throw 'The certificate utility did not produce its private value.' }
        return [IO.File]::ReadAllText($valueFile, [Text.UTF8Encoding]::new($false))
    } finally {
        if (Test-Path -LiteralPath $temporary) {
            $resolvedTemporary = [IO.Path]::GetFullPath($temporary)
            $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
            if (-not $resolvedTemporary.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
                (Split-Path -Leaf $resolvedTemporary) -notmatch '^toolbox-cert-[a-f0-9]{32}$') {
                throw 'Unexpected certificate utility cleanup path.'
            }
            Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
        }
    }
}

function Initialize-ToolboxEnvironment {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$EnvFile)

    $script:ToolboxGeneratedWireMockPassword = $false

    if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) {
        throw "The toolbox environment file must exist before generating local credentials."
    }
    $secretKeys = @('MSSQL_SA_PASSWORD', 'POSTGRES_PASSWORD', 'RABBITMQ_DEFAULT_PASS',
        'SEQ_STORAGE_SECRETKEY', 'WIREMOCK_KEYSTORE_PASSWORD')
    $lines = @(Get-Content -LiteralPath $EnvFile -Encoding UTF8)
    $values = [System.Collections.Hashtable]::new([System.StringComparer]::Ordinal)
    $legacyAwsEndpoint = $false
    foreach ($line in $lines) {
        if ($line -cmatch '^\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$') {
            $key = $matches[1]
            $rawValue = $matches[2]
            if ($key -ceq 'TOOLBOX_CONTAINER_AWS_ENDPOINT') {
                $legacyAwsEndpoint = $rawValue -cmatch '^(?:"http://localstack:4566"|''http://localstack:4566'')\s*(?:#.*)?$|^http://localstack:4566(?:\s+#.*|\s*)$'
            }
            # Recognize only literal placeholders; never evaluate dotenv as script.
            $values[$key] = if ($rawValue -cmatch '^(?:"auto"|''auto'')\s*(?:#.*)?$|^auto(?:\s+#.*|\s*)$') { 'auto' }
                elseif ($rawValue -cmatch '^(?:""|''''|)\s*(?:#.*)?$') { '' }
                else { 'explicit' }
        }
    }
    $replacements = [System.Collections.Hashtable]::new([System.StringComparer]::Ordinal)
    if ($legacyAwsEndpoint) {
        $replacements['TOOLBOX_CONTAINER_AWS_ENDPOINT'] = 'TOOLBOX_CONTAINER_AWS_ENDPOINT="http://aws-emulator:5000"'
    }
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        foreach ($key in $secretKeys) {
            if (-not $values.ContainsKey($key)) { continue }
            if ($values[$key] -cne 'auto') { continue }
            $bytes = New-Object byte[] 32
            $random.GetBytes($bytes)
            $value = if ($key -ceq 'SEQ_STORAGE_SECRETKEY') {
                [Convert]::ToBase64String($bytes)
            } else {
                # SQL Server requires several character classes; keep dotenv-safe characters.
                'Tb!1' + [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
            }
            $replacements[$key] = "$key=`"$value`""
            if ($key -ceq 'WIREMOCK_KEYSTORE_PASSWORD') { $script:ToolboxGeneratedWireMockPassword = $true }
        }
    } finally { $random.Dispose() }
    if ($replacements.Count -eq 0) { return }

    $lines = @($lines | ForEach-Object {
        if ($_ -cmatch '^\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=' -and
            $replacements.ContainsKey($matches[1])) { $replacements[$matches[1]] } else { $_ }
    })
    $directory = Split-Path -Parent ([System.IO.Path]::GetFullPath($EnvFile))
    $temporary = Join-Path $directory ('.env-generated-' + [guid]::NewGuid().ToString('N'))
    try {
        # Create the temporary file before writing credentials, restricting POSIX permissions first.
        $stream = [System.IO.File]::Open($temporary, [System.IO.FileMode]::CreateNew)
        $stream.Dispose()
        if ([System.IO.Path]::DirectorySeparatorChar -eq '/') {
            & chmod 600 $temporary
            if ($LASTEXITCODE -ne 0) { throw 'Unable to protect the generated environment file.' }
        }
        [System.IO.File]::WriteAllText($temporary, ($lines -join [Environment]::NewLine) + [Environment]::NewLine,
            [System.Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $EnvFile -Force
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
    Write-Host 'Local environment initialized in containers/.env; custom values preserved.' -ForegroundColor Green
}
