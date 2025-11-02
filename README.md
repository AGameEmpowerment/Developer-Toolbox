# Developer Toolbox: Environment Setup Guide

A standardized toolkit like this delivers value for both management and developers: it ensures consistent, compliant environments that reduce onboarding time, minimize configuration errors, and support reliable software delivery. Management benefits from improved governance, easier tracking, and integrated security, while developers gain faster setup, fewer environment-related bugs, and streamlined workflows. This alignment accelerates productivity and quality across the team.

## Quick Start: Local Development Environment

Follow these steps to set up your development environment using the repository's project files. This guide covers both local and containerized workflows.

---

### 1. Prerequisites

- **Windows 10/11** (recommended)
- **PowerShell (latest)**
- **.NET 8/9 SDK**
- **Visual Studio 2022 (any edition)**
- **Git Client**
- **Docker Desktop** (for container workflows)
- **SQL Server Instance** (local or containerized)

Optional but recommended:
- Visual Studio Code
- SQL Server Management Studio
- Azure CLI & Functions Core Tools
- Postman, Bruno, LINQPad, JetBrains Toolbox, Notepad++

---

### 2. Clone the Repository

```pwsh
git clone https://github.com/AGameEmpowerment/Developer-Toolbox.git
cd Developer-Toolbox
```

---

### 3. Configure PowerShell Script Execution

Run as administrator:

```pwsh
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope LocalMachine
Set-ExecutionPolicy -ExecutionPolicy Unrestricted -Scope CurrentUser
```

---

### 4. Install Required Tools (Windows)

Use `winget` to install dependencies:

```pwsh
winget install Microsoft.DotNet.SDK.8
winget install Microsoft.DotNet.SDK.9
winget install Microsoft.PowerShell
winget install Git.Git
winget install Docker.DockerDesktop
winget install Microsoft.VisualStudio.2022.Community
winget install Microsoft.SQLServer.2022.Express
```

---

### 5. Initialize Local Development Environment

Run the setup script to start Docker containers and dependencies:

```pwsh
./docker_setup.ps1
```

This will:
- Build and start all containers defined in `containers/docker-compose-common.yml`
- Set up SQL Server, Service Bus, smtp4dev, and other local services
- Prepare example files and mappings for local use

To stop containers:
```pwsh
./docker_down.ps1
```

---

### 6. Database Initialization

Open the solution in Visual Studio and build to restore NuGet packages.

To initialize the database:
1. Build and publish the `Database.Example` project using the profile `StartupExample.Docker.publish.xml` (default password: `P@ssword123!`).
2. Connection strings are managed in `appsettings.json`.

> **⚠️ Security Warning**: The default password `P@ssword123!` is provided for **local development environments only** and must **never** be used in production. Always use strong, unique passwords and secure credential management solutions (e.g., Azure Key Vault, HashiCorp Vault) for production deployments.

---

### 7. Running Applications

After building and initializing the database, you can run any application in the solution as needed.

---

### 8. DevContainer Setup (VS Code)

For reproducible environments and easy onboarding:
1. Install Docker Desktop and Visual Studio Code.
2. Install the VS Code extension: `ms-vscode-remote.remote-containers`.
3. Open the project folder in VS Code and select "Reopen in Container".
4. The devcontainer will build and start all required services automatically.

---

### 9. Troubleshooting & Tips

- If containers fail to start, ensure Docker Desktop is running and you have sufficient resources.
- For database issues, check SQL Server logs in the container or local instance.
- Use `Delete_Old_Git_Tags.ps1` to clean up old Git Repo tags if needed.
- Use `Delete_Old_Docker_Tags.ps1` to clean up old Docker images/tags if needed.
- For advanced configuration, review files in `containers/`, `devops/`, and `terraform/`.

---

### 10. Additional Resources

- [Authors](AUTHORS.md)
- [ChangeLog](CHANGELOG.md)
- [Contributing](CONTRIBUTING.md)
- [DevContainer Documentation](https://code.visualstudio.com/docs/devcontainers/containers)

---

## Summary

This guide provides a clear, step-by-step process for setting up your development environment using the repository's scripts and container files. For further details, see documentation in the `devops/` and `containers/` folders.
