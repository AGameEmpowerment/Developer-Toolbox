<#
.SYNOPSIS
Stages the generic sample library output for NuSpec packaging.

.DESCRIPTION
Copies the compiled DeveloperToolbox.Example.Lib DLL and optional symbols into
a local staging directory. This is a local-development template and does not
publish packages or configure package feeds.

.PARAMETER SourcePath
The directory containing the compiled net10.0 library output.

.PARAMETER OutputPath
The directory that will receive the files used by the NuSpec template.

.EXAMPLE
./devops/PrepNuget.ps1 -Verbose

.OUTPUTS
System.IO.FileInfo for each staged file.

.NOTES
Build DeveloperToolbox.Example.slnx in Release mode before running this script.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$SourcePath = (Join-Path $PSScriptRoot '..\src\DeveloperToolbox.Example\DeveloperToolbox.Example.Lib\bin\Release\net10.0'),

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\nugetlibs\net10.0')
)

$ErrorActionPreference = 'Stop'

try {
    $resolvedSourcePath = (Resolve-Path -LiteralPath $SourcePath).Path
    $resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    $sourceFiles = Get-ChildItem -LiteralPath $resolvedSourcePath -File |
        Where-Object { $_.Name -match '^DeveloperToolbox\.Example\.Lib\.(dll|pdb)$' }

    if (-not $sourceFiles) {
        throw "No sample library output was found in '$resolvedSourcePath'."
    }

    if ($PSCmdlet.ShouldProcess($resolvedOutputPath, 'Create the NuGet staging directory')) {
        $null = New-Item -ItemType Directory -Path $resolvedOutputPath -Force
    }

    if (Test-Path -LiteralPath $resolvedOutputPath) {
        Get-ChildItem -LiteralPath $resolvedOutputPath -File |
            Where-Object { $_.Extension -in '.dll', '.pdb' } |
            ForEach-Object {
                if ($PSCmdlet.ShouldProcess($_.FullName, 'Remove the previously staged file')) {
                    Remove-Item -LiteralPath $_.FullName -Force
                }
            }
    }

    foreach ($sourceFile in $sourceFiles) {
        $destinationPath = Join-Path $resolvedOutputPath $sourceFile.Name
        if ($PSCmdlet.ShouldProcess($destinationPath, "Stage '$($sourceFile.Name)'")) {
            Copy-Item -LiteralPath $sourceFile.FullName -Destination $destinationPath -Force
            Get-Item -LiteralPath $destinationPath
        }
    }
} catch {
    $errorRecord = [System.Management.Automation.ErrorRecord]::new(
        $_.Exception,
        'NuGetStagingFailed',
        [System.Management.Automation.ErrorCategory]::InvalidOperation,
        $SourcePath
    )
    $PSCmdlet.ThrowTerminatingError($errorRecord)
}
