# Setup Development Environment DevContainer
Write-Host "Post Create Commands for Environment..."

# Note: We skip apt update/upgrade here because:
# 1. The base devcontainer image is already up-to-date
# 2. Third-party repos (like Yarn) may have expired GPG keys causing failures
# 3. Package upgrades during container creation increase build time significantly
# If you need to update packages, do so manually after container creation

# Check if .NET SDK is available
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    Write-Host "Updating .NET workloads..."
    dotnet workload update
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Failed to update .NET workloads. Please check your .NET SDK installation."
    }

    Write-Host "Installing LibMan CLI tool..."
    dotnet tool install -g Microsoft.Web.LibraryManager.Cli
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Failed to install LibMan CLI tool. It may already be installed or there was an error."
    }
} else {
    Write-Error ".NET SDK is not installed or not accessible. Please install the .NET SDK to continue."
    Write-Host "Visit https://dotnet.microsoft.com/download to download the .NET SDK."
    exit 1
}

# Setup git Configurations
git config --global credential.useHttpPath true

# Install Package Manager Support
# Note: .NET SDK (already installed) includes NuGet functionality via 'dotnet' CLI
# The standalone 'nuget' package is not needed for modern .NET development
# If you need to install additional apt packages, run 'sudo apt-get update' first
# because the devcontainer build clears /var/lib/apt/lists/* to reduce image size

# Uncomment the following lines to install npm if Node.js development is required
# sudo apt-get update
# sudo apt install -y npm

# Trust HTTPS developer certificate
# Note: This command requires a desktop environment and user interaction on Linux.
# It will fail in Linux-based DevContainers but works on Windows/macOS hosts.
# On Linux containers, the certificate is generated but cannot be automatically trusted.
# For local development on Linux, manually trust the certificate or use HTTP endpoints.
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    Write-Host "Trusting HTTPS developer certificate..."
    dotnet dev-certs https --trust
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Failed to trust HTTPS developer certificate. This is expected in Linux-based DevContainers."
        Write-Host "The certificate has been generated but cannot be automatically trusted in this environment."
        Write-Host "For local development, you may need to manually trust the certificate or use HTTP endpoints."
    }
} else {
    Write-Warning ".NET SDK is not available. Skipping HTTPS certificate trust step."
}

# Setup Docker development environment (WireMock certificates, .env, etc.)
Write-Host "`n=== Setting up Docker Development Environment ===" -ForegroundColor Cyan

$SetupScript = Join-Path $PSScriptRoot "docker_setup.sh"
if (Test-Path $SetupScript) {
    Write-Host "Running docker_setup.sh to configure development services..."

    # Make the script executable
    chmod +x $SetupScript
    chmod +x (Join-Path $PSScriptRoot "docker_down.sh")
    chmod +x (Join-Path $PSScriptRoot "containers/certs/generate-wiremock-cert.sh")

    # Run the setup script
    bash $SetupScript

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Docker development environment configured successfully." -ForegroundColor Green
    } else {
        Write-Warning "Docker setup completed with warnings. Some services may not be fully configured."
    }
} else {
    Write-Warning "docker_setup.sh not found. Skipping Docker development environment setup."
    Write-Host "You can manually run ./docker_setup.sh to configure development services."
}

Write-Host "`nPost-container setup complete!" -ForegroundColor Green
Write-Host "Run './docker_setup.sh' to start development services if not already running."