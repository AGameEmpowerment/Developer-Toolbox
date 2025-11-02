<#+
        Delete_Old_Tags.ps1
        --------------------
        Purpose:
                Clean up old git tags from the repository according to an encoded date in the tag name
                while preserving important tags and avoiding destructive mass-deletions.

        Behavior summary:
            * Preserves any tag whose name contains the configured KeepSubstring (case-insensitive).
            * Always preserves the newest tag. "Newest" is chosen by this priority:
                    1) Encoded date parsed from the tag name (pattern: Version.Year.MonthDay.Build),
                    2) Build number (the final numeric segment) for tie-breaking,
                    3) git creatordate (from for-each-ref) as a final tie-breaker/fallback.
                If no tags match the encoded pattern, the newest tag is selected by creatordate.
            * Deletes tags whose encoded date is older than the RetentionDays window.
            * Deletes tags that do not match the expected encoded pattern (non-standard names),
                unless they are the newest tag or contain the keep substring.
            * Never deletes all tags: if the computed deletion set would include every tag, the script
                aborts and makes no changes.

        Parameters (script args):
            - [int] $RetentionDays  (default: 60)   : retention window in days compared against the encoded date parsed from the tag name.
            - [string] $KeepSubstring (default: 'keep') : case-insensitive substring; matching tags are preserved.
            - [switch] $DryRun  : when specified, the script reports actions but performs no deletions.

        Expected tag name format (encoded date):
            Version.Year.MonthDay.Build  e.g.  1.2024.0101.1
            Regex used to detect/parse this pattern: ^\d+\.(\d{4})\.(\d{4})\.\d+$

        Operational / safety notes:
            * Uses git's creatordate (via for-each-ref) so lightweight tags have a sortable date.
            * Exits early with a message when no tags are found.
            * Dry-run mode is supported via the -DryRun switch; when set the script only emits a report
                and performs no destructive operations.
            * When not in DryRun mode, deletions are performed both locally (git tag -d) and remotely
                (git push origin --delete <tag>).
            * A final safeguard prevents deleting all tags; if the deletion set would include every tag,
                the script aborts without making changes.

        Examples:
            .\Delete_Old_Tags.ps1 -RetentionDays 90
            .\Delete_Old_Tags.ps1 -KeepSubstring 'do-not-delete' -DryRun
#>

param(
    # Number of days to retain based on the date embedded in the tag NAME (not the tag object date)
    [int]$RetentionDays = 60,
    # Substring indicating a tag must be kept
    [string]$KeepSubstring = 'keep',
    # When specified, no destructive actions are taken; a report is emitted instead
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# Compute cutoff based on NOW minus retention days (used only with the encoded date in name)
$timeFrame = (Get-Date).AddDays(-1 * $RetentionDays)

$tagNamePattern = '^\d+\.(\d{4})\.(\d{4})\.\d+$'
$keepRegex = [Regex]::new([Regex]::Escape($KeepSubstring), 'IgnoreCase')

# Retrieve all tags with a consistent sortable creation date (creatordate falls back to commit date for lightweight tags)
$rawTagLines = git for-each-ref --sort=creatordate --format '%(refname:short)|%(creatordate:iso8601)' refs/tags

if (-not $rawTagLines -or $rawTagLines.Count -eq 0) {
    Write-Output 'No tags found; nothing to do.'
    return
}

$tagObjects = $rawTagLines | ForEach-Object {
    $parts = $_ -split '\|',2
    $name  = $parts[0]
    $dateString = $parts[1]
    try { $parsedDate = [DateTime]::Parse($dateString) } catch { $parsedDate = [DateTime]::MinValue }

    # Attempt to derive an encoded date from the tag name pattern Version.Year.MonthDay.Build
    $encodedDate = $null
    $build = $null
    if ($name -match $tagNamePattern) {
        $year  = $Matches[1]
        $monthDay = $Matches[2]
        $month = $monthDay.Substring(0,2)
        $day   = $monthDay.Substring(2,2)
        try { $encodedDate = [DateTime]::ParseExact("$year-$month-$day", 'yyyy-MM-dd', $null) } catch { $encodedDate = $null }
        # Extract build (last segment) for tie-breaking
        $segments = $name.Split('.')
        if ($segments.Count -ge 4) { [void][int]::TryParse($segments[-1], [ref]$build) }
    }

    [pscustomobject]@{ Name = $name; CreateDate = $parsedDate; EncodedDate = $encodedDate; Build=$build }
}

# Determine newest tag preference order: EncodedDate (if any exist) then Build then CreateDate
$patterned = $tagObjects | Where-Object { $_.EncodedDate -ne $null }
if ($patterned.Count -gt 0) {
    $newestTag = $patterned | Sort-Object EncodedDate, Build, CreateDate | Select-Object -Last 1
    $newestReason = 'NewestByEncodedDate'
} else {
    # Fallback: by creation date
    $newestTag = ($tagObjects | Sort-Object CreateDate | Select-Object -Last 1)
    $newestReason = 'NewestByCreateDate'
}
Write-Output "Newest tag preserved: $($newestTag.Name) (Reason=$newestReason EncodedDate=$($newestTag.EncodedDate) CreateDate=$($newestTag.CreateDate))"

# We'll build a structured report for optional DryRun output
$report = @()

$deletionCandidates = @()
$preserved = @($newestTag.Name)

foreach ($t in $tagObjects) {
    $name = $t.Name

    if ($name -eq $newestTag.Name) {
        $report += [pscustomobject]@{ Tag=$name; Action='Keep'; Reason=$newestReason; KeepSubstring=($false) }
        continue # Always keep newest
    }

    if ($keepRegex.IsMatch($name)) {
        $preserved += $name
        $report += [pscustomobject]@{ Tag=$name; Action='Keep'; Reason='KeepSubstring'; KeepSubstring=$true }
        Write-Verbose "Preserve (keep substring): $name"
        continue
    }

    if ($name -match $tagNamePattern) {
        $year  = $Matches[1]
        $monthDay = $Matches[2]
        $month = $monthDay.Substring(0,2)
        $day   = $monthDay.Substring(2,2)
        $encodedDate = [DateTime]::ParseExact("$year-$month-$day", 'yyyy-MM-dd', $null)

        if ($encodedDate -lt $timeFrame) {
            $deletionCandidates += $name
            $report += [pscustomobject]@{ Tag=$name; Action='Delete'; Reason='OlderThanRetention'; KeepSubstring=$false }
        } else {
            $preserved += $name
            $report += [pscustomobject]@{ Tag=$name; Action='Keep'; Reason='WithinRetention'; KeepSubstring=$false }
        }
    }
    else {
        # Non-standard tag naming -> delete (unless newest or keep substring which we already handled)
        $deletionCandidates += $name
        $report += [pscustomobject]@{ Tag=$name; Action='Delete'; Reason='NonStandardPattern'; KeepSubstring=$false }
    }
}

if ($DryRun) {
    Write-Output '--- DRY RUN REPORT ---'
    $report | Sort-Object Tag | Format-Table -AutoSize
    Write-Output ("Preserved tags (computed) {0}: {1}" -f $preserved.Count, ($preserved -join ', '))
    if ($deletionCandidates.Count -eq 0) {
        Write-Output 'No tags would be deleted.'
    } else {
        Write-Output ("Tags that WOULD be deleted ({0}): {1}" -f $deletionCandidates.Count, ($deletionCandidates -join ', '))
    }
    Write-Output 'DRY RUN complete. No changes made.'
    return
}

if ($deletionCandidates.Count -eq 0) {
    Write-Output 'No tags qualified for deletion after applying retention, pattern, and keep rules.'
    Write-Output ("Preserved tags ({0}): {1}" -f $preserved.Count, ($preserved -join ', '))
    return
}

Write-Output ("Preserved tags ({0}): {1}" -f $preserved.Count, ($preserved -join ', '))
Write-Output ("Tags to delete ({0}): {1}" -f $deletionCandidates.Count, ($deletionCandidates -join ', '))

# Final safeguard: ensure we never attempt to delete ALL tags
if ($deletionCandidates.Count -ge $tagObjects.Count) {
    Write-Warning 'Safety check triggered: deletion set includes all tags. Aborting.'
    return
}

foreach ($oldTag in $deletionCandidates) {
    Write-Output "Deleting tag: $oldTag"
    git tag -d $oldTag | Out-Null
    git push origin --delete $oldTag | Out-Null
}

Write-Output 'Tag cleanup complete.'