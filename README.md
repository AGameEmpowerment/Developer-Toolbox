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

This repository provides a local SQL Server instance via Docker (service name: `mssql`) that the init scripts will use to create the example database.

To initialize the database using the provided compose setup:

1. Ensure your environment contains the required variables (or copy `containers/.env.example` to `containers/.env`) and set `MSSQL_SA_PASSWORD`. The example file ships with a development-only password:

	- `MSSQL_SA_PASSWORD="P@ssword123!"` (development only)

2. The `mssql` service exposes SQL Server on localhost port `10433` (container port 1433). The database initialization script (`containers/mssql/db-init.sql`) creates a database named `ProjectExample` and the standard ASP.NET Core Identity tables.

3. Example connection string for local development (use in `appsettings.json` or your project's secrets store):

```text
Server=127.0.0.1,10433;Database=ProjectExample;User Id=sa;Password=P@ssword123!;TrustServerCertificate=True;
```

> **⚠️ Security Warning**: The default password `P@ssword123!` is provided for **local development environments only** and must **never** be used in production. Keep secrets out of source control and use secure secret management solutions (for example, Azure Key Vault or HashiCorp Vault) for production deployments.

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

### 11. Resource Priority of inclusion in projects from the template repository

The easiest option is to copy everything from this repository directly over into your new project repository. There should be little or no conflicts when doing this, be sure to exclude the `.git` folder. However, if you want to be more selective, here is the recommended priority order for including resources from this repository into your new project repository:

1. **High Priority** (must include):
   - `.github/` folder (GitHub Actions workflows and copilot configuration)
      - `agents/` folder (Copilot configuration Select those configurations relevant to your project)
      - `chatmodes/` folder (Copilot configuration Select those configurations relevant to your project)
      - `collections/` folder (Copilot configuration Select those configurations relevant to your project)
      - `instructions/` folder (Copilot configuration Select those configurations relevant to your project)
      - `prompts/` folder (Copilot configuration Select those configurations relevant to your project)
      - `workflows/copilot-setup-steps.yml` Copilot setup workflow
      - `copilot-instructions.md` GitHub Copilot setup instructions (Based copilot instructions file, has redirect instructions to the `instructions/` folder)
      - `dependabot.yml` for dependency updates
   - `devops/` folder structure for (CI/CD pipelines and scripts)
   - `src/` folder as starting point for application code
   - `.editorconfig` standard coding styles
   - `.gitattributes` file for consistent line endings across environments
   - `.gitignore` file or latest from [github/gitignore](https://github.com/github/gitignore)
   - `LICENSE` file (choose appropriate license for your project)
   - `CODEOWNERS` file (Then update as needed for your team)
   - `README.md` (customize for your project)

2. **Medium Priority** (include as needed):
   - `containers/` folder (Docker and container configurations for local development)
   - `docker_setup.ps1` and `docker_down.ps1` scripts
   - `.vsconfig` file (Visual Studio configuration)
   - `Default-Visual-Studio-Settings.vssettings` file (Visual Studio settings)
   - `setup_docker_container.ps1` script (if using Docker devcontainer setup)
   - `Delete_Old_Git_Tags.ps1` and `Delete_Old_Docker_Tags.ps1` scripts
   - `AUTHORS.md`, `CHANGELOG.md`, `CONTRIBUTING.md` files

3. **Low Priority** (nice to have, include as needed):
   - `.devcontainer/` folder (if using VS Code DevContainers)
   - Any sample application code or configurations that are not relevant to your project
     - Many of these will be found under the `.github/` folder for specific copilot configurations and instructions
     - An Example project under the `src/` folder can also be excluded along with the two example solution files `Edu.Si.Example.sln` and `Edu.Si.Example.slnx`
   - Documentation files that do not pertain to your specific project such as placement holder `README.md` files in subfolders

---

## Summary

This guide provides a clear, step-by-step process for setting up your development environment using the repository's scripts and container files. For further details, see documentation in the `devops/` and `containers/` folders.
