# Setup Development Environment DevContainer
Write-Host "Post Create Commands for Environment..."

# Update the system
sudo apt update
sudo apt upgrade -y

dotnet workload update
dotnet tool install -g Microsoft.Web.LibraryManager.Cli

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
dotnet dev-certs https --trust

Write-Host "run  Setup_project.ps1  to complete the process..."