$env:TOOLBOX_IMAGE_MIRRORS = 'docker.io=registry.example.invalid/docker-hub'
# Local-development image acquisition regression tests; never contact an engine.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../.devcontainer/ensure_image.ps1')
$script:calls = [Collections.Generic.List[string]]::new()
$script:mode = 'cached'
function Test-Engine {
    $script:calls.Add(($args -join ' '))
    if ($args[0] -eq 'pull') { Write-Output 'pull-progress' }
    $global:LASTEXITCODE = 0
    if ($args[0] -eq 'image') {
        if ($script:mode -ne 'cached' -and -not ($script:mode -eq 'mirror' -and $args[-1] -like 'registry.example.invalid/*')) { $global:LASTEXITCODE = 1 }
    }
    if ($args[0] -eq 'pull' -and $script:mode -in @('mirror', 'fallback', 'denied') -and $args[-1] -notlike 'registry.example.invalid/*') { $global:LASTEXITCODE = 1 }
    if ($script:mode -eq 'denied' -and $args[0] -eq 'pull') { $global:LASTEXITCODE = 1 }
}
foreach ($mode in @('cached', 'missing', 'mirror', 'fallback', 'denied')) {
    $script:mode = $mode
    $script:calls.Clear()
    $failed = $false
    try { $result = @(Ensure-ToolboxImage -Runtime Test-Engine -Image docker.io/library/redis:latest); if ($result.Count) { throw 'Pull progress leaked into captured output.' } } catch { $failed = $true }
    if ($failed -ne ($mode -eq 'denied')) { throw "Unexpected outcome: $mode" }
    if ($mode -eq 'cached' -and $calls.Count -ne 1) { throw 'Cached image contacted a registry.' }
    if ($mode -eq 'missing' -and @($calls | Where-Object { $_ -like 'pull *' }).Count -ne 1) { throw 'Missing image was not pulled once.' }
    if ($mode -eq 'mirror' -and ($calls[-1] -ne 'tag registry.example.invalid/docker-hub/library/redis:latest docker.io/library/redis:latest' -or @($calls | Where-Object { $_ -like 'pull registry.example.invalid*' }).Count)) { throw 'Cached fallback was not reused and tagged.' }
}
Write-Output 'Image preparation tests passed: cached, missing, cached registry fallback, and denied registries.'

$global:LASTEXITCODE = 0
