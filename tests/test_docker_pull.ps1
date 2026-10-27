#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "toolbox-docker-pull-$([guid]::NewGuid().ToString('N'))"
$previousLog = $env:TOOLBOX_TEST_RUNTIME_LOG
$previousFixture = $env:TOOLBOX_TEST_FIXTURE

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function New-PullFixture {
    param([string]$Name)
    $fixture = Join-Path $testRoot $Name
    $containers = Join-Path $fixture "containers"
    New-Item -ItemType Directory -Path (Join-Path $fixture '.devcontainer') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot '.devcontainer/ensure_image.ps1') -Destination (Join-Path $fixture '.devcontainer')
    New-Item -ItemType Directory -Path $containers -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "docker_pull.ps1") -Destination $fixture
    Copy-Item -LiteralPath (Join-Path $repoRoot "containers/.env.example") -Destination $containers
    foreach ($group in @("common", "azure", "aws")) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "containers/docker-compose-$group.yml") -Destination $containers
    }
    Add-Content -LiteralPath (Join-Path $containers ".env.example") -Value 'TOOLBOX_FUTURE_SETTING="${POSTGRES_USER}/literal#value"'
    return $fixture
}

function Assert-TemplateSettings {
    param([string]$Fixture)
    $envFile = Join-Path $Fixture "containers/.env"
    Assert-Condition (Test-Path -LiteralPath $envFile) "Pull must create containers/.env before invoking Compose."
    $envLines = @(Get-Content -LiteralPath $envFile)
    foreach ($line in Get-Content -LiteralPath (Join-Path $Fixture "containers/.env.example")) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=') {
            $key = $matches[1]
            $assignments = @($envLines | Where-Object { $_ -cmatch "^\s*(?:export\s+)?$([regex]::Escape($key))\s*=" })
            Assert-Condition ($assignments.Count -eq 1) "Expected exactly one assignment for '$key' before Compose pull."
        }
    }
}

function Invoke-MockRuntime {
    param([string]$Runtime, [string[]]$Arguments)
    Add-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Value ("$Runtime " + ($Arguments -join " "))
    Assert-Condition (-not (@($Arguments | Where-Object { $_ -in @("up", "start", "restart", "run", "create") }).Count)) "Pull unexpectedly started or created services."
    if ($Arguments[0] -eq "compose" -and $Arguments[-1] -eq "pull") {
        $envIndex = [array]::IndexOf($Arguments, "--env-file")
        Assert-Condition ($envIndex -ge 0 -and $envIndex + 1 -lt $Arguments.Count) "Compose pull omitted --env-file."
        $expected = Join-Path $env:TOOLBOX_TEST_FIXTURE "containers/.env"
        Assert-Condition ($Arguments[$envIndex + 1] -ceq $expected) "Compose pull must use the repaired developer .env, not the template."
        Assert-TemplateSettings $env:TOOLBOX_TEST_FIXTURE
        Add-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Value "template-ready-before-pull"
    }
    $global:LASTEXITCODE = 0
}

function Invoke-PullFixture {
    param([string]$Fixture, [string]$Runtime)
    $env:TOOLBOX_TEST_FIXTURE = $Fixture
    $env:TOOLBOX_TEST_RUNTIME_LOG = Join-Path $Fixture "runtime.log"
    Set-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG -Value "" -NoNewline
    $outputFile = Join-Path $Fixture "pull.log"
    Push-Location -LiteralPath $Fixture
    try {
        & (Join-Path $Fixture "docker_pull.ps1") -ContainerRuntime $Runtime *> $outputFile
    } finally {
        Pop-Location
    }
    Assert-Condition ($LASTEXITCODE -eq 0) "Pull returned a failure."
    $calls = @(Get-Content -LiteralPath $env:TOOLBOX_TEST_RUNTIME_LOG)
    Assert-Condition (@($calls | Where-Object { $_ -match ' pull ' }).Count -eq 0) 'Cached images must never be pulled.'
    foreach ($image in @('mcr.microsoft.com/mssql/server:latest', 'docker.io/library/python:latest')) {
        Assert-Condition ($calls -contains "$Runtime image inspect $image") 'Missing base-image cache inspection.'
    }
    $output = Get-Content -LiteralPath $outputFile -Raw
    foreach ($secret in @("fixture-secret-never-log", "custom-connection-never-log", "custom-key-never-log")) {
        Assert-Condition (-not $output.Contains($secret)) "Pull printed a developer value."
    }
}

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    function global:docker { Invoke-MockRuntime -Runtime "docker" -Arguments $args }
    function global:podman { Invoke-MockRuntime -Runtime "podman" -Arguments $args }

    foreach ($runtime in @("docker", "podman")) {
        $fresh = New-PullFixture "$runtime fresh with spaces"
        Invoke-PullFixture $fresh $runtime
        $expected = [System.IO.File]::ReadAllText((Join-Path $fresh "containers/.env.example"))
        $created = [System.IO.File]::ReadAllText((Join-Path $fresh "containers/.env"))
        Assert-Condition ($created -ceq $expected) "Fresh .env creation changed template contents."

        $stale = New-PullFixture "$runtime stale without final newline"
        $localLines = @(
            '# Retain this developer comment.'
            '  export POSTGRES_PASSWORD = ''fixture-secret-never-log;# $password'''
            'ConnectionStrings__SqlServer="custom-connection-never-log"'
            'export Resources__Seq__ApiKey="fixture-secret-never-log"'
            'FUNCTIONS_HOST_KEY="custom-key-never-log"'
            'FUNCTIONS_MASTER_KEY='
            'TOOLBOX_CUSTOM_VALUE="$(touch must-not-execute)"'
            'functions_secrets_path="unrelated-case-value"'
            'Resources__S3__Endpoint="${TOOLBOX_AWS_ENDPOINT}/custom#literal"'
        )
        $path = Join-Path $stale "containers/.env"
        [System.IO.File]::WriteAllText($path, ($localLines -join "`n"), [System.Text.UTF8Encoding]::new($false))
        Invoke-PullFixture $stale $runtime
        $migrated = @(Get-Content -LiteralPath $path)
        foreach ($line in $localLines) {
            Assert-Condition ($migrated -ccontains $line) "Migration overwrote a developer assignment or joined the final line."
        }
        Assert-Condition ($migrated -ccontains 'TOOLBOX_FUTURE_SETTING="${POSTGRES_USER}/literal#value"') "Migration expanded raw template interpolation."
        Assert-Condition (-not (Test-Path -LiteralPath (Join-Path $stale "must-not-execute"))) "Migration evaluated developer configuration."
        $before = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($path))
        Invoke-PullFixture $stale $runtime
        $after = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($path))
        Assert-Condition ($before -ceq $after) "Repeated pull changed complete developer configuration."
    }
    Write-Output "PowerShell pull tests passed: fresh/stale configuration, Docker/Podman, pre-pull repair, idempotence, and secret preservation."
} finally {
    Remove-Item Function:\docker -ErrorAction SilentlyContinue
    Remove-Item Function:\podman -ErrorAction SilentlyContinue
    $env:TOOLBOX_TEST_RUNTIME_LOG = $previousLog
    $env:TOOLBOX_TEST_FIXTURE = $previousFixture
    $resolved = [System.IO.Path]::GetFullPath($testRoot)
    $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolved.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolved).StartsWith("toolbox-docker-pull-")) {
        Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
    }
}
