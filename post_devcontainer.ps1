# Setup Development Environment DevContainer
Write-Host "Post Create Commands for Environment..."

# Update the system
sudo apt update
sudo apt upgrade -y

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
sudo apt install -y nuget
# Uncomment the following line to install npm if Node.js development is required
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

Write-Host "run Setup_project.ps1 to complete the process..."