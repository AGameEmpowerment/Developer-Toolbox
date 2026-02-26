# Developer Toolbox: Project Template and Environment Setup

This repository is a starter template for building developer-ready environments and baseline project scaffolding. It is intentionally biased toward Microsoft-centric, Azure-first workloads (ASP.NET, Azure Functions, SQL Server, Terraform, Docker, PowerShell) while still supporting Node.js/React and general polyglot tooling. It is best aligned with:

- .NET and ASP.NET Core services and APIs
- Azure-hosted solutions (Azure CLI, Azure Developer CLI, Bicep)
- Containerized local dev dependencies (SQL Server, Service Bus, SMTP, WireMock)
- CI/CD with Azure DevOps or GitHub workflows
- Infrastructure-as-Code with Terraform

If your project is not Azure/.NET-centric, you can still use this template, but you should selectively remove tooling, containers, and instructions that do not apply.

## What this template includes

- Dev Container configuration with common workloads: .NET 8/9, Node.js LTS, Azure CLI, Azure Developer CLI, Docker, Terraform, PowerShell, Python, Java (minimal), GitHub CLI, and a curated VS Code extension pack.
- Containerized local dependencies via Docker Compose.
- DevOps scaffolding, pipeline placeholders, and manifests.
- Default repository standards (CODEOWNERS, CONTRIBUTING, CHANGELOG, LICENSE, .editorconfig, .gitattributes, .gitignore).

## Choose your setup path

Use one of the options below based on your environment and preferences.

Install command references:

- [INSTALL-WINDOWS-WINGET.md](INSTALL-WINDOWS-WINGET.md) – Windows-only winget commands for required and optional tools.
- [INSTALL-LINUX-APT.md](INSTALL-LINUX-APT.md) – Linux-only apt and npm commands for required and optional tools.

### Option A: Dev Containers (recommended)

Use this if you want a consistent, preconfigured environment without installing every tool locally.

1. Install Docker Desktop.
2. Install Visual Studio Code and the Dev Containers extension.
3. Open this repository in VS Code and select “Reopen in Container.”
4. The container will run post-create setup and install the tools described in the dev container configuration.

This path follows the workloads defined in .devcontainer/devcontainer.json and includes Azure, .NET, Node.js, Terraform, and Docker tooling out of the box.

### Option B: Local setup on Windows (winget)

This is the fastest local setup for Windows 10/11.

1. Clone the repository.
2. Install tools using winget:
    - .NET SDK 8, 9, 10
    - OpenJDK
    - PowerShell
    - Git
    - Docker Desktop
    - Visual Studio 2022
    - SQL Server Express (optional if using containers)

3. Start the containerized dependencies:
    - Run docker_setup.ps1

4. Stop containers when finished:
    - Run docker_down.ps1

### Option C: Local setup on Linux (apt)

Use this if you want native tooling on Linux without Dev Containers.

1. Install prerequisites using apt:
    - Git
    - Docker Engine / Docker Compose
    - PowerShell
    - .NET SDK 8, 9, 10
    - OpenJDK
    - Node.js LTS (optional)

2. Start the containerized dependencies:
    - Run docker_setup.sh

3. Stop containers when finished:
    - Run docker_down.sh

### Option D: Node.js-focused setup (npm)

Use this if you only need frontend tooling or Node.js-based automation.

1. Install Node.js LTS (nvm or system package manager).
2. Install project dependencies with npm.
3. Run your Node.js workflow locally.
4. Start containerized dependencies only if needed.

## Database and local services

The default Docker Compose setup runs a local SQL Server instance and other supporting services. The SQL Server container exposes localhost port 10433 and initializes a database named ProjectExample.

WireMock HTTPS certificate generation uses `keytool`, which is provided by a Java Development Kit (JDK). If you plan to generate WireMock certificates locally, install OpenJDK (recommended) and ensure `keytool` is available on your PATH.

Use the following example connection string for local development:

Server=127.0.0.1,10433;Database=ProjectExample;User Id=sa;Password=P@ssword123!;TrustServerCertificate=True;

Security note: the provided password is for local development only. Do not reuse it in production. Store secrets in your secret manager or environment configuration.

## How to customize this template for your project

Use this checklist to adapt the template for your own repository.

1. Update the root README and project metadata files.
2. Keep or remove containers based on what your app needs.
3. Keep or remove DevOps scaffolding based on your CI/CD platform.
4. Decide whether to use Dev Containers or local tool installation.
5. Update or replace the LICENSE and CODEOWNERS for your organization.
6. Remove example content you do not want to maintain.

Recommended priority for copying into a new repository:

- High priority:
    - .github (Copilot instructions, workflows, and repo automation)
    - devops (pipelines, manifests, and structure)
    - src (starting point for application code)
    - .editorconfig, .gitattributes, .gitignore
    - README, LICENSE, CODEOWNERS
- Medium priority:
    - containers and Docker scripts
    - Visual Studio settings files
    - authoring and contributing docs
- Low priority:
    - example content or sample solutions you do not need

## Dev Container workload summary

The dev container is configured for the following workloads:

- .NET SDK 8/9 and wasm-tools workload
- Node.js LTS with npm, yarn, and pnpm
- Azure CLI, Azure Developer CLI, and Bicep
- Docker-in-Docker for container workflows
- Terraform and TFLint
- PowerShell, Python, Git, GitHub CLI
- Java 21 (minimal install for tooling)

If you remove any of these tools from your project, update .devcontainer/devcontainer.json accordingly.

## Documentation map

Use the links below to find focused documentation in this repository. Each link includes a one-sentence description of what the document is for.

- [.github/readme.md](.github/readme.md) – Repository automation, Copilot configuration, and GitHub-specific guidance.
- [containers/readme.md](containers/readme.md) – Docker Compose services and local dependency containers.
- [containers/certs/readme.md](containers/certs/readme.md) – Certificate setup for HTTPS and WireMock scenarios.
- [containers/extensions/readme.md](containers/extensions/readme.md) – VS Code extensions copied into container images.
- [containers/mappings/readme.md](containers/mappings/readme.md) – Example mappings used by local container services.
- [containers/\_\_files/readme.md](containers/__files/readme.md) – Example files used by local container services.
- [devops/readme.md](devops/readme.md) – DevOps folder overview and how to use it in CI/CD.
- [devops/manifest/readme.md](devops/manifest/readme.md) – Manifest templates and conventions for release artifacts.
- [devops/pipelines/readme.md](devops/pipelines/readme.md) – CI/CD pipeline templates and conventions.
- [devops/terraform/readme.md](devops/terraform/readme.md) – Terraform layout and infrastructure guidance.
- [src/readme.md](src/readme.md) – Application source layout expectations and starter guidance.

## Troubleshooting

- Ensure Docker Desktop or Docker Engine is running before starting containers.
- If SQL Server does not start, check container logs and confirm the environment variables in containers/.env.
- If WireMock certificate generation fails, verify OpenJDK is installed and `keytool` is available on PATH.
- For script execution issues on Windows, set PowerShell execution policy to allow local scripts.

## Additional resources

- [AUTHORS.md](AUTHORS.md)
- [CHANGELOG.md](CHANGELOG.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [Dev Containers documentation](https://code.visualstudio.com/docs/devcontainers/containers)

Built with accessibility in mind, but accessibility issues may still exist; please review and test with tools like Accessibility Insights.

## Skills.sh Integration (Codex + Copilot)

This repository uses the Skills CLI ecosystem at `https://skills.sh/` for skill discovery and installation.

Common commands:

```bash
npx skills find <query>
npx skills list -g
npx skills add <owner/repo@skill> -g -y
npx skills check
```

Reference:

- `.github/skills/INDEX.md` (canonical skill discovery map)

### Install Shared Skills Globally (Codex + Copilot)

These skills are useful for both Codex and Copilot in this repository. You can
paste the whole block into a PowerShell terminal to install them globally.

```powershell
npx skills add microsoft/github-copilot-for-azure@appinsights-instrumentation -g -y
npx skills add microsoft/github-copilot-for-azure@azure-resource-lookup -g -y
npx skills add microsoft/github-copilot-for-azure@azure-resource-visualizer -g -y
npx skills add microsoft/github-copilot-for-azure@microsoft-code-reference -g -y
npx skills add microsoft/github-copilot-for-azure@microsoft-docs -g -y
npx skills add anthropics/skills@webapp-testing -g -y
```

### Install Codex Skills Globally (Skills.sh)

The following commands install Codex-focused skills discussed for this
repository (excluding the shared skills listed above). You can paste the whole
block into a PowerShell terminal to install them globally.

```powershell
npx skills add microsoft/github-copilot-for-azure@azure-ai -g -y
npx skills add microsoft/github-copilot-for-azure@azure-diagnostics -g -y
npx skills add microsoft/github-copilot-for-azure@azure-observability -g -y
npx skills add microsoft/github-copilot-for-azure@azure-rbac -g -y
npx skills add vercel-labs/skills@find-skills -g -y
npx skills add microsoft/github-copilot-for-azure@microsoft-foundry -g -y
```

### Install Copilot Skills Globally (Skills.sh)

The following commands install the Copilot-focused skills discussed for this
repository (excluding the shared skills listed above). You can paste the whole
block into a PowerShell terminal to install them globally.

```powershell
npx skills add microsoft/github-copilot-for-azure@azure-role-selector -g -y
npx skills add microsoft/github-copilot-for-azure@azure-static-web-apps -g -y
npx skills add microsoft/github-copilot-for-azure@github-issues -g -y
npx skills add microsoft/github-copilot-for-azure@make-skill-template -g -y
npx skills add microsoft/github-copilot-for-azure@nuget-manager -g -y
npx skills add microsoft/github-copilot-for-azure@vscode-ext-commands -g -y
npx skills add microsoft/github-copilot-for-azure@vscode-ext-localization -g -y
npx skills add microsoft/github-copilot-for-azure@web-design-reviewer -g -y
```

Optional verification:

```powershell
npx skills list -g
npx skills check
```
