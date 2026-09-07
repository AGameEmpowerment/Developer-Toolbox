#Requires -Version 5.1
<#
.SYNOPSIS
Installs the developer-toolbox bootstrap files into another Git repository.

.DESCRIPTION
Validates that TargetRepositoryPath is the root of a non-bare Git worktree, then
copies Toolbox launchers, devcontainer support, the shared repository bridge
skill, and supporting scripts into that repository. Existing target files and
directories are always preserved.

.PARAMETER TargetRepositoryPath
The root directory of the Git repository that will receive the bootstrap files.

.EXAMPLE
./Install-ToolboxSetup.ps1 -TargetRepositoryPath C:\Code\Projects\MyProject

.OUTPUTS
None.

.NOTES
Run this script from an Developer-Toolbox checkout. Git must be available on
PATH. The target must be the repository root, not a subdirectory inside it.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Medium")]
param(
    [Parameter(Position = 0)]
    [string]$TargetRepositoryPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($TargetRepositoryPath)) {
    Write-Output "Usage: ./Install-ToolboxSetup.ps1 -TargetRepositoryPath <repository-root>"
    Write-Output ""
    Write-Output "Example:"
    Write-Output "  ./Install-ToolboxSetup.ps1 -TargetRepositoryPath C:\Code\Projects\MyProject"
    Write-Output ""
    [Console]::Error.WriteLine("error: TargetRepositoryPath is required.")
    exit 2
}

function Get-NormalizedPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    return [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
}

function Test-PathEntry {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        return $true
    }

    $parentPath = Split-Path -Parent $Path
    $entryName = Split-Path -Leaf $Path
    if (-not $parentPath -or -not (Test-Path -LiteralPath $parentPath -PathType Container)) {
        return $false
    }

    $comparison = if ($env:OS -eq "Windows_NT") {
        [System.StringComparison]::OrdinalIgnoreCase
    } else {
        [System.StringComparison]::Ordinal
    }
    foreach ($entry in Get-ChildItem -LiteralPath $parentPath -Force -ErrorAction SilentlyContinue) {
        if ($entry.Name.Equals($entryName, $comparison)) {
            return $true
        }
    }

    return $false
}

function Invoke-GitText {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryPath,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    try {
        $result = & git -C $RepositoryPath @Arguments 2>$null
        if ($LASTEXITCODE -ne 0) {
            return $null
        }
        return ($result | Out-String).Trim()
    } catch {
        return $null
    }
}

function Get-ProjectDevContainerContent {
    param(
        [Parameter(Mandatory)]
        [string]$SourcePath,

        [Parameter(Mandatory)]
        [string]$ProjectName
    )

    $sourceContent = [System.IO.File]::ReadAllText($SourcePath)
    $jsonProjectName = ConvertTo-Json -InputObject $ProjectName -Compress
    $namePattern = [regex]::new('(?m)^(\s*)"name"\s*:\s*"[^"]*"\s*,')
    if (-not $namePattern.IsMatch($sourceContent)) {
        throw "Devcontainer template '$SourcePath' does not contain a name property."
    }

    return $namePattern.Replace(
        $sourceContent,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            return "$($match.Groups[1].Value)`"name`": $jsonProjectName,"
        },
        1
    )
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git is required but was not found on PATH."
}

if (-not (Test-Path -LiteralPath $TargetRepositoryPath -PathType Container)) {
    throw "Target repository path '$TargetRepositoryPath' does not exist or is not a directory."
}

$targetPath = Get-NormalizedPath (Resolve-Path -LiteralPath $TargetRepositoryPath).Path
$isWorkTree = Invoke-GitText -RepositoryPath $targetPath -Arguments @("rev-parse", "--is-inside-work-tree")
if ($isWorkTree -ne "true") {
    throw "Target '$targetPath' is not a Git worktree."
}

$repositoryRootText = Invoke-GitText -RepositoryPath $targetPath -Arguments @("rev-parse", "--show-toplevel")
if (-not $repositoryRootText) {
    throw "Git could not determine the repository root for '$targetPath'."
}

$repositoryRoot = Get-NormalizedPath $repositoryRootText
if ($targetPath -ne $repositoryRoot) {
    throw "Target '$targetPath' is inside a Git repository but is not its root. Use '$repositoryRoot' instead."
}
$projectName = Split-Path -Leaf $repositoryRoot

$manifestPath = Join-Path $PSScriptRoot "setup/toolbox-bootstrap-files.txt"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Toolbox bootstrap manifest '$manifestPath' was not found."
}

$bootstrapFiles = [System.Collections.Generic.List[object]]::new()
$manifestEntries = Get-Content -LiteralPath $manifestPath |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and -not $_.StartsWith("#") }

foreach ($manifestEntry in $manifestEntries) {
    $sourcePath = Join-Path $PSScriptRoot $manifestEntry
    if (-not (Test-Path -LiteralPath $sourcePath) -and $manifestEntry -notmatch '[/\\]') {
        $disabledTemplatePath = Join-Path $PSScriptRoot "setup/$($manifestEntry)_"
        if (Test-Path -LiteralPath $disabledTemplatePath -PathType Leaf) {
            $sourcePath = $disabledTemplatePath
        }
    }

    if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
        $bootstrapFiles.Add([PSCustomObject]@{
                SourcePath      = $sourcePath
                RelativePath    = $manifestEntry
                ExpectedContent = $null
            })
        continue
    }

    if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
        throw "Toolbox bootstrap source '$sourcePath' was not found."
    }

    $targetEntryPath = Join-Path $repositoryRoot $manifestEntry
    if (Test-PathEntry -Path $targetEntryPath) {
        Write-Verbose "Preserving existing target entry: $targetEntryPath"
        continue
    }

    foreach ($sourceFile in Get-ChildItem -LiteralPath $sourcePath -File -Recurse) {
        $childRelativePath = $sourceFile.FullName.Substring($sourcePath.Length).TrimStart(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar
        )
        $relativePath = Join-Path $manifestEntry $childRelativePath
        $portableRelativePath = $relativePath.Replace("\", "/")
        $expectedContent = if ($portableRelativePath -eq ".devcontainer/devcontainer.json") {
            Get-ProjectDevContainerContent -SourcePath $sourceFile.FullName -ProjectName $projectName
        } else {
            $null
        }
        $bootstrapFiles.Add([PSCustomObject]@{
                SourcePath      = $sourceFile.FullName
                RelativePath    = $relativePath
                ExpectedContent = $expectedContent
            })
    }
}

foreach ($bootstrapFile in $bootstrapFiles) {
    $targetFile = Join-Path $repositoryRoot $bootstrapFile.RelativePath

    if (Test-PathEntry -Path $targetFile) {
        Write-Verbose "Preserving existing target: $targetFile"
        continue
    }

    if ($PSCmdlet.ShouldProcess($targetFile, "Install Toolbox bootstrap file")) {
        $targetDirectory = Split-Path -Parent $targetFile
        if (-not (Test-Path -LiteralPath $targetDirectory -PathType Container)) {
            New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
        }
        if ($null -ne $bootstrapFile.ExpectedContent) {
            $utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
            [System.IO.File]::WriteAllText($targetFile, $bootstrapFile.ExpectedContent, $utf8WithoutBom)
        } else {
            Copy-Item -LiteralPath $bootstrapFile.SourcePath -Destination $targetFile -Force
        }
        if ($env:OS -ne "Windows_NT" -and $targetFile.EndsWith(".sh", [System.StringComparison]::OrdinalIgnoreCase)) {
            & chmod u+x -- $targetFile
            if ($LASTEXITCODE -ne 0) {
                throw "Unable to make Bash launcher '$targetFile' executable."
            }
        }
        Write-Output "Installed $targetFile"
    }
}

Write-Output "Toolbox bootstrap files are ready in '$repositoryRoot'."
Write-Output "Primary AI skill: toolbox-repository-bridge"
Write-Output "Run '.\setup_toolbox.ps1' or 'bash ./setup_toolbox.sh' from the target repository."

