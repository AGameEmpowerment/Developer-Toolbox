<#
.SYNOPSIS
    General-purpose setup script for Node and C# projects.

.DESCRIPTION
    Automatically detects Node (React) projects and C# projects within a directory
    and performs package installation/restoration. Supports JFrog CLI for .NET
    restore if available, otherwise falls back to dotnet restore.

.PARAMETER RootPath
    Root directory to search for projects. Accepts relative or absolute paths.
    Defaults to the script's directory. Searches recursively through all subdirectories.

.PARAMETER UseJFrog
    Use JFrog CLI for .NET package restoration instead of dotnet restore.

.EXAMPLE
    .\run-copilot-setup.ps1 -RootPath ".\src"

.EXAMPLE
    .\run-copilot-setup.ps1 -RootPath "..\MyProject" -UseJFrog

.EXAMPLE
    .\run-copilot-setup.ps1 -RootPath "C:\MyProject"
#>
[CmdletBinding()]
param(
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$RootPath = $PSScriptRoot,

    [Parameter()]
    [switch]$UseJFrog
)

begin {
    $ErrorActionPreference = 'Stop'
    Write-Verbose 'Starting Copilot setup process'
}

process {
    try {
        # Convert relative path to absolute path
        $resolvedPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($RootPath)

        # Validate root path exists
        if (-not (Test-Path -Path $resolvedPath -PathType Container)) {
            $errorRecord = [System.Management.Automation.ErrorRecord]::new(
                [System.IO.DirectoryNotFoundException]::new("Root path not found: $resolvedPath (original: $RootPath)"),
                'RootPathNotFound',
                [System.Management.Automation.ErrorCategory]::ObjectNotFound,
                $RootPath
            )
            $PSCmdlet.ThrowTerminatingError($errorRecord)
        }

        Write-Host "Searching for projects in: $resolvedPath"
        Write-Verbose 'Searching recursively through subdirectories...'
        $nodeProjects = Get-ChildItem -Path $resolved

        # Search for Node/React projects (package.json files)
        Write-Verbose 'Searching for Node projects...'
        $nodeProjects = Get-ChildItem -Path $RootPath -Filter 'package.json' -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -notmatch '^(node_modules|dist|build|coverage|obj|bin)$' }

        if ($nodeProjects) {
            $projectsFound = $true
            Write-Host "`nFound $($nodeProjects.Count) Node project(s)" -ForegroundColor Green

            foreach ($packageJson in $nodeProjects) {
                $projectDir = $packageJson.DirectoryName
                Write-Host "  Installing dependencies in: $projectDir"

                try {
                    # Verify npm is available
                    $npmVersion = npm --version 2>$null
                    if (-not $npmVersion) {
                        Write-Warning "npm not found in PATH. Skipping: $projectDir"
                        continue
                    }

                    Write-Verbose "Using npm version: $npmVersion"
                    $result = npm ci --prefix $projectDir 2>&1

                    if ($LASTEXITCODE -eq 0) {
                        Write-Host "    ✓ Successfully installed dependencies" -ForegroundColor Green
                    } else {
                        Write-Warning "Failed to install dependencies in: $projectDir"
                        Write-Verbose "npm output: $result"
                    }
                } catch {
                    Write-Warning "Error processing Node project at $projectDir : $_"
                }
            }
        } else {
            Write-Host "`nNo Node projects found" -ForegroundColor Yellow
        }

        # Search for C# projects (.csproj files)
        Write-Verbose 'Searching for C# projects recursively...'
        $csharpProjects = Get-ChildItem -Path $resolvedPath -Filter '*.csproj' -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -notmatch '^(obj|bin|packages|node_modules)$' }

        if ($csharpProjects) {
            $projectsFound = $true
            Write-Host "`nFound $($csharpProjects.Count) C# project(s)" -ForegroundColor Green

            # Determine which restore command to use
            $restoreCommand = 'dotnet'
            if ($UseJFrog.IsPresent) {
                $jfVersion = jf --version 2>$null
                if ($jfVersion) {
                    $restoreCommand = 'jf dotnet'
                    Write-Verbose "Using JFrog CLI version: $jfVersion"
                } else {
                    Write-Warning "JFrog CLI not found. Falling back to dotnet restore"
                    $restoreCommand = 'dotnet'
                }
            }

            Write-Host "  Using restore command: $restoreCommand restore"

            foreach ($project in $csharpProjects) {
                Write-Host "  Restoring: $($project.FullName)"

                try {
                    if ($restoreCommand -eq 'jf dotnet') {
                        $result = jf dotnet restore $project.FullName 2>&1
                    } else {
                        $result = dotnet restore $project.FullName 2>&1
                    }

                    if ($LASTEXITCODE -eq 0) {
                        Write-Host "    ✓ Successfully restored packages" -ForegroundColor Green
                    } else {
                        Write-Warning "Failed to restore packages for: $($project.FullName)"
                        Write-Verbose "Restore output: $result"
                    }
                } catch {
                    Write-Warning "Error processing C# project at $($project.FullName): $_"
                }
            }
        } else {
            Write-Host "`nNo C# projects found" -ForegroundColor Yellow
        }

        # Validate at least one project type was found
        if (-not $projectsFound) {
            Write-Warning "No Node or C# projects found in: $resolvedPath"
            Write-Host "Please ensure you're running this script in a directory containing project files." -ForegroundColor Yellow
        } else {
            Write-Host "`nCopilot setup tasks completed successfully." -ForegroundColor Green
        }
    } catch {
        $errorRecord = [System.Management.Automation.ErrorRecord]::new(
            $_.Exception,
            'SetupFailed',
            [System.Management.Automation.ErrorCategory]::NotSpecified,
            $RootPath
        )
        $PSCmdlet.ThrowTerminatingError($errorRecord)
    }
}

end {
    Write-Verbose 'Copilot setup process finished'
}
