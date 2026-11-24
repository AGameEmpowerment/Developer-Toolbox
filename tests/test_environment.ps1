#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'containers/environment.ps1')
$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('toolbox-env-' + [guid]::NewGuid().ToString('N'))
function Assert-EnvironmentTest([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
try {
    New-Item -ItemType Directory -Path $testDirectory | Out-Null
    $file = Join-Path $testDirectory '.env'
    $template = @('MSSQL_SA_PASSWORD="auto"', 'POSTGRES_PASSWORD=''auto'' # generated',
        'export RABBITMQ_DEFAULT_PASS = auto', 'SEQ_STORAGE_SECRETKEY="auto"',
        'ConnectionStrings__Postgres="Password=${POSTGRES_PASSWORD}"',
        'TOOLBOX_CUSTOM="$(do-not-run)"', 'postgres_password="auto"')
    [System.IO.File]::WriteAllText($file, ($template -join "`n"), [System.Text.UTF8Encoding]::new($false))
    $output = Initialize-ToolboxEnvironment -EnvFile $file 6>&1 | Out-String
    $generated = Get-Content -LiteralPath $file -Raw
    foreach ($key in @('MSSQL_SA_PASSWORD', 'POSTGRES_PASSWORD', 'RABBITMQ_DEFAULT_PASS')) {
        $pattern = '(?m)^' + $key + '="(Tb!1[a-f0-9]{64})"\r?$'
        Assert-EnvironmentTest ($generated -match $pattern) 'A managed password was not generated correctly.'
        Assert-EnvironmentTest (-not $output.Contains($matches[1])) 'Generated credentials were printed.'
    }
    foreach ($key in @('SEQ_STORAGE_SECRETKEY')) {
        Assert-EnvironmentTest ($generated -match ('(?m)^' + $key + '="([^"]+)"\r?$')) 'A managed key was missing.'
        Assert-EnvironmentTest ([Convert]::FromBase64String($matches[1]).Length -eq 32) 'A managed key is not 256 bits.'
        Assert-EnvironmentTest (-not $output.Contains($matches[1])) 'Generated keys were printed.'
    }
    Assert-EnvironmentTest ($generated.Contains($template[4])) 'Connection interpolation changed.'
    Assert-EnvironmentTest ($generated.Contains($template[5])) 'Developer script-like text changed.'
    Assert-EnvironmentTest ($generated.Contains($template[6])) 'Case-sensitive dotenv key changed.'
    Initialize-ToolboxEnvironment -EnvFile $file
    Assert-EnvironmentTest ((Get-Content -LiteralPath $file -Raw) -ceq $generated) 'Repeated setup rotated credentials.'
    $explicit = "POSTGRES_PASSWORD=auto`nexport POSTGRES_PASSWORD = 'developer custom password'`nSEQ_STORAGE_SECRETKEY='developer-key'`nMSSQL_SA_PASSWORD='AUTO'"
    [System.IO.File]::WriteAllText($file, $explicit)
    Initialize-ToolboxEnvironment -EnvFile $file
    Assert-EnvironmentTest ((Get-Content -LiteralPath $file -Raw) -ceq $explicit) 'Last explicit assignment was overwritten.'
    $literalHashes = @('POSTGRES_PASSWORD=auto#developer-password',
        'MSSQL_SA_PASSWORD=auto#not-a-comment', 'RABBITMQ_DEFAULT_PASS=auto#literal',
        'WIREMOCK_KEYSTORE_PASSWORD=auto#local-keystore') -join "`n"
    [System.IO.File]::WriteAllText($file, $literalHashes)
    Initialize-ToolboxEnvironment -EnvFile $file
    Assert-EnvironmentTest (([System.IO.File]::ReadAllText($file)) -ceq $literalHashes) 'An embedded hash in an explicit credential was mistaken for a placeholder comment.'
    Assert-EnvironmentTest (-not $script:ToolboxGeneratedWireMockPassword) 'An explicit keystore password triggered certificate regeneration.'
    $leadingHashes = $literalHashes.Replace('=auto#', '=#')
    [System.IO.File]::WriteAllText($file, $leadingHashes)
    Initialize-ToolboxEnvironment -EnvFile $file
    Assert-EnvironmentTest (([System.IO.File]::ReadAllText($file)) -ceq $leadingHashes) 'A leading hash in an explicit credential was mistaken for a blank value.'
    Assert-EnvironmentTest (-not $script:ToolboxGeneratedWireMockPassword) 'A leading-hash keystore password triggered certificate regeneration.'
    $unicode = 'TOOLBOX_CUSTOM="' + [char]0x00e9 + [char]0x4e2d + '"'
    [System.IO.File]::WriteAllText($file, $unicode + "`nSEQ_STORAGE_SECRETKEY=`"auto`"", [System.Text.UTF8Encoding]::new($false))
    Initialize-ToolboxEnvironment -EnvFile $file
    Assert-EnvironmentTest (([System.IO.File]::ReadAllText($file)).Contains($unicode)) 'UTF-8 developer values changed.'
    $other = Join-Path $testDirectory 'other.env'
    Set-Content -LiteralPath $other -Value 'SEQ_STORAGE_SECRETKEY="auto"'
    Initialize-ToolboxEnvironment -EnvFile $other
    Assert-EnvironmentTest (-not $generated.Contains((Get-Content $other | Select-Object -First 1))) 'Separate developers received identical keys.'
    Assert-EnvironmentTest (@(Get-ChildItem -LiteralPath $testDirectory -Filter '.env-generated-*').Count -eq 0) 'Temporary secrets remained.'
    Write-Host 'Environment generation tests passed.'
} finally {
    if (Test-Path -LiteralPath $testDirectory) { Remove-Item -LiteralPath $testDirectory -Recurse -Force }
}
