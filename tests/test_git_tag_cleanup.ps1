#Requires -Version 7.0
# The pipe-ref case requires Git reftable support; verified with Git 2.56.0.
[CmdletBinding()]
param(
    [string]$CleanupScriptPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Delete_Old_Git_Tags.ps1'),
    [string]$ShellPath = (Get-Process -Id $PID).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "toolbox-tag-cleanup-$([guid]::NewGuid().ToString('N'))"
$gitPath = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$CleanupScriptPath = [IO.Path]::GetFullPath($CleanupScriptPath)
$passed = 0

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function Invoke-Child {
    param([string]$Executable, [string]$Directory, [string[]]$Argument)
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.WorkingDirectory = $Directory
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.CreateNoWindow = $true
    foreach ($item in $Argument) { $start.ArgumentList.Add($item) }
    # Fixtures must never inherit repository routing, hooks, or user Git settings.
    foreach ($key in @($start.Environment.Keys)) {
        if ($key.StartsWith('GIT_', [StringComparison]::OrdinalIgnoreCase)) { $null = $start.Environment.Remove($key) }
    }
    $start.Environment['GIT_CONFIG_NOSYSTEM'] = '1'
    $start.Environment['GIT_CONFIG_GLOBAL'] = Join-Path $testRoot 'empty.gitconfig'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        $null = $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw 'Disposable tag-cleanup child timed out.'
        }
        [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $stdout.GetAwaiter().GetResult(); Error = $stderr.GetAwaiter().GetResult() }
    } finally { $process.Dispose() }
}

function Invoke-Git {
    param([string]$Directory, [string[]]$Argument)
    $result = Invoke-Child $gitPath $Directory $Argument
    if ($result.ExitCode -ne 0) { throw "Fixture Git failed: $($result.Error)" }
    $result.Output.Trim()
}

function New-TagFixture {
    param([string]$Name, [string]$OldTag = 'stale-release', [switch]$RemoteTag, [switch]$Reftable)
    $root = Join-Path $testRoot $Name
    $null = New-Item -ItemType Directory -Path $root
    $remote = Join-Path $root 'remote.git'
    $work = Join-Path $root 'work'
    $initArguments = @('init', '--quiet')
    if ($Reftable) { $initArguments += '--ref-format=reftable' }
    $null = Invoke-Git $root ($initArguments + @('--bare', $remote))
    $null = Invoke-Git $root ($initArguments + @('--initial-branch=main', $work))
    $null = Invoke-Git $work @('config', 'user.name', 'Disposable tag cleanup test')
    $null = Invoke-Git $work @('config', 'user.email', 'fixture@example.test')
    $null = Invoke-Git $work @('config', 'commit.gpgSign', 'false')
    [IO.File]::WriteAllText((Join-Path $work 'fixture.txt'), 'Local-only test fixture')
    $null = Invoke-Git $work @('add', 'fixture.txt')
    $null = Invoke-Git $work @('commit', '--quiet', '-m', 'fixture')
    $null = Invoke-Git $work @('remote', 'add', 'origin', $remote)
    $null = Invoke-Git $work @('push', '--quiet', 'origin', 'HEAD:refs/heads/main', "HEAD:refs/heads/$OldTag")
    $null = Invoke-Git $work @('tag', '1.2099.0101.1')
    $null = Invoke-Git $work @('tag', 'keep-important')
    $null = Invoke-Git $work @('tag', $OldTag)
    if ($RemoteTag) { $null = Invoke-Git $work @('push', '--quiet', 'origin', "refs/tags/$OldTag") }
    [pscustomobject]@{ Root = $root; Work = $work; Remote = $remote; OldTag = $OldTag }
}

function Has-Ref {
    param([string]$Directory, [string]$Reference)
    $result = Invoke-Child $gitPath $Directory @('show-ref', '--verify', '--quiet', $Reference)
    if ($result.ExitCode -notin 0, 1) { throw 'Fixture ref inspection failed.' }
    $result.ExitCode -eq 0
}

function Invoke-Cleanup {
    param([string]$Directory, [switch]$DryRun)
    $arguments = @('-NoProfile', '-NonInteractive', '-File', $CleanupScriptPath)
    if ($DryRun) { $arguments += '-DryRun' }
    Invoke-Child $shellPath $Directory $arguments
}

try {
    $null = New-Item -ItemType Directory -Path $testRoot
    [IO.File]::WriteAllText((Join-Path $testRoot 'empty.gitconfig'), '')

    $fixture = New-TagFixture 'missing-remote-tag'
    $result = Invoke-Cleanup $fixture.Work
    Assert-Condition (Has-Ref $fixture.Remote 'refs/heads/stale-release') 'A stale local tag must not delete a same-name remote branch.'
    Assert-Condition ($result.ExitCode -eq 0) 'An already absent remote tag must allow stale local tag cleanup.'
    Assert-Condition (-not (Has-Ref $fixture.Work 'refs/tags/stale-release')) 'A stale local tag must be removed after successful remote cleanup.'
    $passed++

    $fixture = New-TagFixture 'same-name-branch-and-tag' -RemoteTag
    $null = Invoke-Git $fixture.Work @('branch', 'stale-release')
    $result = Invoke-Cleanup $fixture.Work
    Assert-Condition ($result.ExitCode -eq 0) 'An existing remote tag must be deleted successfully.'
    Assert-Condition (Has-Ref $fixture.Remote 'refs/heads/stale-release') 'Deleting the tag must preserve a same-name remote branch.'
    Assert-Condition (Has-Ref $fixture.Work 'refs/heads/stale-release') 'Deleting the tag must preserve a same-name local branch.'
    Assert-Condition (-not (Has-Ref $fixture.Remote 'refs/tags/stale-release') -and -not (Has-Ref $fixture.Work 'refs/tags/stale-release')) 'Successful cleanup must remove both tag refs.'
    Assert-Condition ((Has-Ref $fixture.Work 'refs/tags/1.2099.0101.1') -and (Has-Ref $fixture.Work 'refs/tags/keep-important')) 'Newest and keep-substring tags must remain.'
    $passed++

    $fixture = New-TagFixture 'encoded-old-tag' -OldTag '1.2000.0101.1' -RemoteTag
    $result = Invoke-Cleanup $fixture.Work
    Assert-Condition ($result.ExitCode -eq 0 -and -not (Has-Ref $fixture.Remote 'refs/tags/1.2000.0101.1') -and -not (Has-Ref $fixture.Work 'refs/tags/1.2000.0101.1')) 'Encoded-date retention must still delete ordinary old tags.'
    $passed++

    $fixture = New-TagFixture 'tag-name-leading-tags' -OldTag 'tags/old-release' -RemoteTag
    $result = Invoke-Cleanup $fixture.Work
    Assert-Condition ($result.ExitCode -eq 0 -and -not (Has-Ref $fixture.Remote 'refs/tags/tags/old-release') -and -not (Has-Ref $fixture.Work 'refs/tags/tags/old-release')) 'Tag names beginning with tags/ must be preserved exactly during deletion.'
    Assert-Condition (Has-Ref $fixture.Remote 'refs/heads/tags/old-release') 'The tags/ prefix in a tag name must not change the branch namespace.'
    $passed++

    # Reftable permits legal pipe-containing Git refs on Windows filesystems too.
    $fixture = New-TagFixture 'pipe-containing-tag-names' -OldTag 'stale|suffix' -RemoteTag -Reftable
    foreach ($name in @('keep-important|suffix', 'protected|keep-important')) {
        $null = Invoke-Git $fixture.Work @('tag', $name)
        $null = Invoke-Git $fixture.Work @('push', '--quiet', 'origin', "refs/tags/$name")
    }
    $result = Invoke-Cleanup $fixture.Work
    Assert-Condition ($result.ExitCode -eq 0 -and -not (Has-Ref $fixture.Remote 'refs/tags/stale|suffix') -and -not (Has-Ref $fixture.Work 'refs/tags/stale|suffix')) 'A literal pipe must not truncate the selected tag name.'
    foreach ($name in @('keep-important|suffix', 'protected|keep-important')) {
        Assert-Condition ((Has-Ref $fixture.Work "refs/tags/$name") -and (Has-Ref $fixture.Remote "refs/tags/$name")) 'Keep-substring matching must inspect the complete pipe-containing tag name.'
    }
    Assert-Condition (Has-Ref $fixture.Remote 'refs/heads/stale|suffix') 'Pipe-containing tag deletion must preserve the exact same-name remote branch.'
    $passed++

    $fixture = New-TagFixture 'symbolic-local-tag' -RemoteTag
    $null = Invoke-Git $fixture.Work @('update-ref', '--no-deref', '-d', 'refs/tags/stale-release')
    $null = Invoke-Git $fixture.Work @('symbolic-ref', 'refs/tags/stale-release', 'refs/heads/main')
    $result = Invoke-Cleanup $fixture.Work
    Assert-Condition (Has-Ref $fixture.Work 'refs/heads/main') 'Deleting a symbolic tag must not delete its local branch target.'
    Assert-Condition ($result.ExitCode -eq 0 -and -not (Has-Ref $fixture.Work 'refs/tags/stale-release') -and -not (Has-Ref $fixture.Remote 'refs/tags/stale-release')) 'Cleanup must delete only the symbolic tag and remote tag refs.'
    $passed++

    $fixture = New-TagFixture 'symbolic-remote-tag' -RemoteTag
    $null = Invoke-Git $fixture.Work @('tag', 'a-delete-before-symbolic')
    $null = Invoke-Git $fixture.Work @('push', '--quiet', 'origin', 'refs/tags/a-delete-before-symbolic')
    $null = Invoke-Git $fixture.Remote @('update-ref', '--no-deref', '-d', 'refs/tags/stale-release')
    $null = Invoke-Git $fixture.Remote @('symbolic-ref', 'refs/tags/stale-release', 'refs/heads/main')
    $result = Invoke-Cleanup $fixture.Work
    Assert-Condition ($result.ExitCode -ne 0 -and $result.Error -match 'symbolic remote tag') 'An advertised remote symbolic tag must be rejected before deletion.'
    Assert-Condition ((Has-Ref $fixture.Remote 'refs/heads/main') -and (Has-Ref $fixture.Remote 'refs/tags/stale-release') -and (Has-Ref $fixture.Work 'refs/tags/stale-release')) 'Rejecting a remote symbolic tag must preserve its target, alias and local tag.'
    Assert-Condition ((Has-Ref $fixture.Remote 'refs/tags/a-delete-before-symbolic') -and (Has-Ref $fixture.Work 'refs/tags/a-delete-before-symbolic')) 'All selected remote refs must be inspected before any tag deletion.'
    $passed++

    $fixture = New-TagFixture 'unreachable-remote' -RemoteTag
    $null = Invoke-Git $fixture.Work @('remote', 'set-url', 'origin', (Join-Path $fixture.Root 'missing-local-remote.git'))
    $result = Invoke-Cleanup $fixture.Work
    Assert-Condition ($result.ExitCode -ne 0 -and $result.Error -match 'remote tag.*exit code') 'Remote inspection failures must report an error and fail the process.'
    Assert-Condition ((Has-Ref $fixture.Work 'refs/tags/stale-release') -and (Has-Ref $fixture.Remote 'refs/tags/stale-release')) 'Remote failure must retain local and remote evidence.'
    $passed++

    $fixture = New-TagFixture 'rejected-remote-push' -RemoteTag
    $null = Invoke-Git $fixture.Remote @('config', 'receive.hideRefs', 'refs/tags/stale-release')
    $result = Invoke-Cleanup $fixture.Work
    Assert-Condition ($result.ExitCode -ne 0 -and $result.Error -match 'delete remote tag.*exit code') 'A rejected native push must report an error and fail the process.'
    Assert-Condition ((Has-Ref $fixture.Work 'refs/tags/stale-release') -and (Has-Ref $fixture.Remote 'refs/tags/stale-release')) 'Rejected remote deletion must retain both tag refs.'
    $passed++

    $fixture = New-TagFixture 'locked-local-tag' -RemoteTag
    [IO.File]::WriteAllText((Join-Path $fixture.Work '.git/refs/tags/stale-release.lock'), '')
    $result = Invoke-Cleanup $fixture.Work
    Assert-Condition ($result.ExitCode -ne 0 -and $result.Error -match 'local tag.*exit code') 'Local ref deletion failures must be reported.'
    Assert-Condition ((Has-Ref $fixture.Work 'refs/tags/stale-release') -and -not (Has-Ref $fixture.Remote 'refs/tags/stale-release')) 'Local deletion is attempted only after successful remote tag deletion.'
    $passed++

    $fixture = New-TagFixture 'dry-run' -RemoteTag
    $localBefore = Invoke-Git $fixture.Work @('for-each-ref', '--format=%(refname):%(objectname)')
    $remoteBefore = Invoke-Git $fixture.Remote @('for-each-ref', '--format=%(refname):%(objectname)')
    $result = Invoke-Cleanup $fixture.Work -DryRun
    Assert-Condition ($result.ExitCode -eq 0 -and $result.Output.Contains('DRY RUN complete. No changes made.')) 'DryRun must report its non-mutating result.'
    Assert-Condition ($localBefore -ceq (Invoke-Git $fixture.Work @('for-each-ref', '--format=%(refname):%(objectname)'))) 'DryRun must preserve every local ref.'
    Assert-Condition ($remoteBefore -ceq (Invoke-Git $fixture.Remote @('for-each-ref', '--format=%(refname):%(objectname)'))) 'DryRun must preserve every remote ref.'
    $passed++

    $result = Invoke-Cleanup $testRoot
    Assert-Condition ($result.ExitCode -ne 0 -and $result.Error -match 'enumerate.*exit code') 'Tag enumeration failure must not be reported as no tags.'
    $passed++

    Write-Output "$passed local-only Git tag cleanup cases passed; no network remotes used."
} finally {
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedTestRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTestRoot).StartsWith('toolbox-tag-cleanup-')) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
