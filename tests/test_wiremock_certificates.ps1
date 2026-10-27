#Requires -Version 5.1
# Isolated certificate/setup tests; no engine, certificate store, or host JDK is used.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "toolbox-wiremock-$([guid]::NewGuid().ToString('N'))"
$previousPassword = [Environment]::GetEnvironmentVariable('TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD', 'Process')
$previousJavaHome = [Environment]::GetEnvironmentVariable('JAVA_HOME', 'Process')
$global:ToolboxWireMockCertificateTest = @{}
$global:ToolboxWireMockCertificateTest.hostKeyTool = $false
$global:ToolboxWireMockCertificateTest.failKeyTool = $false
$global:ToolboxWireMockCertificateTest.skipOutputs = $false
$global:ToolboxWireMockCertificateTest.calls = @()
$global:ToolboxWireMockCertificateTest.expectedPassword = 'fixture;# secret $ with spaces'

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function Get-Command {
    param([string]$Name, [string]$ErrorAction)
    if ($Name -eq 'keytool') {
        if ($global:ToolboxWireMockCertificateTest.hostKeyTool) { return [pscustomobject]@{ Source = 'keytool' } }
        return
    }
    if ($Name -in @('docker', 'podman')) { return [pscustomobject]@{ Source = $Name } }
    Microsoft.PowerShell.Core\Get-Command -Name $Name -ErrorAction SilentlyContinue
}

function Get-ChildItem {
    param([string]$Path, [string]$ErrorAction)
    # Suppress installed Windows JDK discovery; fixtures never require host Java.
    if ($Path -like '*\bin\keytool.exe') { return }
    Microsoft.PowerShell.Management\Get-ChildItem -Path $Path -ErrorAction SilentlyContinue
}

function Invoke-FakeKeyTool {
    param([string]$Runtime, [object[]]$Arguments)
    if ($Arguments[0] -eq 'image' -and $Arguments[1] -eq 'inspect') {
        $global:ToolboxWireMockCertificateTest.cacheInspections += [string]$Arguments[-1]
        $global:LASTEXITCODE = 0
        return
    }
    $global:ToolboxWireMockCertificateTest.calls += [pscustomobject]@{ Runtime = $Runtime; Arguments = @($Arguments) }
    Assert-Condition ($env:TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD -ceq $global:ToolboxWireMockCertificateTest.expectedPassword) 'Keytool did not receive the configured password through its environment.'
    Assert-Condition (-not (@($Arguments) -contains $global:ToolboxWireMockCertificateTest.expectedPassword)) 'Password appeared in native process arguments.'
    Assert-Condition (@($Arguments) -contains '-storepass:env') 'Keytool did not use environment-based password input.'
    if ($global:ToolboxWireMockCertificateTest.failKeyTool) { $global:LASTEXITCODE = 7; return }
    $global:LASTEXITCODE = 0
    if ($global:ToolboxWireMockCertificateTest.skipOutputs) { return }
    $keystoreIndex = [array]::IndexOf($Arguments, '-keystore')
    $fileIndex = [array]::IndexOf($Arguments, '-file')
    $destination = if ($fileIndex -ge 0) { [string]$Arguments[$fileIndex + 1] } else { [string]$Arguments[$keystoreIndex + 1] }
    if ($Runtime -ne 'host') {
        $mountIndex = [array]::IndexOf($Arguments, '--volume')
        $mount = [string]$Arguments[$mountIndex + 1]
        Assert-Condition ($mount -match '^(.*):/toolbox/certs:rw(?:,z)?$') 'Certificate bind mount was not one intact argument.'
        $destination = Join-Path $matches[1] (Split-Path -Leaf $destination)
    }
    [System.IO.File]::WriteAllText($destination, 'fixture output')
}

function docker { Invoke-FakeKeyTool -Runtime 'docker' -Arguments $args }
function podman { Invoke-FakeKeyTool -Runtime 'podman' -Arguments $args }
function keytool { Invoke-FakeKeyTool -Runtime 'host' -Arguments $args }

function New-CertificateFixture {
    param([string]$Name)
    $repository = Join-Path $testRoot $Name
    $fixture = Join-Path $repository 'containers/certs'
    New-Item -ItemType Directory -Path $fixture -Force | Out-Null
    $helpers = Join-Path $repository '.devcontainer'
    New-Item -ItemType Directory -Path $helpers -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot '.devcontainer/ensure_image.ps1') -Destination $helpers
    Copy-Item -LiteralPath (Join-Path $repoRoot 'containers/certs/Generate-WireMockCert.ps1') -Destination $fixture
    return $fixture
}

function Invoke-CertificateFixture {
    param([string]$Fixture, [string]$Runtime, [bool]$ExpectSuccess = $true)
    $global:ToolboxWireMockCertificateTest.calls = @()
    $global:ToolboxWireMockCertificateTest.cacheInspections = @()
    $outputFile = Join-Path $Fixture 'output.log'
    $succeeded = $false
    try {
        & (Join-Path $Fixture 'Generate-WireMockCert.ps1') -ContainerRuntime $Runtime -KeystorePassword $global:ToolboxWireMockCertificateTest.expectedPassword -Force *> $outputFile
        $succeeded = $true
    } catch { $_.Exception.Message | Add-Content -LiteralPath $outputFile }
    $details = [System.IO.File]::ReadAllText($outputFile).Replace($global:ToolboxWireMockCertificateTest.expectedPassword, '<redacted>')
    Assert-Condition ($succeeded -eq $ExpectSuccess) "Certificate generation had an unexpected outcome: $details"
    Assert-Condition (-not ([System.IO.File]::ReadAllText($outputFile).Contains($global:ToolboxWireMockCertificateTest.expectedPassword))) 'Certificate generation printed a password.'
    Assert-Condition ($env:TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD -ceq 'previous-secret') 'Generator did not restore the caller environment.'
}

try {
    $env:JAVA_HOME = ''
    $env:TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD = 'previous-secret'
    foreach ($runtime in @('docker', 'podman')) {
        $fixture = New-CertificateFixture "$runtime path with spaces"
        Invoke-CertificateFixture -Fixture $fixture -Runtime $runtime
        Assert-Condition ($global:ToolboxWireMockCertificateTest.calls.Count -eq 2) 'Expected exactly two ephemeral keytool invocations.'
        Assert-Condition ($global:ToolboxWireMockCertificateTest.cacheInspections.Count -eq 1 -and $global:ToolboxWireMockCertificateTest.cacheInspections[0] -eq 'wiremock/wiremock:latest') 'Generator did not inspect its cached helper image.'
        foreach ($call in $global:ToolboxWireMockCertificateTest.calls) {
            Assert-Condition ($call.Runtime -eq $runtime) 'Generator switched away from the selected engine.'
            Assert-Condition ($call.Arguments -contains '--rm') 'Generator left a persistent helper container.'
            Assert-Condition ($call.Arguments -contains 'wiremock/wiremock:latest') 'Generator used an unrelated image.'
            Assert-Condition ($call.Arguments -contains 'none') 'Generator did not disable container networking.'
            if ($runtime -eq 'podman') {
                Assert-Condition ($call.Arguments -contains 'keep-id') 'Podman did not preserve writable bind ownership.'
                Assert-Condition (([string]($call.Arguments | Where-Object { $_ -like '*:/toolbox/certs:*' })).EndsWith(':rw,z')) 'Podman did not use a shared SELinux mount label.'
            }
        }
        Assert-Condition (Test-Path -LiteralPath (Join-Path $fixture 'wiremock.jks')) 'Keystore was not generated.'
        Assert-Condition (Test-Path -LiteralPath (Join-Path $fixture 'wiremock.crt')) 'Public certificate was not exported.'
    }

    $global:ToolboxWireMockCertificateTest.hostKeyTool = $true
    Invoke-CertificateFixture -Fixture (New-CertificateFixture 'host preferred') -Runtime 'podman'
    Assert-Condition (@($global:ToolboxWireMockCertificateTest.calls | Where-Object { $_.Runtime -ne 'host' }).Count -eq 0) 'Available host keytool was not preferred.'
    $global:ToolboxWireMockCertificateTest.hostKeyTool = $false

    $global:ToolboxWireMockCertificateTest.failKeyTool = $true
    Invoke-CertificateFixture -Fixture (New-CertificateFixture 'engine failure') -Runtime 'podman' -ExpectSuccess $false
    Assert-Condition ($global:ToolboxWireMockCertificateTest.calls.Count -eq 1) 'Generator continued exporting after keytool failed.'
    $global:ToolboxWireMockCertificateTest.failKeyTool = $false

    $global:ToolboxWireMockCertificateTest.skipOutputs = $true
    Invoke-CertificateFixture -Fixture (New-CertificateFixture 'missing output') -Runtime 'docker' -ExpectSuccess $false
    $global:ToolboxWireMockCertificateTest.skipOutputs = $false

    # Setup should delegate to the selected engine while retaining a raw local assignment.
    $fixture = Join-Path $testRoot 'setup custom password'
    $containers = Join-Path $fixture 'containers'
    $certs = Join-Path $containers 'certs'
    New-Item -ItemType Directory -Path $certs -Force | Out-Null
    $helpers = Join-Path $fixture '.devcontainer'
    New-Item -ItemType Directory -Path $helpers -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docker_setup.ps1') -Destination $fixture
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docker_pull.ps1') -Destination $fixture
    Copy-Item -LiteralPath (Join-Path $repoRoot '.devcontainer/ensure_image.ps1') -Destination $helpers
    foreach ($group in @('common', 'azure', 'aws')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "containers/docker-compose-$group.yml") -Destination $containers
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'containers/environment.ps1') -Destination $containers
    Copy-Item -LiteralPath (Join-Path $repoRoot 'containers/.env.example') -Destination $containers
    Copy-Item -LiteralPath (Join-Path $repoRoot 'containers/.env.example') -Destination (Join-Path $containers '.env')
    Add-Content -LiteralPath (Join-Path $containers '.env') -Value "TOOLBOX_CERT_PASSWORD='$($global:ToolboxWireMockCertificateTest.expectedPassword)'"
    $assignment = 'export WIREMOCK_KEYSTORE_PASSWORD = "${TOOLBOX_CERT_PASSWORD}"'
    Add-Content -LiteralPath (Join-Path $containers '.env') -Value $assignment -Encoding UTF8
    @'
param([string]$ContainerRuntime, [switch]$Force)
if ($ContainerRuntime -ne 'podman') { throw 'Setup changed its selected engine.' }
if ($global:ToolboxWireMockCertificateTest.generatedMode) {
    if ($env:TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD -cnotmatch '^Tb!1[0-9a-f]{64}$') { throw 'Setup did not pass the generated password.' }
} elseif ($env:TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD -cne 'fixture;# secret $ with spaces') { throw 'Setup changed its configured password.' }
$global:ToolboxWireMockCertificateTest.generationCount++
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'wiremock.jks') -Value 'fixture'
'@ | Set-Content -LiteralPath (Join-Path $certs 'Generate-WireMockCert.ps1') -Encoding UTF8
    function podman {
        if ($args[0] -eq 'image' -and $args[1] -eq 'inspect') {
            if ($args -contains '--format') { '17.0.5035.5' }
            $global:LASTEXITCODE = 0
            return
        }
        if ($args -contains 'run' -and $args[-1] -eq 'password') {
            if ($global:ToolboxWireMockCertificateTest.utilityFails) {
                $failedComposePath = [string]$args[([Array]::IndexOf($args, '-f') + 1)]
                [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $failedComposePath) 'value'), $global:ToolboxWireMockCertificateTest.expectedPassword)
                $global:ToolboxWireMockCertificateTest.expectedPassword
                $global:LASTEXITCODE = 7
                return
            }
            Assert-Condition ($args -contains '--rm' -and $args -contains '-T') 'Password utility must be ephemeral and noninteractive.'
            $composePath = [string]$args[([Array]::IndexOf($args, '-f') + 1)]
            $dotenvPath = [string]$args[([Array]::IndexOf($args, '--env-file') + 1)]
            $utility = Get-Content -LiteralPath $composePath -Raw | ConvertFrom-Json
            Assert-Condition ($utility.services.password.network_mode -eq 'none') 'Password utility enabled networking.'
            Assert-Condition ($utility.services.password.userns_mode -eq 'keep-id') 'Password utility changed Podman ownership.'
            Assert-Condition ($utility.services.password.environment.TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD -ceq '${WIREMOCK_KEYSTORE_PASSWORD:-changeit}') 'Setup resolved the password independently from Compose.'
            Assert-Condition (-not $utility.services.password.PSObject.Properties['ports']) 'Password utility published ports.'
            $value = $global:ToolboxWireMockCertificateTest.expectedPassword
            if ($global:ToolboxWireMockCertificateTest.generatedMode) {
                $assignmentLine = Get-Content -LiteralPath $dotenvPath | Where-Object { $_ -cmatch '^WIREMOCK_KEYSTORE_PASSWORD="(Tb!1[a-f0-9]{64})"$' } | Select-Object -Last 1
                if ($assignmentLine -cmatch '^WIREMOCK_KEYSTORE_PASSWORD="(Tb!1[a-f0-9]{64})"$') { $value = $matches[1] }
            }
            [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $composePath) 'value'), $value)
        }
        if ($args[0] -eq 'version') { '6.0.1' }
        if ($args -contains '--services') { 'wiremock' }
        if ($args[0] -eq 'ps') {
            Assert-Condition ($args -contains '--filter') 'Migration did not filter project labels.'
            Assert-Condition (([string]$args[3]) -in @('label=com.docker.compose.project=dev_common_shared', 'label=io.podman.compose.project=dev_common_shared')) 'Migration queried unrelated projects.'
            if ($global:ToolboxWireMockCertificateTest.cleanupMode -eq 'invalid') { '--all' }
            else { 'a' * 64; 'b' * 64; 'c' * 64; 'd' * 64; 'e' * 64; 'f' * 64; '1' * 64 }
        }
        if ($args[0] -eq 'inspect') {
            Assert-Condition ($args[2] -ceq '{{json .Config.Labels}}') 'Migration inspected more than container labels.'
            $global:ToolboxWireMockCertificateTest.inspected += [string]$args[3]
            if ($global:ToolboxWireMockCertificateTest.cleanupMode -eq 'inspect-failure') { $global:LASTEXITCODE = 1; return }
            switch ([string]$args[3]) {
                ('a' * 64) { '{"io.podman.compose.project":"dev_common_shared","io.podman.compose.service":"function-app"}' }
                ('b' * 64) { '{"com.docker.compose.project":"dev_common_shared","com.docker.compose.service":"postgres"}' }
                ('c' * 64) { '{"com.docker.compose.project":"foreign-project","com.docker.compose.service":"lambda-runtime"}' }
                ('d' * 64) { '{"io.podman.compose.project":"dev_common_shared","io.podman.compose.service":"custom-development-service"}' }
                ('e' * 64) { '{"io.podman.compose.project":"dev_common_shared","io.podman.compose.service":"Function-App"}' }
                ('f' * 64) { '{"io.podman.compose.project":"dev_common_shared","io.podman.compose.service":"mssql"}' }
                ('1' * 64) { '{"com.docker.compose.project":"dev_common_shared","com.docker.compose.service":"redis"}' }
            }
        }
        if ($args[0] -eq 'rm') {
            Assert-Condition ($args.Count -eq 3 -and $args[1] -eq '-f') 'Migration requested volume deletion or unrelated cleanup.'
            $global:ToolboxWireMockCertificateTest.removed += [string]$args[2]
        }
        if ($args -contains 'up') { $global:ToolboxWireMockCertificateTest.startCount++ }
        $global:LASTEXITCODE = 0
    }
    $global:ToolboxWireMockCertificateTest.cleanupMode = 'normal'
    $global:ToolboxWireMockCertificateTest.removed = @()
    $global:ToolboxWireMockCertificateTest.inspected = @()
    $global:ToolboxWireMockCertificateTest.startCount = 0
    $global:ToolboxWireMockCertificateTest.generatedMode = $false
    $global:ToolboxWireMockCertificateTest.generationCount = 0
    $global:ToolboxWireMockCertificateTest.utilityFails = $true
    $utilityFailed = $false
    try { & (Join-Path $fixture 'docker_setup.ps1') -ContainerRuntime 'podman' *> (Join-Path $fixture 'setup.log') }
    catch { $utilityFailed = $true }
    Assert-Condition $utilityFailed 'Setup accepted a failed password utility.'
    Assert-Condition ($global:ToolboxWireMockCertificateTest.generationCount -eq 0) 'Setup generated a certificate after its password utility failed.'
    Assert-Condition (-not ([IO.File]::ReadAllText((Join-Path $fixture 'setup.log')).Contains($global:ToolboxWireMockCertificateTest.expectedPassword))) 'Password utility failure exposed a secret.'
    $global:ToolboxWireMockCertificateTest.utilityFails = $false
    & (Join-Path $fixture 'docker_setup.ps1') -ContainerRuntime 'podman' *> (Join-Path $fixture 'setup.log')
    Assert-Condition ([System.IO.File]::ReadAllText((Join-Path $containers '.env')).Contains($assignment)) 'Setup rewrote the custom dotenv password.'
    Assert-Condition (-not ([System.IO.File]::ReadAllText((Join-Path $fixture 'setup.log')).Contains($global:ToolboxWireMockCertificateTest.expectedPassword))) 'Setup printed the custom password.'
    Assert-Condition ($env:TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD -ceq 'previous-secret') 'Setup did not restore its caller environment.'
    Assert-Condition ($global:ToolboxWireMockCertificateTest.removed.Count -eq 3 -and $global:ToolboxWireMockCertificateTest.removed[0] -ceq ('a' * 64) -and $global:ToolboxWireMockCertificateTest.removed[1] -ceq ('b' * 64) -and $global:ToolboxWireMockCertificateTest.removed[2] -ceq ('f' * 64)) 'Migration must remove retired samples and relocated cloud containers while retaining unrelated containers.'
    Assert-Condition ($global:ToolboxWireMockCertificateTest.inspected.Count -eq 7) 'Migration did not deduplicate container identifiers.'
    foreach ($mode in @('inspect-failure', 'invalid')) {
        $global:ToolboxWireMockCertificateTest.cleanupMode = $mode
        $global:ToolboxWireMockCertificateTest.removed = @()
        $failed = $false
        try { & (Join-Path $fixture 'docker_setup.ps1') -ContainerRuntime 'podman' *> (Join-Path $fixture 'setup.log') }
        catch { $failed = $true }
        Assert-Condition $failed 'Migration accepted unverifiable container ownership.'
        Assert-Condition ($global:ToolboxWireMockCertificateTest.removed.Count -eq 0) 'Migration removed containers after a failed ownership check.'
        Assert-Condition ($global:ToolboxWireMockCertificateTest.startCount -eq 3) 'Setup started services after a failed migration check.'
    }
    $global:ToolboxWireMockCertificateTest.cleanupMode = 'normal'
    $global:ToolboxWireMockCertificateTest.generatedMode = $true
    Add-Content -LiteralPath (Join-Path $containers '.env') -Value 'WIREMOCK_KEYSTORE_PASSWORD="auto"' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $certs 'wiremock.jks') -Value 'stale keystore'
    & (Join-Path $fixture 'docker_setup.ps1') -ContainerRuntime 'podman' *> (Join-Path $fixture 'setup.log')
    Assert-Condition ($global:ToolboxWireMockCertificateTest.generationCount -eq 2) 'Setup did not regenerate an existing keystore after replacing an auto password.'
    & (Join-Path $fixture 'docker_setup.ps1') -ContainerRuntime 'podman' *> (Join-Path $fixture 'setup.log')
    Assert-Condition ($global:ToolboxWireMockCertificateTest.generationCount -eq 2) 'Setup regenerated a keystore with an unchanged explicit password.'
    Write-Host 'PowerShell WireMock/setup tests passed: isolated certificate, provider-resolution, and targeted migration scenarios.'
} finally {
    [Environment]::SetEnvironmentVariable('TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD', $previousPassword, 'Process')
    [Environment]::SetEnvironmentVariable('JAVA_HOME', $previousJavaHome, 'Process')
    Remove-Variable -Name ToolboxWireMockCertificateTest -Scope Global -ErrorAction SilentlyContinue
    $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
    if ($resolvedTestRoot.StartsWith([System.IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedTestRoot)) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
