#!/usr/bin/env pwsh
<#
.SYNOPSIS
Inventory a GitHub repository and its wiki for the github-wiki skill.

.DESCRIPTION
Mirrors wiki_inventory.py: same checks, same sections, same JSON shape.
Reads repository identity through the GitHub CLI, clones or refreshes the wiki
into a folder outside the project tree, and reports wiki pages (title, size,
last commit, headings, links, broken links, orphans) plus a depth-limited
project tree, documentation-bearing files, and recent commits.

.EXAMPLE
pwsh -NoProfile -File wiki_inventory.ps1 -Repo . -WikiDir ../my-repo.wiki
#>
[CmdletBinding()]
param(
    [string]$Repo = '.',
    [string]$WikiDir,
    [int]$Depth = 3,
    [switch]$NoClone,
    [switch]$Json
)

$ErrorActionPreference = 'Continue'

$DocPatterns = @(
    '^(readme|contributing|changelog|license|licence|security|code_of_conduct|support)([.-].*)?$',
    '^codeowners$',
    '^(agents|claude|gemini|copilot-instructions)\.md$',
    '^docs/',
    '^\.github/',
    '^\.agents/',
    '^\.claude(-plugin)?/',
    '^\.codex-plugin/',
    '(^|/)plugin\.json$',
    '(^|/)marketplace\.json$',
    '(^|/)skill\.md$',
    '\.(sln|csproj|fsproj|vbproj)$',
    '(^|/)package\.json$',
    '(^|/)pyproject\.toml$',
    '(^|/)requirements[^/]*\.txt$',
    '(^|/)dockerfile$',
    '(^|/)docker-compose[^/]*\.ya?ml$',
    '(^|/)azure-pipelines[^/]*\.ya?ml$',
    '(^|/)appsettings[^/]*\.json$',
    '(^|/)\.env\.example$',
    '(^|/)adr[s]?/'
)
$SpecialPages = @('home', '_sidebar', '_footer')

function Invoke-Cmd {
    param([string[]]$Command, [string]$Cwd)
    $exe = $Command[0]
    $rest = @()
    if ($Command.Count -gt 1) { $rest = $Command[1..($Command.Count - 1)] }
    if (-not (Get-Command $exe -ErrorAction SilentlyContinue)) {
        return @{ Code = 127; Out = "${exe}: command not found" }
    }
    $prev = Get-Location
    try {
        if ($Cwd) { Set-Location -LiteralPath $Cwd }
        $out = & $exe @rest 2>&1 | ForEach-Object { "$_" }
        return @{ Code = $LASTEXITCODE; Out = (($out -join "`n").Trim()) }
    } finally {
        Set-Location $prev
    }
}

function Normalize-Target {
    param([string]$Raw)
    $t = $Raw.Trim().Split('#', 2)[0]
    if ($t.Contains('|')) { $t = $t.Split('|', 2)[1] }
    $t = $t.Trim().Replace(' ', '-')
    if ($t.ToLower().EndsWith('.md')) { $t = $t.Substring(0, $t.Length - 3) }
    return $t
}

function Test-External {
    param([string]$Target)
    return [bool]($Target -imatch '^(https?:|mailto:|ftp:|//|#)')
}

function Normalize-GitRemote {
    param([string]$Remote)
    $value = $Remote.Trim().TrimEnd('/')
    if ($value -imatch '^git@github\.com:(.+)$') {
        $value = "https://github.com/$($Matches[1])"
    }
    if ($value.EndsWith('.git', [System.StringComparison]::OrdinalIgnoreCase)) {
        $value = $value.Substring(0, $value.Length - 4)
    }
    return $value.ToLowerInvariant()
}

function Sort-Ordinal {
    param([string[]]$Items)
    $arr = [string[]]@($Items)
    [Array]::Sort($arr, [System.StringComparer]::Ordinal)  # byte-order sort, matches Python sorted()
    return ,$arr
}

function Get-RepoInventory {
    param([string]$RepoArg, [int]$TreeDepth)
    $r = Invoke-Cmd @('git', '-C', $RepoArg, 'rev-parse', '--show-toplevel')
    if ($r.Code -ne 0) { Write-Error "error: $RepoArg is not inside a git repository ($($r.Out))"; exit 1 }
    $root = [System.IO.Path]::GetFullPath($r.Out)
    $info = [ordered]@{ root = $root; folder = (Split-Path $root -Leaf); warnings = @() }

    $info.branch = (Invoke-Cmd @('git', 'branch', '--show-current') $root).Out
    if (-not $info.branch) { $info.branch = '(detached)' }
    $info.head = (Invoke-Cmd @('git', 'rev-parse', '--short', 'HEAD') $root).Out
    $status = (Invoke-Cmd @('git', 'status', '--short') $root).Out
    $info.uncommitted_changes = @($status -split "`n" | Where-Object { $_.Trim() }).Count
    $remote = (Invoke-Cmd @('git', 'remote', 'get-url', 'origin') $root).Out
    $info.origin = if ($remote -and -not $remote.StartsWith('fatal')) { $remote } else { $null }

    $gh = @{}
    $ghr = Invoke-Cmd @('gh', 'repo', 'view', '--json', 'nameWithOwner,url,description,defaultBranchRef,hasWikiEnabled,isPrivate') $root
    if ($ghr.Code -eq 0) {
        try { $gh = $ghr.Out | ConvertFrom-Json -AsHashtable } catch { $info.warnings += 'gh repo view returned unparseable JSON' }
    } else {
        $first = if ($ghr.Out) { ($ghr.Out -split "`n")[0] } else { 'unknown error' }
        $info.warnings += "gh repo view failed: $first"
    }

    $url = $gh['url']
    if (-not $url -and $info.origin) {
        if ($info.origin -match '^(?:git@github\.com:|https://github\.com/)(.+?)(?:\.git)?$') {
            $url = "https://github.com/$($Matches[1])"
            $info.warnings += 'repository URL derived from the origin remote because gh was unavailable'
        }
    }
    $info.name_with_owner = if ($gh['nameWithOwner']) { $gh['nameWithOwner'] } elseif ($url) { ($url -split 'github.com/')[-1] } else { $null }
    $info.url = $url
    $info.description = $gh['description']
    $info.default_branch = if ($gh['defaultBranchRef']) { $gh['defaultBranchRef']['name'] } else { $null }
    $info.wiki_enabled = if ($gh.ContainsKey('hasWikiEnabled')) { $gh['hasWikiEnabled'] } else { $null }
    $info.is_private = $gh['isPrivate']
    $info.wiki_web_url = if ($url) { "$url/wiki" } else { $null }
    $info.wiki_git_url = if ($url) { "$url.wiki.git" } else { $null }

    if ($info.default_branch -and $info.branch -ne $info.default_branch) {
        $info.warnings += "checkout is on '$($info.branch)', not the default branch '$($info.default_branch)'"
    }
    if ($info.uncommitted_changes) { $info.warnings += "working tree has $($info.uncommitted_changes) uncommitted change(s)" }
    if ($info.wiki_enabled -eq $false) { $info.warnings += 'the repository wiki is disabled (Settings > Features > Wikis)' }

    Invoke-Cmd @('git', 'fetch', '--quiet', 'origin') $root | Out-Null
    $info.behind_remote = $null
    if ($info.branch -ne '(detached)') {
        $b = Invoke-Cmd @('git', 'rev-list', '--count', "HEAD..origin/$($info.branch)") $root
        if ($b.Code -eq 0 -and $b.Out -match '^\d+$') { $info.behind_remote = [int]$b.Out }
        if ($info.behind_remote) { $info.warnings += "local branch is $($info.behind_remote) commit(s) behind origin/$($info.branch)" }
    }

    $files = @((Invoke-Cmd @('git', 'ls-files') $root).Out -split "`n" | Where-Object { $_ })
    $info.tracked_files = $files.Count
    $tree = [ordered]@{}
    foreach ($f in $files) {
        $parts = $f -split '/'
        $node = $tree
        $dirs = $parts[0..([Math]::Max($parts.Count - 2, 0))]
        if ($parts.Count -eq 1) { $dirs = @() }
        $i = 0
        foreach ($part in $dirs) {
            if ($i -ge $TreeDepth) { break }
            $key = "$part/"
            if (-not $node.Contains($key)) { $node[$key] = [ordered]@{} }
            $node = $node[$key]
            $i++
        }
        if ($parts.Count -le $TreeDepth) { if (-not $node.Contains($parts[-1])) { $node[$parts[-1]] = $null } }
        else { $node['...'] = $null }
    }
    $info.tree = $tree
    $info.doc_files = Sort-Ordinal @($files | Where-Object { $f = $_; ($DocPatterns | Where-Object { $f -imatch $_ }).Count -gt 0 })
    $info.extensions = @($files | ForEach-Object { $e = [System.IO.Path]::GetExtension($_).ToLower(); if ($e) { $e } else { '(none)' } } |
        Group-Object | Sort-Object Count -Descending | Select-Object -First 10 | ForEach-Object { ,@($_.Name, $_.Count) })
    $info.recent_commits = @((Invoke-Cmd @('git', 'log', '-15', '--format=%h %cs %s') $root).Out -split "`n" | Where-Object { $_ })
    return $info
}

function Render-Tree {
    param($Node, [int]$Indent = 0)
    $lines = @()
    $keys = @($Node.Keys) | Sort-Object { -not $_.EndsWith('/') }, { $_.ToLower() }
    foreach ($name in $keys) {
        $lines += ('  ' * $Indent) + $name
        if ($Node[$name] -is [System.Collections.IDictionary]) { $lines += Render-Tree $Node[$name] ($Indent + 1) }
    }
    return $lines
}

function Prepare-Wiki {
    param($Info, [string]$Dir, [bool]$SkipClone)
    $full = [System.IO.Path]::GetFullPath($Dir)
    $wiki = [ordered]@{ dir = $full; status = $null; warnings = @(); pages = @(); error = $false }
    $rootPrefix = $Info.root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if ($full.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or $full -ieq $Info.root) {
        $wiki.warnings += 'wiki directory is inside the project working tree; choose a sibling or temp folder'
        $wiki.status = 'invalid wiki directory'
        $wiki.error = $true
        return $wiki
    }
    if ($Info.wiki_enabled -eq $false) {
        $wiki.warnings += 'repository wiki is disabled; clone and refresh were skipped'
        $wiki.status = 'wiki disabled'
        $wiki.error = $true
        return $wiki
    }
    $isRepo = Test-Path (Join-Path $full '.git')
    if ($isRepo) {
        $remoteResult = Invoke-Cmd @('git', 'remote', 'get-url', 'origin') $full
        $remote = $remoteResult.Out
        if ($remoteResult.Code -ne 0 -or -not $Info.wiki_git_url) {
            $wiki.warnings += 'existing clone could not be verified against the repository wiki'
            $wiki.status = 'unverified existing clone'
            $wiki.error = $true
            return $wiki
        }
        if ((Normalize-GitRemote $remote) -ne (Normalize-GitRemote $Info.wiki_git_url)) {
            $wiki.warnings += "existing clone remote is $remote, expected $($Info.wiki_git_url)"
            $wiki.status = 'existing clone remote mismatch'
            $wiki.error = $true
            return $wiki
        }
        if ($SkipClone) { $wiki.status = 'existing clone (not refreshed)' }
        else {
            $p = Invoke-Cmd @('git', 'pull', '--ff-only', '--quiet') $full
            $last = if ($p.Out) { ($p.Out -split "`n")[-1] } else { 'unknown' }
            if ($p.Code -eq 0) {
                $wiki.status = 'refreshed'
            } else {
                $wiki.status = "pull failed: $last"
                $wiki.error = $true
                return $wiki
            }
        }
    } elseif ($SkipClone) {
        $wiki.status = 'missing (clone skipped)'
        $wiki.warnings += 'wiki directory does not exist and -NoClone was given'
        $wiki.error = $true
        return $wiki
    } elseif (-not $Info.wiki_git_url) {
        $wiki.status = 'unknown wiki URL'
        $wiki.warnings += 'repository identity did not provide a wiki clone URL'
        $wiki.error = $true
        return $wiki
    } else {
        $c = Invoke-Cmd @('git', 'clone', '--quiet', $Info.wiki_git_url, $full)
        if ($c.Code -ne 0) {
            $last = if ($c.Out) { ($c.Out -split "`n")[-1] } else { 'unknown' }
            $wiki.status = "clone failed: $last"
            $wiki.error = $true
            if ($Info.wiki_enabled) {
                $wiki.warnings += "wiki is enabled but cannot be cloned; it probably has no pages yet. Create the first page at $($Info.wiki_web_url) and retry"
            }
            return $wiki
        }
        $wiki.status = 'cloned'
    }

    $wiki.branch = (Invoke-Cmd @('git', 'branch', '--show-current') $full).Out
    $wiki.head = (Invoke-Cmd @('git', 'log', '-1', '--format=%h %cs %s') $full).Out

    $entries = Get-ChildItem -LiteralPath $full -Force | Where-Object { $_.Name -ne '.git' }
    $pageFiles = @($entries | Where-Object { -not $_.PSIsContainer -and $_.Extension -imatch '^\.(md|markdown)$' })
    $sortedNames = Sort-Ordinal @($pageFiles | ForEach-Object Name)
    $byName = @{}; foreach ($pf in $pageFiles) { $byName[$pf.Name] = $pf }
    $pageFiles = @($sortedNames | ForEach-Object { $byName[$_] })
    $pageNames = @($pageFiles | ForEach-Object Name)
    $wiki.other_entries = Sort-Ordinal @($entries | Where-Object { $pageNames -notcontains $_.Name } | ForEach-Object Name)
    $names = @{}
    foreach ($pf in $pageFiles) { $names[$pf.BaseName.ToLower()] = $pf.BaseName }
    $inbound = @{}
    $pages = @()
    foreach ($pf in $pageFiles) {
        $text = Get-Content -LiteralPath $pf.FullName -Raw -Encoding UTF8
        if ($null -eq $text) { $text = '' }
        $stem = $pf.BaseName
        $last = (Invoke-Cmd @('git', 'log', '-1', '--format=%cs', '--', $pf.Name) $full).Out
        $headings = @([regex]::Matches($text, '(?m)^(#{1,2})\s+(.*\S)\s*$') | ForEach-Object { $_.Groups[2].Value })
        $links = @()
        foreach ($m in [regex]::Matches($text, '\[\[([^\]]+)\]\]')) { $links += Normalize-Target $m.Groups[1].Value }
        foreach ($m in [regex]::Matches($text, '(?<!\!)\[[^\]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)')) {
            if (-not (Test-External $m.Groups[1].Value)) { $links += Normalize-Target $m.Groups[1].Value }
        }
        $assets = @([regex]::Matches($text, '!\[[^\]]*\]\(([^)\s]+)') | ForEach-Object { $_.Groups[1].Value } | Where-Object { -not (Test-External $_) })
        $broken = @()
        foreach ($t in $links) {
            if (-not $t) { continue }
            $key = $t.ToLower()
            if ($t.Contains('/') -and -not $names.ContainsKey($key)) {
                if (-not (Test-Path -LiteralPath (Join-Path $full $t))) { $broken += $t }
                continue
            }
            if ($names.ContainsKey($key)) {
                if ($key -ne $stem.ToLower()) { $inbound[$key] = 1 + [int]$inbound[$key] }
            } elseif (-not (Test-Path -LiteralPath (Join-Path $full $t))) { $broken += $t }
        }
        foreach ($a in $assets) {
            if (-not (Test-Path -LiteralPath (Join-Path $full ($a.Split('#')[0])))) { $broken += $a }
        }
        $lineCount = ($text -split "`n").Count
        if ($text.EndsWith("`n") -or $text.Length -eq 0) { $lineCount-- }
        $pages += [ordered]@{
            file = $pf.Name
            title = $stem.Replace('-', ' ')
            bytes = $pf.Length
            lines = $lineCount
            last_commit = if ($last) { $last } else { $null }
            headings = $headings
            links = Sort-Ordinal @($links | Select-Object -Unique)
            broken_links = Sort-Ordinal @($broken | Select-Object -Unique)
        }
    }
    foreach ($page in $pages) {
        $stem = [System.IO.Path]::GetFileNameWithoutExtension($page.file)
        $page.inbound_links = [int]$inbound[$stem.ToLower()]
        $page.orphan = ($SpecialPages -notcontains $stem.ToLower()) -and ($page.inbound_links -eq 0)
    }
    $wiki.pages = $pages
    $wiki.page_count = $pages.Count
    $wiki.broken_link_count = [int](($pages | ForEach-Object { $_.broken_links.Count } | Measure-Object -Sum).Sum)
    $wiki.orphan_count = @($pages | Where-Object { $_.orphan }).Count
    if ($null -eq $wiki.broken_link_count) { $wiki.broken_link_count = 0 }
    return $wiki
}

function Render-Text {
    param($Info, $Wiki, [int]$TreeDepth)
    $out = @()
    $out += '== Repository =='
    $out += "root:            $($Info.root)"
    $out += "repository:      $(if ($Info.name_with_owner) { $Info.name_with_owner } else { '(unknown)' })"
    $out += "url:             $(if ($Info.url) { $Info.url } else { '(unknown)' })"
    $out += "description:     $(if ($Info.description) { $Info.description } else { '(none)' })"
    $out += "branch:          $($Info.branch) @ $($Info.head)"
    $out += "default branch:  $(if ($Info.default_branch) { $Info.default_branch } else { '(unknown)' })"
    $out += "uncommitted:     $($Info.uncommitted_changes)"
    $out += "behind remote:   $(if ($null -ne $Info.behind_remote) { $Info.behind_remote } else { '(unknown)' })"
    $out += "wiki enabled:    $(if ($null -ne $Info.wiki_enabled) { $Info.wiki_enabled } else { '(unknown)' })"
    $out += "wiki url:        $(if ($Info.wiki_web_url) { $Info.wiki_web_url } else { '(unknown)' })"
    $out += "wiki git:        $(if ($Info.wiki_git_url) { $Info.wiki_git_url } else { '(unknown)' })"
    foreach ($w in $Info.warnings) { $out += "WARNING: $w" }

    $out += ''
    $out += '== Wiki =='
    $out += "clone dir:       $($Wiki.dir)"
    $out += "status:          $($Wiki.status)"
    if ($Wiki.branch) {
        $out += "wiki branch:     $($Wiki.branch)"
        $out += "wiki head:       $($Wiki.head)"
    }
    foreach ($w in $Wiki.warnings) { $out += "WARNING: $w" }
    if ($Wiki.pages.Count) {
        $out += "pages: $($Wiki.page_count)   broken links: $($Wiki.broken_link_count)   orphans: $($Wiki.orphan_count)"
        foreach ($p in $Wiki.pages) {
            $flags = @()
            if ($p.orphan) { $flags += 'ORPHAN' }
            if ($p.broken_links.Count) { $flags += "BROKEN:$($p.broken_links.Count)" }
            $flag = if ($flags.Count) { "  [$($flags -join ' ')]" } else { '' }
            $lc = if ($p.last_commit) { $p.last_commit } else { '?' }
            $out += "- $($p.file)  ($($p.lines) lines, $($p.bytes) bytes, last $lc, inbound $($p.inbound_links))$flag"
            foreach ($h in ($p.headings | Select-Object -First 12)) { $out += "    # $h" }
            if ($p.headings.Count -gt 12) { $out += "    ... $($p.headings.Count - 12) more headings" }
            foreach ($b in $p.broken_links) { $out += "    broken -> $b" }
        }
        if ($Wiki.other_entries.Count) { $out += "other entries: $($Wiki.other_entries -join ', ')" }
    }

    $out += ''
    $out += "== Project tree (depth $TreeDepth, $($Info.tracked_files) tracked files) =="
    $out += Render-Tree $Info.tree
    $out += ''
    $out += '== Documentation-bearing files =='
    if ($Info.doc_files.Count) { foreach ($f in $Info.doc_files) { $out += "- $f" } } else { $out += '(none)' }
    $out += ''
    $out += '== File types =='
    foreach ($e in $Info.extensions) { $out += ('{0,-12} {1}' -f $e[0], $e[1]) }
    $out += ''
    $out += '== Recent commits =='
    $out += $Info.recent_commits
    return ($out -join "`n")
}

$info = Get-RepoInventory -RepoArg $Repo -TreeDepth $Depth
if (-not $WikiDir) { $WikiDir = Join-Path (Split-Path $info.root -Parent) ($info.folder + '.wiki') }
$wiki = Prepare-Wiki -Info $info -Dir $WikiDir -SkipClone:$NoClone.IsPresent

if ($Json) {
    [ordered]@{ repository = $info; wiki = $wiki } | ConvertTo-Json -Depth 12
} else {
    Render-Text -Info $info -Wiki $wiki -TreeDepth $Depth
}

if ($wiki.error) { exit 1 }
$problems = [int]$wiki.broken_link_count + [int]$wiki.orphan_count
if ($problems) { exit 2 } else { exit 0 }
