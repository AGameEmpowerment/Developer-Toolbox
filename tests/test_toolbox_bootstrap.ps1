#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "developer-toolbox-bootstrap-$([guid]::NewGuid().ToString('N'))"

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,

        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
}

function New-TestRepository {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter()]
        [string]$OriginUrl
    )

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    & git -C $Path init --quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to initialize test repository '$Path'."
    }

    if ($OriginUrl) {
        & git -C $Path remote add origin $OriginUrl
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to add origin to test repository '$Path'."
        }
    }
}

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

    $mockBin = Join-Path $testRoot "mock-bin"
    New-Item -ItemType Directory -Path $mockBin -Force | Out-Null
    if ($env:OS -eq "Windows_NT") {
        Set-Content -LiteralPath (Join-Path $mockBin "docker.cmd") -Value '@exit /b 1'
        Set-Content -LiteralPath (Join-Path $mockBin "podman.cmd") -Value @'
@echo off
if "%1"=="info" exit /b 0
exit /b 1
'@
        Set-Content -LiteralPath (Join-Path $mockBin "devcontainer.cmd") -Value '@echo %*'
    } else {
        Set-Content -LiteralPath (Join-Path $mockBin "docker") -Value @'
#!/usr/bin/env bash
exit 1
'@
        Set-Content -LiteralPath (Join-Path $mockBin "podman") -Value @'
#!/usr/bin/env bash
[[ "$1" == "info" ]]
'@
        Set-Content -LiteralPath (Join-Path $mockBin "devcontainer") -Value @'
#!/usr/bin/env bash
printf '%s\n' "$*"
'@
        foreach ($commandName in @("docker", "podman", "devcontainer")) {
            & chmod u+x -- (Join-Path $mockBin $commandName)
            if ($LASTEXITCODE -ne 0) {
                throw "Unable to make mock command '$commandName' executable."
            }
        }
    }

    $savedPath = $env:PATH
    try {
        $env:PATH = "$mockBin$([System.IO.Path]::PathSeparator)$savedPath"
        $hostLauncherOutput = & (Join-Path $repoRoot ".devcontainer/start_devcontainer.ps1")
        Assert-Condition -Condition (($hostLauncherOutput | Out-String) -match '--docker-path.+podman') `
            -Message "PowerShell host launcher did not fall back from unreachable Docker to Podman."
    } finally {
        $env:PATH = $savedPath
    }

    $consumerPath = Join-Path $testRoot "Consumer Project"
    New-TestRepository -Path $consumerPath
    & (Join-Path $repoRoot "Install-ToolboxSetup.ps1") -TargetRepositoryPath $consumerPath | Out-Null

    $expectedFiles = @(
        "setup_toolbox.ps1",
        "setup_toolbox.sh",
        "setup/_toolbox.ps1",
        "setup/_toolbox.sh",
        "setup/toolbox-bootstrap-files.txt",
        ".devcontainer/devcontainer.json",
        ".devcontainer/post_devcontainer.ps1",
        ".devcontainer/start_devcontainer.ps1",
        ".devcontainer/start_devcontainer.sh",
        ".devcontainer/start_developer_toolkit.ps1",
        ".devcontainer/start_developer_toolkit.sh",
        ".agents/skills/toolbox-repository-bridge/SKILL.md",
        ".claude/skills/toolbox-repository-bridge/SKILL.md"
    )
    foreach ($relativePath in $expectedFiles) {
        $installedPath = Join-Path $consumerPath $relativePath
        Assert-Condition -Condition (Test-Path -LiteralPath $installedPath -PathType Leaf) `
            -Message "Installer did not create '$relativePath'."
    }
    foreach ($relativePath in @("start_developer_toolkit.ps1", "start_developer_toolkit.sh")) {
        Assert-Condition -Condition (-not (Test-Path -LiteralPath (Join-Path $consumerPath $relativePath))) `
            -Message "Installer created redundant root launcher '$relativePath'."
    }
    if ($env:OS -ne "Windows_NT") {
        foreach ($relativePath in @(
                "setup_toolbox.sh",
                "setup/_toolbox.sh",
                ".devcontainer/start_devcontainer.sh",
                ".devcontainer/start_developer_toolkit.sh"
            )) {
            & /usr/bin/test -x (Join-Path $consumerPath $relativePath)
            Assert-Condition -Condition ($LASTEXITCODE -eq 0) `
                -Message "PowerShell installer did not make '$relativePath' executable."
        }
    }

    $devcontainerContent = Get-Content -Raw -LiteralPath (Join-Path $consumerPath ".devcontainer/devcontainer.json")
    Assert-Condition -Condition ($devcontainerContent -match '(?m)^\s*"name"\s*:\s*"Consumer Project",') `
        -Message "Installed devcontainer name does not match the consumer repository directory."
    Assert-Condition -Condition ($devcontainerContent.Contains('${containerWorkspaceFolder}/.devcontainer/post_devcontainer.ps1')) `
        -Message "Installed devcontainer post-create command is not workspace-relative."
    Assert-Condition -Condition ($devcontainerContent -match '(?m)^\s*"DEVCONTAINER"\s*:\s*"true",') `
        -Message "Installed devcontainer does not set its explicit environment marker."
    $postCreateContent = Get-Content -Raw -LiteralPath (Join-Path $consumerPath ".devcontainer/post_devcontainer.ps1")
    Assert-Condition -Condition ($postCreateContent.Contains("./.devcontainer/start_developer_toolkit.ps1")) `
        -Message "Installed post-create guidance does not use the devcontainer developer toolkit command."

    $devContainerVariables = @("DEVCONTAINER", "REMOTE_CONTAINERS", "CODESPACES")
    $savedDevContainerVariables = @{}
    foreach ($variableName in $devContainerVariables) {
        $savedDevContainerVariables[$variableName] = [Environment]::GetEnvironmentVariable($variableName)
        [Environment]::SetEnvironmentVariable($variableName, $null)
    }
    try {
        $outsideDevContainerRejected = $false
        try {
            & (Join-Path $consumerPath ".devcontainer/start_developer_toolkit.ps1") | Out-Null
        } catch {
            $outsideDevContainerRejected = $_.Exception.Message -match "must be run from inside a devcontainer"
        }
        Assert-Condition -Condition $outsideDevContainerRejected `
            -Message "Devcontainer developer toolkit command ran outside a devcontainer."

        [Environment]::SetEnvironmentVariable("DEVCONTAINER", "true")
        & (Join-Path $consumerPath ".devcontainer/start_developer_toolkit.ps1") -WhatIf | Out-Null

        $collisionPath = Join-Path $testRoot "Consumer With Docker Setup"
        New-TestRepository -Path $collisionPath
        & (Join-Path $repoRoot "Install-ToolboxSetup.ps1") -TargetRepositoryPath $collisionPath | Out-Null
        Set-Content -LiteralPath (Join-Path $collisionPath "setup_toolbox.ps1") -Value @'
[CmdletBinding()]
param([string]$ContainerRuntime)
Write-Output "toolbox-stub:$ContainerRuntime"
'@
        Set-Content -LiteralPath (Join-Path $collisionPath "docker_setup.ps1") -Value @'
param([string]$ContainerRuntime)
throw "consumer docker setup must not be selected"
'@

        $whatIfOutput = & (Join-Path $collisionPath ".devcontainer/start_developer_toolkit.ps1") `
            -ContainerRuntime podman -WhatIf | Out-String
        Assert-Condition -Condition ($whatIfOutput -notmatch "toolbox-stub") `
            -Message "Devcontainer developer toolkit command ignored -WhatIf."

        $collisionOutput = & (Join-Path $collisionPath ".devcontainer/start_developer_toolkit.ps1") `
            -ContainerRuntime podman
        Assert-Condition -Condition (($collisionOutput | Out-String) -match "toolbox-stub:podman") `
            -Message "Consumer launcher did not prefer setup_toolbox.ps1 or rejected a successful PowerShell child."
    } finally {
        foreach ($variableName in $devContainerVariables) {
            [Environment]::SetEnvironmentVariable($variableName, $savedDevContainerVariables[$variableName])
        }
    }

    $preservePath = Join-Path $testRoot "Preserve Existing"
    New-TestRepository -Path $preservePath
    $existingDevcontainer = Join-Path $preservePath ".devcontainer"
    New-Item -ItemType Directory -Path $existingDevcontainer -Force | Out-Null
    $existingDevcontainerMarker = Join-Path $existingDevcontainer "consumer-owned.txt"
    Set-Content -LiteralPath $existingDevcontainerMarker -Value "preserve-me" -NoNewline
    $existingWrapper = Join-Path $preservePath "setup_toolbox.ps1"
    Set-Content -LiteralPath $existingWrapper -Value "preserve-me" -NoNewline
    & (Join-Path $repoRoot "Install-ToolboxSetup.ps1") -TargetRepositoryPath $preservePath | Out-Null
    Assert-Condition -Condition ((Get-Content -Raw -LiteralPath $existingWrapper) -eq "preserve-me") `
        -Message "Installer replaced an existing target file."
    Assert-Condition -Condition (Test-Path -LiteralPath $existingDevcontainerMarker -PathType Leaf) `
        -Message "Installer replaced an existing target directory."
    Assert-Condition -Condition (-not (Test-Path -LiteralPath (Join-Path $existingDevcontainer "devcontainer.json"))) `
        -Message "Installer added files to an existing target directory."

    $danglingLinkPath = Join-Path $testRoot "Preserve Dangling Link"
    New-TestRepository -Path $danglingLinkPath
    $danglingTarget = Join-Path $testRoot "Removed Link Target"
    New-Item -ItemType Directory -Path $danglingTarget -Force | Out-Null
    $danglingDevcontainer = Join-Path $danglingLinkPath ".devcontainer"
    $linkType = if ($env:OS -eq "Windows_NT") { "Junction" } else { "SymbolicLink" }
    New-Item -ItemType $linkType -Path $danglingDevcontainer -Target $danglingTarget | Out-Null
    Remove-Item -LiteralPath $danglingTarget -Recurse -Force
    & (Join-Path $repoRoot "Install-ToolboxSetup.ps1") -TargetRepositoryPath $danglingLinkPath | Out-Null
    $preservedLink = Get-ChildItem -LiteralPath $danglingLinkPath -Force |
        Where-Object { $_.Name -eq ".devcontainer" } |
        Select-Object -First 1
    Assert-Condition -Condition ($null -ne $preservedLink) `
        -Message "Installer replaced or removed a dangling consumer-owned link."
    Assert-Condition -Condition (-not (Test-Path -LiteralPath $danglingTarget)) `
        -Message "Installer followed a dangling consumer-owned link and recreated its target."

    $wrongToolboxPath = Join-Path $testRoot "Wrong Toolbox"
    New-TestRepository -Path $wrongToolboxPath -OriginUrl "https://github.com/example/not-the-toolbox.git"
    $wrongOriginRejected = $false
    try {
        & (Join-Path $repoRoot "setup/_toolbox.ps1") `
            -ToolboxPath $wrongToolboxPath -NoUpdate -SkipSetup | Out-Null
    } catch {
        $wrongOriginRejected = $_.Exception.Message -match "unexpected origin"
    }
    Assert-Condition -Condition $wrongOriginRejected `
        -Message "Launcher accepted a Git repository with an unexpected origin."

    $validToolboxPath = Join-Path $testRoot "Valid Toolbox"
    New-TestRepository -Path $validToolboxPath `
        -OriginUrl "https://github.com/AGameEmpowerment/Developer-Toolbox.git"
    $launcherOutput = & (Join-Path $consumerPath "setup_toolbox.ps1") `
        -ToolboxPath $validToolboxPath -NoUpdate -SkipSetup
    Assert-Condition -Condition (($launcherOutput | Out-String) -match "Service startup was skipped") `
        -Message "Installed root wrapper did not forward arguments to the implementation launcher."

    $lowercaseToolboxPath = Join-Path $testRoot "Lowercase Toolbox"
    New-TestRepository -Path $lowercaseToolboxPath `
        -OriginUrl "https://github.com/agameempowerment/developer-toolbox.git"
    $lowercaseLauncherOutput = & (Join-Path $repoRoot "setup/_toolbox.ps1") `
        -ToolboxPath $lowercaseToolboxPath -NoUpdate -SkipSetup
    Assert-Condition -Condition (($lowercaseLauncherOutput | Out-String) -match "Service startup was skipped") `
        -Message "Launcher rejected a lowercase Toolbox origin URL."

    Write-Output "PowerShell Toolbox bootstrap tests passed."
} finally {
    $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
    if ($resolvedTestRoot.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTestRoot).StartsWith("developer-toolbox-bootstrap-")) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
