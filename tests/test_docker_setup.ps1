#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "toolbox-docker-setup-$([guid]::NewGuid().ToString('N'))"
$previousLog = $env:TOOLBOX_TEST_RUNTIME_LOG

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function New-SetupFixture {
    param([string]$Name)
    $fixture = Join-Path $testRoot $Name
    $containers = Join-Path $fixture "containers"
    New-Item -ItemType Directory -Path (Join-Path $fixture '.devcontainer') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot '.devcontainer/ensure_image.ps1') -Destination (Join-Path $fixture '.devcontainer')
    $certs = Join-Path $containers "certs"
    New-Item -ItemType Directory -Path $certs -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "docker_setup.ps1") -Destination $fixture
    Copy-Item -LiteralPath (Join-Path $repoRoot "docker_pull.ps1") -Destination $fixture
    Copy-Item -LiteralPath (Join-Path $repoRoot "docker_down.ps1") -Destination $fixture
    Copy-Item -LiteralPath (Join-Path $repoRoot "containers/.env.example") -Destination $containers
    Copy-Item -LiteralPath (Join-Path $repoRoot "containers/environment.ps1") -Destination $containers
    foreach ($group in @("common", "azure", "aws")) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "containers/docker-compose-$group.yml") -Destination $containers
    }
    # No certificate or generator exists, so tests never touch the user's trust store or JDK.
    Set-Content -LiteralPath (Join-Path $certs "wiremock.jks") -Value "fake-keystore"
    Add-Content -LiteralPath (Join-Path $containers ".env.example") -Value 'TOOLBOX_FUTURE_SETTING="${RABBITMQ_DEFAULT_USER}/literal#value"'
    return $fixture
}

function Invoke-SetupFixture {
    param([string]$Fixture, [bool]$ExpectSuccess = $true, [string]$Runtime = "docker")
    $env:TOOLBOX_TEST_RUNTIME_LOG = Join-Path $Fixture "runtime.log"
    Set-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Value "" -NoNewline
    $outputFile = Join-Path $Fixture "setup.log"
    $succeeded = $false
    try {
        & (Join-Path $Fixture "docker_setup.ps1") -ContainerRuntime $Runtime *> $outputFile
        $succeeded = $true
    } catch {
        Add-Content -LiteralPath $outputFile -Value ($_ | Out-String)
    }
    Assert-Condition ($succeeded -eq $ExpectSuccess) ("Unexpected setup result: " + (Get-Content -LiteralPath $outputFile -Raw))
    return Get-Content -LiteralPath $outputFile -Raw
}

function Assert-TemplateSettings {
    param([string]$Fixture)
    $envLines = @(Get-Content -LiteralPath (Join-Path $Fixture "containers/.env"))
    foreach ($line in Get-Content -LiteralPath (Join-Path $Fixture "containers/.env.example")) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=') {
            $key = $matches[1]
            $assignments = @($envLines | Where-Object { $_ -cmatch "^\s*(?:export\s+)?$([regex]::Escape($key))\s*=" })
            Assert-Condition ($assignments.Count -eq 1) "Expected exactly one assignment for '$key'."
        }
    }
}

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    # Shadow the runtime command instead of invoking Docker or Podman.
    $global:toolboxTestInvalidGroup = $null
    $global:toolboxTestVolumesExist = $false
    $global:toolboxTestFailedVolume = $null
    function global:docker {
        Add-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Value ($args -join " ")
        if ($args[0] -eq 'image' -and $args -contains '--format') { Write-Output '17.0.5035.5'; $global:LASTEXITCODE = 0; return }
        if ($args[0] -eq 'volume') {
            $global:LASTEXITCODE = 0
            if ($args[1] -eq 'inspect' -and -not $global:toolboxTestVolumesExist) { $global:LASTEXITCODE = 1 }
            if ($args[1] -eq 'create' -and $args[-1] -eq $global:toolboxTestFailedVolume) { $global:LASTEXITCODE = 23 }
            return
        }
        if ($args -contains "config" -and $args -contains "--services") {
            if ($global:toolboxTestInvalidGroup -and $args -contains "dev_$($global:toolboxTestInvalidGroup)_shared") {
                $global:LASTEXITCODE = 17
                return
            }
            Write-Output @("mssql", "postgres", "azurite", "service-bus", "dynamodb", "rabbitmq", "aws-emulator", "aws-init")
        }
        $global:LASTEXITCODE = 0
    }
    $global:toolboxTestPodmanServerVersion = "6.0.2"
    $global:toolboxTestPodmanRemote = "true"
    function global:podman {
        Add-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Value ($args -join " ")
        $global:LASTEXITCODE = 0
        if ($args[0] -eq 'image' -and $args -contains '--format') { Write-Output '17.0.5035.5'; $global:LASTEXITCODE = 0; return }
        if ($args[0] -eq 'volume') {
            if ($args[1] -eq 'inspect' -and -not $global:toolboxTestVolumesExist) { $global:LASTEXITCODE = 1 }
            if ($args[1] -eq 'create' -and $args[-1] -eq $global:toolboxTestFailedVolume) { $global:LASTEXITCODE = 23 }
            return
        }
        if ($args -contains '{{.Server.Version}}') { $global:toolboxTestPodmanServerVersion; return }
        if ($args -contains '{{.Host.ServiceIsRemote}}') { $global:toolboxTestPodmanRemote; return }
        if ($args -contains '{{.Version.Version}}') { "6.0.2"; return }
        if ($args -contains '{{.Host.RemoteSocket.Path}}') { throw "Moto must not request the Podman API socket." }
        if ($args -contains "config" -and $args -contains "--services") {
            Write-Output @("mssql", "postgres", "azurite", "service-bus", "dynamodb", "rabbitmq", "aws-emulator", "aws-init")
        }
    }

    $create = New-SetupFixture "create with spaces"
    $output = Invoke-SetupFixture $create
    Assert-TemplateSettings $create
    $created = Get-Content -LiteralPath (Join-Path $create "containers/.env") -Raw
    $template = Get-Content -LiteralPath (Join-Path $create "containers/.env.example") -Raw
    $generatedPattern = '^(MSSQL_SA_PASSWORD|POSTGRES_PASSWORD|RABBITMQ_DEFAULT_PASS|SEQ_STORAGE_SECRETKEY|WIREMOCK_KEYSTORE_PASSWORD)='
    $createdLines = @($created -split '\r?\n' | Where-Object { $_ -notmatch $generatedPattern })
    $templateLines = @($template -split '\r?\n' | Where-Object { $_ -notmatch $generatedPattern })
    Assert-Condition (($createdLines -join "`n") -eq ($templateLines -join "`n")) "Creating .env changed unmanaged template assignments."
    foreach ($key in @('MSSQL_SA_PASSWORD', 'POSTGRES_PASSWORD', 'RABBITMQ_DEFAULT_PASS',
                         'SEQ_STORAGE_SECRETKEY', 'WIREMOCK_KEYSTORE_PASSWORD')) {
        Assert-Condition ($created -cmatch "(?m)^$key=`"([^`"\r\n]+)`"\r?$") "A managed local credential remained blank."
        $generated = $matches[1]
        Assert-Condition ($generated -cne 'auto') "A managed auto placeholder was not generated."
        Assert-Condition (-not $output.Contains($generated)) "Setup printed a generated local credential."
    }
    $runtimeCalls = Get-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Raw
    Assert-Condition ($runtimeCalls.Contains("config --services")) "Setup did not resolve default services."
    Assert-Condition ($output.Contains("Moto SQS/SNS/EventBridge/S3")) "Setup did not report the free AWS emulator endpoint."
    Assert-Condition ($runtimeCalls.Contains("up --build -d")) "Successful preflight did not reach Compose up."
    foreach ($group in @("common", "azure", "aws")) {
        $expected = "docker-compose-$group.yml -p dev_${group}_shared"
        Assert-Condition ($runtimeCalls.Contains("$expected config --services")) "Setup did not validate the $group group."
        Assert-Condition ($runtimeCalls.Contains("$expected up --build -d")) "Setup did not build and start the $group group."
    }
    Assert-Condition (@($runtimeCalls -split '\r?\n' | Where-Object { $_ -match ' up --build -d$' }).Count -eq 3) "Setup must start exactly three groups."
    Assert-Condition (-not $runtimeCalls.Contains("--profile")) "Default startup unexpectedly requires profiles."
    foreach ($runtime in @('docker', 'podman')) {
        $freshVolumes = New-SetupFixture "$runtime fresh volumes"
        Invoke-SetupFixture $freshVolumes $true $runtime | Out-Null
        $runtimeCalls = Get-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Raw
        foreach ($group in @('azure', 'aws')) {
            $volumes = if ($group -eq 'azure') { @('cosmos-data', 'mssql-data', 'azurite-data') } else { @('postgres-data', 'dynamodb-data', 'rabbitmq-data') }
            foreach ($volume in $volumes) {
                $createCommand = "volume create --driver local --label com.docker.compose.project=dev_${group}_shared --label com.docker.compose.volume=$volume dev_common_shared_$volume"
                Assert-Condition ($runtimeCalls.Contains($createCommand)) "Setup did not provision $volume with a cleanup label."
                Assert-Condition ($runtimeCalls.IndexOf($createCommand) -lt $runtimeCalls.IndexOf('up --build -d')) "Volume provisioning happened after startup."
            }
        }
        $global:toolboxTestVolumesExist = $true
        Invoke-SetupFixture $freshVolumes $true $runtime | Out-Null
        $runtimeCalls = Get-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Raw
        Assert-Condition (-not $runtimeCalls.Contains('volume create') -and -not $runtimeCalls.Contains('volume rm')) "Setup modified retained data volumes."
        Assert-Condition (@($runtimeCalls -split '\r?\n' | Where-Object { $_ -match '^volume inspect ' }).Count -eq 6) "Setup did not inspect all retained cloud volumes."
        $global:toolboxTestVolumesExist = $false
        $global:toolboxTestFailedVolume = 'dev_common_shared_mssql-data'
        $failedVolumes = New-SetupFixture "$runtime failed volume creation"
        $output = Invoke-SetupFixture $failedVolumes $false $runtime
        Assert-Condition ($output.Contains('Unable to create shared data volume')) "Volume creation failure was unclear."
        $runtimeCalls = Get-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Raw
        Assert-Condition (-not $runtimeCalls.Contains('up --build -d') -and -not $runtimeCalls.Contains('ps -a --filter')) "Setup changed containers after volume provisioning failed."
        $global:toolboxTestFailedVolume = $null
    }
    foreach ($group in @("azure", "aws")) {
        $global:toolboxTestInvalidGroup = $group
        $invalid = New-SetupFixture "invalid $group group"
        $output = Invoke-SetupFixture $invalid $false
        Assert-Condition ($output.Contains("Unable to resolve the $group Compose services")) "Invalid group error was unclear."
        $runtimeCalls = Get-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Raw
        Assert-Condition (-not $runtimeCalls.Contains('up --build -d') -and -not $runtimeCalls.Contains('ps -a --filter')) "Setup must validate every group before migration or startup."
    }
    $global:toolboxTestInvalidGroup = $null

    $migrate = New-SetupFixture "migrate"
    $unicodeCredential = 'local;# $password-' + [char]0x00E9 + [char]0x4E2D
    $localAssignments = @(
        ("  export POSTGRES_PASSWORD = '" + $unicodeCredential + "'")
        'ConnectionStrings__SqlServer="local custom SQL connection"'
        'export Resources__Seq__ApiKey="fixture-file-secret-never-log"'
        'WIREMOCK_KEYSTORE_PASSWORD="local-keystore-password"'
        'TOOLBOX_CUSTOM_VALUE="$(this-must-not-execute)"'
        'resources__s3__bucket="unrelated-case-value"'
    )
    [System.IO.File]::WriteAllText((Join-Path $migrate "containers/.env"),
        ($localAssignments -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
    $output = Invoke-SetupFixture $migrate
    Assert-TemplateSettings $migrate
    $migratedLines = @(Get-Content -LiteralPath (Join-Path $migrate "containers/.env") -Encoding UTF8)
    foreach ($assignment in $localAssignments) {
        Assert-Condition ($migratedLines -contains $assignment) "Migration overwrote a quoted local assignment."
    }
    Assert-Condition ($migratedLines -contains 'TOOLBOX_FUTURE_SETTING="${RABBITMQ_DEFAULT_USER}/literal#value"') `
        "Migration did not copy a future template setting as raw data."
    Assert-Condition ($migratedLines -contains 'Resources__S3__Bucket="toolbox-samples"') `
        "A differently cased variable prevented the canonical assignment from being added."
    Assert-Condition (-not $output.Contains("fixture-file-secret-never-log")) "Setup printed a local API key."
    Assert-Condition (-not $output.Contains($unicodeCredential)) "Setup printed a custom Unicode credential."
    $firstMigration = Get-Content -LiteralPath (Join-Path $migrate "containers/.env") -Raw
    Invoke-SetupFixture $migrate | Out-Null
    Assert-Condition ($firstMigration -eq (Get-Content -LiteralPath (Join-Path $migrate "containers/.env") -Raw)) `
        "A second setup changed the migrated .env."

    $legacyEndpoint = New-SetupFixture "legacy AWS default endpoint"
    Set-Content -LiteralPath (Join-Path $legacyEndpoint "containers/.env") -Value `
        'export TOOLBOX_CONTAINER_AWS_ENDPOINT="http://localstack:4566"'
    Invoke-SetupFixture $legacyEndpoint | Out-Null
    $legacyLines = @(Get-Content -LiteralPath (Join-Path $legacyEndpoint "containers/.env"))
    Assert-Condition (@($legacyLines | Where-Object { $_ -cmatch '^\s*(?:export\s+)?TOOLBOX_CONTAINER_AWS_ENDPOINT\s*=' }).Count -eq 1) `
        "The endpoint migration introduced duplicate settings."
    Assert-Condition (($legacyLines -join "`n").Contains('http://aws-emulator:5000')) `
        "The old default AWS container endpoint was not migrated to the free emulator."
    Assert-Condition (-not (($legacyLines -join "`n").Contains('http://localstack:4566'))) `
        "The old default AWS endpoint remains active."

    $customEndpoint = New-SetupFixture "custom AWS endpoint"
    $customAssignment = 'export TOOLBOX_CONTAINER_AWS_ENDPOINT="http://localhost:5500"'
    Set-Content -LiteralPath (Join-Path $customEndpoint "containers/.env") -Value $customAssignment
    Invoke-SetupFixture $customEndpoint | Out-Null
    Assert-Condition (@(Get-Content -LiteralPath (Join-Path $customEndpoint "containers/.env")) -ccontains $customAssignment) `
        "The default endpoint migration overwrote a developer's explicit custom endpoint."

    $podman = New-SetupFixture "podman without licensing or socket"
    Invoke-SetupFixture $podman $true "podman" | Out-Null
    $runtimeCalls = Get-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Raw
    Assert-Condition ($runtimeCalls.Contains("up --build -d")) "Free AWS emulation did not reach Podman startup."
    foreach ($group in @("common", "azure", "aws")) {
        Assert-Condition ($runtimeCalls.Contains("docker-compose-$group.yml -p dev_${group}_shared up --build -d")) "Podman did not start the $group group."
    }
    Assert-Condition (-not $runtimeCalls.Contains('RemoteSocket.Path')) "Free AWS emulation still required the engine socket."
    $global:toolboxTestPodmanServerVersion = "<no value>"
    $global:toolboxTestPodmanRemote = "false"
    Invoke-SetupFixture $podman $true "podman" | Out-Null
    $runtimeCalls = Get-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Raw
    Assert-Condition ($runtimeCalls.Contains('{{.Version.Version}}')) "Native Podman did not check its engine version."
    $global:toolboxTestPodmanRemote = "true"
    foreach ($version in @("5.7.1", "invalid")) {
        $global:toolboxTestPodmanServerVersion = $version
        $versionFixture = New-SetupFixture "server version $version"
        $output = Invoke-SetupFixture $versionFixture $false "podman"
        Assert-Condition ($output.Contains("Podman") -and $output.Contains("6 or later")) "Unsupported server version error was unclear."
        Assert-Condition (-not (Get-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Raw).Contains("up --build -d")) "Unsupported Podman reached Compose up."
    }

    foreach ($runtime in @("docker", "podman")) {
        $teardown = New-SetupFixture "$runtime teardown"
        & (Join-Path $teardown "docker_down.ps1") -ContainerRuntime $runtime *> (Join-Path $teardown "down.log")
        $runtimeCalls = Get-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Raw
        foreach ($group in @("common", "azure", "aws")) {
            Assert-Condition ($runtimeCalls.Contains("docker-compose-$group.yml -p dev_${group}_shared down --remove-orphans")) "Teardown did not stop the $group group."
            foreach ($label in @("com.docker.compose.project", "io.podman.compose.project")) {
                Assert-Condition ($runtimeCalls.Contains("label=${label}=dev_${group}_shared")) "Teardown did not clean up the $group group."
            }
        }
        Assert-Condition (-not $runtimeCalls.Contains('volume rm') -and -not $runtimeCalls.Contains('--volumes')) "Normal teardown must retain data volumes."
    }
    Write-Host "PowerShell setup/teardown tests passed: three groups, additive configuration, generated credentials, free AWS emulation, and Podman engine versions."
} finally {
    $env:TOOLBOX_TEST_RUNTIME_LOG = $previousLog
    Remove-Item -LiteralPath "Function:docker" -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "Function:podman" -ErrorAction SilentlyContinue
    Remove-Variable -Name toolboxTestInvalidGroup -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable -Name toolboxTestVolumesExist, toolboxTestFailedVolume -Scope Global -ErrorAction SilentlyContinue
    # Delete only the unique fixture tree created by this test.
    $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
    $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedTestRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTestRoot) -like "toolbox-docker-setup-*") {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
