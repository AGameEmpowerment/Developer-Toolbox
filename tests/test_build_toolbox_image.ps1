#Requires -Version 7.3
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$builder = Join-Path (Split-Path -Parent $PSScriptRoot) 'Build-ToolboxImage.ps1'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "toolbox-image-build-$([guid]::NewGuid().ToString('N'))"
$previousRuntime = $env:CONTAINER_RUNTIME
$script:cases = 0
$global:toolboxImageBuildcalls = [System.Collections.Generic.List[object]]::new()
$global:toolboxImageBuildunavailable = @()
$global:toolboxImageBuildbuildExitCode = 0
$global:toolboxImageBuildinspectExitCode = 0
$global:toolboxImageBuildnativeProbe = $null

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function Invoke-FakeRuntime {
    param([string]$Runtime, [object[]]$Arguments)
    $global:toolboxImageBuildcalls.Add([pscustomobject]@{ Runtime = $Runtime; Arguments = [string[]]$Arguments })
    $global:LASTEXITCODE = 0
    switch ($Arguments[0]) {
        'info' { if ($Runtime -in $global:toolboxImageBuildunavailable) { $global:LASTEXITCODE = 9 } }
        'build' {
            $global:LASTEXITCODE = $global:toolboxImageBuildbuildExitCode
            if ($global:toolboxImageBuildnativeProbe) {
                $probe = $global:toolboxImageBuildnativeProbe
                & $probe.Executable -NoProfile -File $probe.Script $probe.Output @Arguments
                $global:LASTEXITCODE = $LASTEXITCODE
            }
        }
        'image' {
            Assert-Condition ($Arguments[1] -eq 'inspect') 'Only image inspection is permitted after building.'
            $global:LASTEXITCODE = $global:toolboxImageBuildinspectExitCode
            if ($global:toolboxImageBuildinspectExitCode -eq 0) { 'sha256:fixture-local-image' }
        }
        default { throw "Unexpected runtime operation: $($Arguments[0])" }
    }
}

function Test-Case {
    param([string]$Name, [scriptblock]$Action)
    $global:toolboxImageBuildcalls.Clear()
    $global:toolboxImageBuildunavailable = @()
    $global:toolboxImageBuildbuildExitCode = 0
    $global:toolboxImageBuildinspectExitCode = 0
    $global:toolboxImageBuildnativeProbe = $null
    & $Action
    $script:cases++
    Write-Output "PASS: $Name"
}

function Assert-Fails {
    param([hashtable]$Parameters, [string]$MessagePattern)
    $failed = $false
    try { & $builder @Parameters | Out-Null } catch {
        $failed = $true
        Assert-Condition ($_.Exception.Message -like $MessagePattern) "Unexpected failure: $($_.Exception.Message)"
    }
    Assert-Condition $failed 'Expected the build to fail.'
}

try {
    New-Item -ItemType Directory -Path (Join-Path $testRoot 'context with spaces') -Force | Out-Null
    $context = Join-Path $testRoot 'context with spaces'
    Set-Content -LiteralPath (Join-Path $context 'Dockerfile') -Value 'FROM scratch'
    Set-Content -LiteralPath (Join-Path $context 'custom.Dockerfile') -Value 'FROM scratch'
    $nativeProbeScript = Join-Path $testRoot 'capture-native-arguments.ps1'
    Set-Content -LiteralPath $nativeProbeScript -Value @'
[System.IO.File]::WriteAllLines($args[0], [string[]]$args[1..($args.Length - 1)])
'@
    function global:docker { Invoke-FakeRuntime -Runtime docker -Arguments $args }
    function global:podman { Invoke-FakeRuntime -Runtime podman -Arguments $args }
    $parameters = @{ ContextPath = $context; ImageName = 'localhost/fixture'; ContainerRuntime = 'docker' }

    foreach ($runtime in @('docker', 'podman')) {
        Test-Case "$runtime builds and verifies a local tag without pulling or pushing" {
            $parameters.ContainerRuntime = $runtime
            $result = & $builder @parameters -PassThru
            Assert-Condition ($result.ImageId -eq 'sha256:fixture-local-image') 'Missing verified image ID.'
            Assert-Condition ($result.ContainerRuntime -eq $runtime) 'Wrong runtime returned.'
            Assert-Condition ($result.Tag -eq 'localhost/fixture:latest') 'Build did not use latest.'
            $build = @($global:toolboxImageBuildcalls | Where-Object { $_.Arguments[0] -eq 'build' })
            Assert-Condition ($build.Count -eq 1) 'Expected exactly one build.'
            Assert-Condition ($build[0].Arguments[-1] -eq $context) 'Build context was split or changed.'
            Assert-Condition (($build[0].Arguments -contains '--load') -eq ($runtime -eq 'docker')) 'Docker output must load locally.'
            Assert-Condition (-not ($build[0].Arguments -match '^--pull|^--push|^--output')) 'Unexpected registry/export option.'
            Assert-Condition ($global:toolboxImageBuildcalls.Count -eq 3) 'Expected only info, build and inspect.'
        }
        Test-Case "$runtime preserves literal arguments and optional build settings" {
            $parameters.ContainerRuntime = $runtime
            $literal = 'value with spaces;"quoted";$(must-not-execute)#literal'
            & $builder @parameters -Dockerfile custom.Dockerfile -BuildArgument @{ OIDC_BASE_IMAGE = 'localhost/base:latest'; MESSAGE = $literal; EMPTY = '' } -Platform linux/amd64 -Target final -NoCache
            $arguments = @($global:toolboxImageBuildcalls | Where-Object { $_.Arguments[0] -eq 'build' })[0].Arguments
            Assert-Condition ($arguments -ccontains "MESSAGE=$literal") 'Literal build argument was changed.'
            Assert-Condition ($arguments -ccontains 'EMPTY=') 'Empty build argument was dropped.'
            Assert-Condition ($arguments -contains (Join-Path $context 'custom.Dockerfile')) 'Dockerfile was not relative to context.'
            foreach ($expected in @('--platform', 'linux/amd64', '--target', 'final', '--no-cache')) {
                Assert-Condition ($arguments -contains $expected) "Missing option: $expected"
            }
            Assert-Condition (-not ($arguments -match '^--pull')) 'Build must reuse its upstream.'
        }
    }
    foreach ($passingMode in @('Legacy', 'Standard')) {
        Test-Case "native arguments retain quotes when the caller uses $passingMode mode" {
            $previousPassingMode = $PSNativeCommandArgumentPassing
            try {
                $PSNativeCommandArgumentPassing = $passingMode
                $nativeOutput = Join-Path $testRoot 'native-arguments.txt'
                $global:toolboxImageBuildnativeProbe = @{
                    Executable = (Get-Process -Id $PID).Path
                    Script = $nativeProbeScript
                    Output = $nativeOutput
                }
                $literal = '{"scope":"open id","value":";$(must-not-execute)#literal"}'
                & $builder -ContextPath $context -ImageName localhost/fixture -ContainerRuntime docker -BuildArgument @{ CONFIG = $literal }
                $received = Get-Content -LiteralPath $nativeOutput
                Assert-Condition ($received -ccontains "CONFIG=$literal") 'The native process received a changed build argument.'
                Assert-Condition ($PSNativeCommandArgumentPassing -eq $passingMode) 'The builder changed the caller preference.'
            } finally {
                $PSNativeCommandArgumentPassing = $previousPassingMode
            }
        }
    }
    foreach ($runtime in @('docker', 'podman')) {
        Test-Case "$runtime OIDC profile resolves this checkout outside the repository and reuses upstream" {
            $sourceRevision = (& git -C (Split-Path -Parent $PSScriptRoot) rev-parse HEAD).Trim()
            Push-Location -LiteralPath $testRoot
            try {
                $result = & $builder -Profile oidc -ContainerRuntime $runtime -PassThru
            } finally { Pop-Location }
            $expectedContext = Join-Path (Split-Path -Parent $PSScriptRoot) 'containers/oidc/keycloak'
            $build = @($global:toolboxImageBuildcalls | Where-Object { $_.Arguments[0] -eq 'build' })[0].Arguments
            Assert-Condition ($result.ContextPath -eq $expectedContext -and $build[-1] -eq $expectedContext) 'Profile context depends on the caller directory.'
            Assert-Condition ($result.Tag -ceq 'localhost/toolbox-oidc-keycloak:latest') 'Profile did not select the reusable canonical image.'
            Assert-Condition ($result.Dockerfile -eq (Join-Path $expectedContext 'Dockerfile')) 'Profile did not select its own Dockerfile.'
            Assert-Condition ($build -ccontains "SOURCE_REVISION=$sourceRevision") 'Profile omitted checkout provenance.'
            Assert-Condition (-not ($build -match '^--pull')) 'Build must reuse its upstream.'
            Assert-Condition (($build -contains '--load') -eq ($runtime -eq 'docker')) 'Profile changed local output behavior.'
            Assert-Condition ($global:toolboxImageBuildcalls.Count -eq 4) 'Profile performed operations beyond info, build and image inspection.'
        }
        Test-Case "$runtime OIDC profile honors explicit cached builds and caller build arguments" {
            $overrides = @{ SOURCE_REVISION = 'caller-revision'; UPSTREAM_IMAGE = 'localhost/qualified-upstream:latest'; EMPTY = '' }
            & $builder -Profile oidc -ContainerRuntime $runtime -BuildArgument $overrides
            $build = @($global:toolboxImageBuildcalls | Where-Object { $_.Arguments[0] -eq 'build' })[0].Arguments
            Assert-Condition (-not ($build -match '^--pull')) 'Cached upstream was not reused.'
            Assert-Condition ($build -ccontains 'SOURCE_REVISION=caller-revision' -and $build -ccontains 'UPSTREAM_IMAGE=localhost/qualified-upstream:latest' -and $build -ccontains 'EMPTY=') 'Profile changed explicit build arguments.'
            Assert-Condition ($overrides.Count -eq 3 -and $overrides.SOURCE_REVISION -ceq 'caller-revision') 'Profile mutated caller arguments.'
        }
    }
    Test-Case 'running without arguments builds the OIDC profile' {
        $env:CONTAINER_RUNTIME = $null
        & $builder
        $build = @($global:toolboxImageBuildcalls | Where-Object { $_.Arguments[0] -eq 'build' })
        Assert-Condition ($build.Count -eq 1) 'Expected exactly one default build.'
        Assert-Condition ($build[0].Runtime -eq 'docker') 'Default build did not auto-select Docker.'
        Assert-Condition ($build[0].Arguments -ccontains 'localhost/toolbox-oidc-keycloak:latest') 'Default build did not select the OIDC profile.'
        Assert-Condition (-not ($build[0].Arguments -match '^--pull')) 'Default build must reuse its upstream.'
    }
    Test-Case 'OIDC profile provenance does not mutate the caller argument table' {
        $arguments = @{ UPSTREAM_IMAGE = 'localhost/qualified-upstream:latest' }
        & $builder -Profile oidc -ContainerRuntime docker -BuildArgument $arguments -WhatIf
        Assert-Condition ($arguments.Count -eq 1 -and -not $arguments.ContainsKey('SOURCE_REVISION')) 'Profile inserted provenance into caller state.'
        Assert-Condition ($global:toolboxImageBuildcalls.Count -eq 0) 'Profile WhatIf contacted an engine.'
    }
    foreach ($customParameter in @('ContextPath', 'ImageName', 'Dockerfile')) {
        Test-Case "OIDC profile cannot be combined with custom $customParameter" {
            $invalid = @{ Profile = 'oidc'; ContainerRuntime = 'docker' }
            $invalid[$customParameter] = switch ($customParameter) {
                ContextPath { $context }
                ImageName { 'localhost/fixture' }
                Dockerfile { 'Dockerfile' }
            }
            Assert-Fails $invalid '*Parameter set cannot be resolved*'
            Assert-Condition ($global:toolboxImageBuildcalls.Count -eq 0) 'Invalid profile combination contacted an engine.'
        }
    }
    Test-Case 'unknown named profiles fail before contacting an engine' {
        Assert-Fails @{ Profile = 'unknown'; ContainerRuntime = 'docker' } '*does not belong to the set*'
        Assert-Condition ($global:toolboxImageBuildcalls.Count -eq 0) 'Unknown profile contacted an engine.'
    }
    $parameters.ContainerRuntime = 'auto'
    Test-Case 'auto prefers a reachable Docker engine' {
        $result = & $builder @parameters -PassThru
        Assert-Condition ($result.ContainerRuntime -eq 'docker') 'Auto did not prefer Docker.'
    }
    Test-Case 'auto falls back to reachable Podman' {
        $global:toolboxImageBuildunavailable = @('docker')
        $result = & $builder @parameters -PassThru
        Assert-Condition ($result.ContainerRuntime -eq 'podman') 'Auto did not fall back.'
    }
    Test-Case 'explicit engine failure does not switch image stores' {
        $global:toolboxImageBuildunavailable = @('docker')
        $parameters.ContainerRuntime = 'docker'
        Assert-Fails $parameters '*No reachable*'
        Assert-Condition ($global:toolboxImageBuildcalls.Count -eq 1) 'Explicit engine selection fell back or built.'
    }
    Test-Case 'WhatIf validates input without contacting either engine' {
        & $builder @parameters -WhatIf
        Assert-Condition ($global:toolboxImageBuildcalls.Count -eq 0) 'WhatIf contacted an engine.'
    }
    Test-Case 'missing context fails before engine calls' {
        $invalid = $parameters.Clone(); $invalid.ContextPath = Join-Path $testRoot 'missing'
        Assert-Fails $invalid '*does not exist*'
        Assert-Condition ($global:toolboxImageBuildcalls.Count -eq 0) 'Invalid context contacted an engine.'
    }
    Test-Case 'missing Dockerfile fails before engine calls' {
        $invalid = $parameters.Clone(); $invalid.Dockerfile = 'missing.Dockerfile'
        Assert-Fails $invalid '*does not exist*'
        Assert-Condition ($global:toolboxImageBuildcalls.Count -eq 0) 'Invalid Dockerfile contacted an engine.'
    }
    Test-Case 'invalid build argument names are rejected' {
        $invalid = $parameters.Clone(); $invalid.BuildArgument = @{ '--push' = 'yes' }
        Assert-Fails $invalid '*valid variable names*'
        Assert-Condition ($global:toolboxImageBuildcalls.Count -eq 0) 'Invalid build arguments contacted an engine.'
    }
    Test-Case 'explicit tags are rejected in favor of latest' {
        $invalid = $parameters.Clone(); $invalid.ImageName = 'localhost/fixture:old'
        Assert-Fails $invalid '*always builds :latest*'
        Assert-Condition ($global:toolboxImageBuildcalls.Count -eq 0) 'Invalid tag contacted an engine.'
    }
    Test-Case 'registry ports are retained while latest is appended' {
        $result = & $builder -ContextPath $context -ImageName localhost:5000/fixture -ContainerRuntime docker -PassThru
        Assert-Condition ($result.Tag -eq 'localhost:5000/fixture:latest') 'Registry port was mistaken for a tag.'
    }
    Test-Case 'null build argument values are rejected' {
        $invalid = $parameters.Clone(); $invalid.BuildArgument = @{ BASE_IMAGE = $null }
        Assert-Fails $invalid '*non-null values*'
        Assert-Condition ($global:toolboxImageBuildcalls.Count -eq 0) 'Null argument contacted an engine.'
    }
    Test-Case 'build failures are surfaced and skip inspection' {
        $global:toolboxImageBuildbuildExitCode = 23
        Assert-Fails $parameters '*exit code 23*'
        Assert-Condition ($global:toolboxImageBuildcalls.Count -eq 2) 'Failed build was inspected or retried.'
    }
    Test-Case 'missing local build output is a failure' {
        $global:toolboxImageBuildinspectExitCode = 1
        Assert-Fails $parameters '*unavailable in the selected local image store*'
    }
    Test-Case 'runtime environment selection is honored' {
        $env:CONTAINER_RUNTIME = 'podman'
        $result = & $builder -ContextPath $context -ImageName localhost/fixture -PassThru
        Assert-Condition ($result.ContainerRuntime -eq 'podman') 'Environment selection was ignored.'
    }
    Write-Output "PowerShell local-image build tests passed: $script:cases cases."
} finally {
    Remove-Item Function:\docker -ErrorAction SilentlyContinue
    Remove-Item Function:\podman -ErrorAction SilentlyContinue
    $env:CONTAINER_RUNTIME = $previousRuntime
    $resolved = [System.IO.Path]::GetFullPath($testRoot)
    $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolved.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolved).StartsWith('toolbox-image-build-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
    }
}
