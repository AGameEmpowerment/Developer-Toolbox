# Dev Container post-create hook.
# Prepares the toolchain inside the container without restoring credentialed packages or
# starting the local emulator stack. Every step after the .NET SDK check is non-fatal so a
# Codespace can finish provisioning when one optional download is unavailable.
Write-Host "Post Create Commands for Environment..."

# Skip apt update/upgrade here. The base image is current, third-party repositories can have
# expired signing keys, and package upgrades make container creation significantly slower.
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error ".NET SDK is not installed or accessible. The base image should provide .NET 10."
    Write-Host "Visit https://dotnet.microsoft.com/download to download the .NET SDK."
    exit 1
}

Write-Host "Installing the Aspire project templates..."
dotnet new install Aspire.ProjectTemplates --force
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Failed to install the Aspire project templates. Install them later if this project uses Aspire."
}

Write-Host "Updating .NET workloads..."
dotnet workload update
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Failed to update .NET workloads. Check the installed .NET SDKs and network access."
}

Write-Host "Installing LibMan CLI tool..."
$globalTools = dotnet tool list --global
if ($globalTools -match '(?im)^microsoft\.web\.librarymanager\.cli\s') {
    dotnet tool update --global Microsoft.Web.LibraryManager.Cli
} else {
    dotnet tool install --global Microsoft.Web.LibraryManager.Cli
}
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Failed to install or update LibMan CLI. Install it later if a sample requires LibMan."
}

git config --global credential.useHttpPath true

# `dotnet dev-certs https` creates the developer certificate. `--trust` needs a desktop trust
# store and can fail inside Linux containers; the certificate still exists for local HTTPS use.
Write-Host "Creating the HTTPS developer certificate..."
dotnet dev-certs https
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Failed to create the HTTPS developer certificate."
} else {
    dotnet dev-certs https --trust 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Certificate created but not trusted; that is expected in Linux-based dev containers."
    }
}

Write-Host "`nPost-container setup complete!" -ForegroundColor Green
Write-Host @"

Next steps:
  1. Build:            dotnet build
  2. Optional stack:  ./.devcontainer/start_developer_toolkit.ps1
  3. Run tests:        dotnet test
"@
