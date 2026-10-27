# Local development only. Reuse the selected engine's tag before contacting a registry.
function Ensure-ToolboxImage {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Runtime, [Parameter(Mandatory)][string]$Image)
    $PSNativeCommandUseErrorActionPreference = $false
    & $Runtime image inspect $Image *> $null
    if ($LASTEXITCODE -eq 0) { return }
    & $Runtime pull $Image | Out-Host
    if ($LASTEXITCODE -eq 0) { return }

    $normalized = $Image -replace '^docker\.io/', ''
    $registry = 'docker.io'
    if (($normalized -split '/')[0] -match '[.:]' -and $normalized.Contains('/')) {
        $registry = ($normalized -split '/')[0]
        $normalized = $normalized.Substring($registry.Length + 1)
    } elseif (-not $normalized.Contains('/')) { $normalized = "library/$normalized" }
    $mirrors = @{}
    if ($env:TOOLBOX_IMAGE_MIRRORS) {
        foreach ($mapping in $env:TOOLBOX_IMAGE_MIRRORS.Split(',')) {
            $parts = $mapping.Split('=', 2)
            if ($parts.Length -ne 2) { throw 'Image mirrors must use registry=mirror-prefix pairs.' }
            $mirrors[$parts[0]] = $parts[1]
        }
    }
    if (-not $mirrors.ContainsKey($registry)) {
        throw "Cannot pull '$Image'. Configure its approved registry mirror in TOOLBOX_IMAGE_MIRRORS."
    }
    $prefix = $mirrors[$registry].TrimEnd('/')
    if ($prefix -notmatch '^[A-Za-z0-9.-]+(?::[0-9]+)?/[A-Za-z0-9/_-]+$') { throw 'Invalid image mirror prefix.' }
    $fallback = "$prefix/$normalized"
    & $Runtime image inspect $fallback *> $null
    if ($LASTEXITCODE -ne 0) {
        & $Runtime pull $fallback | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Cannot pull '$Image' or its approved registry mirror." }
    }
    & $Runtime tag $fallback $Image | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Cannot tag the verified mirror image as '$Image'." }
}
