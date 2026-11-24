<#
.SYNOPSIS
Samples memory and CPU use for the shared local Toolbox containers.
.DESCRIPTION
Reads container statistics for the common, Azure and AWS Compose projects.
Use Samples and IntervalSeconds to take repeated measurements.
#>
[CmdletBinding()]
param(
    [ValidateSet('docker', 'podman')]
    [string]$ContainerRuntime = 'docker',

    [ValidateRange(1, 3600)]
    [int]$IntervalSeconds = 5,

    [ValidateRange(1, 1000)]
    [int]$Samples = 1
)

$ErrorActionPreference = 'Stop'
$runtime = Get-Command -Name $ContainerRuntime -CommandType Application -ErrorAction Stop |
    Select-Object -First 1
$projects = @('dev_common_shared', 'dev_azure_shared', 'dev_aws_shared')
$labels = @('com.docker.compose.project', 'io.podman.compose.project')

for ($sample = 1; $sample -le $Samples; $sample++) {
    $containers = foreach ($project in $projects) {
        foreach ($label in $labels) {
            & $runtime.Source ps --quiet --filter "label=$label=$project"
            if ($LASTEXITCODE -ne 0) { throw "Could not list containers for $project." }
        }
    }

    $containerIds = @($containers | Sort-Object -Unique)
    if ($containerIds.Count -eq 0) {
        Write-Output 'No shared Toolbox containers are running.'
    } else {
        Write-Output (Get-Date -Format o)
        & $runtime.Source stats --no-stream @containerIds
        if ($LASTEXITCODE -ne 0) { throw 'Could not read shared Toolbox container statistics.' }
    }

    if ($sample -lt $Samples) { Start-Sleep -Seconds $IntervalSeconds }
}
