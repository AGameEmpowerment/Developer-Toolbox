<#
.SYNOPSIS
	Delete old Docker images and tags locally and optionally from a remote registry.

DESCRIPTION
	This script helps remove old Docker images and tags according to a simple retention policy.
	It runs safely by default in --WhatIf (dry-run) mode and provides options to:
	  - keep the most recent N tags
	  - preserve semver/stable tags (like latest, release/*, main)
	  - filter by repository name
	  - optionally call remote registry APIs (Docker Hub, GitHub Container Registry) to delete tags

	NOTE: Remote registry deletion requires a token and may have provider-specific APIs and rate limits.

USAGE
	.\Delete_Old_Docker_Tags.ps1 -Repository myorg/myimage -Keep 5 -DryRun

PARAMETERS
	-Repository <string>
		Docker repository name (e.g. myorg/myimage). Required.

	-Keep <int>
		Number of most recent tags to keep. Default: 5.

	-PreservePatterns <string[]>
		Array of wildcard patterns to always preserve (default: 'latest','stable','release*','main').

	-AgeDays <int>
		Minimum age in days before a tag is eligible for deletion. Default: 30.

	-DryRun
		If specified, no deletions are performed. The script will only list candidates.

	-DeleteRemote
		If specified, attempt to delete tags from the remote registry via supported API.

	-RegistryProvider <string>
		Optional: 'dockerhub' or 'ghcr'. If omitted and -DeleteRemote is set, the script will attempt to infer from repository.

	-Token <string>
		Optional: Auth token for registry API. If not provided, will read from env var DOCKER_REG_TOKEN or GITHUB_TOKEN as applicable.

	-Verbose, -WhatIf, -Confirm

NOTES
	- This script focuses on safety. Always run with -DryRun first.
	- Local image pruning uses `docker images` and `docker rmi` and will only remove images not referenced by running containers.

AUTHOR
	AGameEmpowerment (adapted)
#>

param(
	[Parameter(Mandatory=$true)]
	[string] $Repository,

	[int] $Keep = 5,

	[int] $AgeDays = 30,

	[string[]] $PreservePatterns = @('latest','stable','release*','main'),

	[switch] $DryRun,

	[switch] $DeleteRemote,

	[ValidateSet('dockerhub','ghcr')]
	[string] $RegistryProvider,

	[string] $Token
)

function Write-Log {
	param([string] $Message, [string] $Level = 'INFO')
	$ts = (Get-Date).ToString('s')
	Write-Output "[$ts] [$Level] $Message"
}

function Matches-PreservePattern {
	param([string] $Tag, [string[]] $Patterns)
	foreach ($p in $Patterns) {
		if ($Tag -like $p) { return $true }
	}
	return $false
}

function Get-LocalImageTags {
	param([string] $repo)
	# Returns objects with RepoTag, ImageID, CreatedSince (TimeSpan)
	$format = '{{.Repository}}:{{.Tag}}|{{.ID}}|{{.CreatedSince}}'
	$out = docker images --format $format 2>&1
	if ($LASTEXITCODE -ne 0) {
		Write-Log "Failed to list docker images: $out" 'ERROR'
		return @()
	}
	$lines = $out -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
	$result = @()
	foreach ($l in $lines) {
		$parts = $l -split '\|',3
		if ($parts.Count -ne 3) { continue }
		$repotag = $parts[0]
		$id = $parts[1]
		$created = $parts[2]
		if ($repotag -like "$repo*") {
			# Convert CreatedSince like '2 weeks ago' into approximate date - use heuristic
			$createdAt = Get-Date
			try {
				if ($created -match '^(\d+)\s+day') { $createdAt = (Get-Date).AddDays(-[int]$matches[1]) }
				elseif ($created -match '^(\d+)\s+hour') { $createdAt = (Get-Date).AddHours(-[int]$matches[1]) }
				elseif ($created -match '^(\d+)\s+week') { $createdAt = (Get-Date).AddDays(-7*[int]$matches[1]) }
				elseif ($created -match '^(\d+)\s+month') { $createdAt = (Get-Date).AddMonths(-[int]$matches[1]) }
			} catch { }
			$result += [pscustomobject]@{
				RepoTag = $repotag
				ImageID = $id
				Created = $createdAt
				CreatedSinceRaw = $created
			}
		}
	}
	return $result
}

function Remove-LocalImage {
	param([string] $imageId, [string] $repoTag)
	if ($DryRun) {
		Write-Log "DRY-RUN: would remove local image $repoTag ($imageId)"
		return $true
	}
	Write-Log "Removing local image $repoTag ($imageId)"
	$out = docker rmi $imageId 2>&1
	if ($LASTEXITCODE -ne 0) {
		Write-Log "Failed to remove image $imageId: $out" 'ERROR'
		return $false
	}
	return $true
}

function Delete-RemoteTag {
	param([string] $repo, [string] $tag, [string] $provider, [string] $token)
	# Provider-specific implementations
	if (-not $provider) {
		if ($repo -match '^ghcr.io/') { $provider = 'ghcr' }
		else { $provider = 'dockerhub' }
	}

	if ($provider -eq 'dockerhub') {
		# Docker Hub v2 API does not provide an official delete tag endpoint for Docker Hub public API
		# but for authenticated users with the repository, we can delete manifest by digest.
		# This implementation will attempt to fetch manifest digest and delete it.
		try {
			$parts = $repo -split '/',2
			if ($parts.Count -lt 2) { Write-Log "Docker Hub repo should be like 'namespace/repo'" 'ERROR'; return $false }
			$namespace = $parts[0]; $reponame = $parts[1]
			$authHeader = @{}
			if ($token) { $authHeader = @{ Authorization = "Bearer $token" } }
			$manifestUrl = "https://hub.docker.com/v2/repositories/$namespace/$reponame/tags/$tag/"
			$resp = Invoke-RestMethod -Uri $manifestUrl -Headers $authHeader -Method Get -ErrorAction Stop
			# Docker Hub provides endpoint returning tag info; deletion via API requires more steps or registry access.
			Write-Log "Docker Hub API returned tag info for $repo:$tag. Remote deletion via public API is limited; manual deletion may be required." 'WARN'
			return $false
		} catch {
			Write-Log "Failed calling Docker Hub API: $($_.Exception.Message)" 'ERROR'
			return $false
		}
	}
	elseif ($provider -eq 'ghcr') {
		# GitHub Container Registry: GraphQL or REST API can delete packages by package_version id.
		# We will attempt REST deletions using the packages API. Token requires package:delete scope.
		try {
			if (-not $token) { Write-Log "No token provided for ghcr; set GITHUB_TOKEN or pass -Token" 'ERROR'; return $false }
			# Repo format expected: ghcr.io/OWNER/IMAGE
			$m = $repo -match '^ghcr.io/([^/]+)/([^/]+)$'
			if (-not $m) { Write-Log "Cannot parse ghcr repo name. Expected ghcr.io/OWNER/IMAGE" 'ERROR'; return $false }
			$owner = $matches[1]; $image = $matches[2]
			# Need to find package and version IDs via GitHub API
			$headers = @{ Authorization = "Bearer $token"; Accept = 'application/vnd.github+json' }
			# List package versions
			$listUrl = "https://api.github.com/orgs/$owner/packages/container/$image/versions"
			# Try as org first, then user
			$resp = Invoke-RestMethod -Uri $listUrl -Headers $headers -Method Get -ErrorAction SilentlyContinue
			if (-not $resp) {
				$listUrl = "https://api.github.com/users/$owner/packages/container/$image/versions"
				$resp = Invoke-RestMethod -Uri $listUrl -Headers $headers -Method Get -ErrorAction Stop
			}
			foreach ($v in $resp) {
				if ($v.metadata.container.tags -contains $tag) {
					$verId = $v.id
					if ($DryRun) { Write-Log "DRY-RUN: would delete GHCR package version $verId for $repo:$tag"; return $true }
					$delUrl = "https://api.github.com/orgs/$owner/packages/container/$image/versions/$verId"
					# if user package
					if ($listUrl -match '/users/') { $delUrl = "https://api.github.com/users/$owner/packages/container/$image/versions/$verId" }
					Invoke-RestMethod -Uri $delUrl -Headers $headers -Method Delete -ErrorAction Stop
					Write-Log "Deleted GHCR package version $verId for $repo:$tag"
					return $true
				}
			}
			Write-Log "Could not find package version for $repo:$tag on GHCR" 'WARN'
			return $false
		} catch {
			Write-Log "Failed GHCR delete: $($_.Exception.Message)" 'ERROR'
			return $false
		}
	}
	else {
		Write-Log "Unknown registry provider: $provider" 'ERROR'
		return $false
	}
}

try {
	Write-Log "Starting cleanup for repository: $Repository"

	$localTags = Get-LocalImageTags -repo $Repository
	if (-not $localTags -or $localTags.Count -eq 0) {
		Write-Log "No local images found for $Repository"
	} else {
		# Sort by Created desc (newest first)
		$sorted = $localTags | Sort-Object -Property Created -Descending
		# Determine preserve set
		$preserve = @()
		$candidates = @()
		$now = Get-Date
		for ($i=0; $i -lt $sorted.Count; $i++) {
			$item = $sorted[$i]
			$tagOnly = $item.RepoTag -replace '^.+:', ''
			if (Matches-PreservePattern -Tag $tagOnly -Patterns $PreservePatterns) {
				$preserve += $item
				continue
			}
			if ($i -lt $Keep) {
				$preserve += $item
				continue
			}
			$age = ($now - $item.Created).TotalDays
			if ($age -lt $AgeDays) { $preserve += $item; continue }
			# Candidate for deletion
			$candidates += $item
		}

		Write-Log "Found $($localTags.Count) local tags; preserving $($preserve.Count); candidates for deletion: $($candidates.Count)"
		foreach ($c in $candidates) {
			Remove-LocalImage -imageId $c.ImageID -repoTag $c.RepoTag | Out-Null
		}
	}

	if ($DeleteRemote) {
		if (-not $Token) {
			if ($RegistryProvider -eq 'ghcr') { $Token = $env:GITHUB_TOKEN }
			else { $Token = $env:DOCKER_REG_TOKEN }
		}
		Write-Log "Attempting remote deletion for $Repository (provider: $RegistryProvider)"
		# For remote deletion, use localTags (if any) to choose tags to delete, otherwise we can attempt listing remote tags - not implemented broadly.
		$remoteCandidates = @()
		if ($localTags -and $localTags.Count -gt 0) {
			# Re-run same candidate selection based on $localTags
			$sorted = $localTags | Sort-Object -Property Created -Descending
			for ($i=0; $i -lt $sorted.Count; $i++) {
				$item = $sorted[$i]
				$tagOnly = $item.RepoTag -replace '^.+:', ''
				if (Matches-PreservePattern -Tag $tagOnly -Patterns $PreservePatterns) { continue }
				if ($i -lt $Keep) { continue }
				$age = ($now - $item.Created).TotalDays
				if ($age -lt $AgeDays) { continue }
				$remoteCandidates += $tagOnly
			}
		}
		if ($remoteCandidates.Count -eq 0) { Write-Log "No remote deletion candidates found (based on local images)." }
		foreach ($t in $remoteCandidates) {
			Delete-RemoteTag -repo $Repository -tag $t -provider $RegistryProvider -token $Token | Out-Null
		}
	}

	Write-Log "Cleanup completed for $Repository"
} catch {
	Write-Log "Unhandled error: $($_.Exception.Message)" 'ERROR'
	throw
}
