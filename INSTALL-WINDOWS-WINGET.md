---
post_title: "Windows install commands (winget)"
author1: "Michael Carey"
post_slug: "install-windows-winget"
microsoft_alias: "n/a"
featured_image: "n/a"
categories: ["documentation", "setup"]
tags: ["windows", "winget", "install", "tooling"]
ai_note: "Created with AI assistance."
summary: "Copy/paste commands to install required and optional tools on Windows using winget."
post_date: "2026-01-31"
---

## Windows setup commands (winget)

Copy and paste the following commands into PowerShell.

### Tier 1: Critical (required to build and run)

```pwsh
winget install Docker.DockerDesktop
winget install Git.Git
winget install Microsoft.DotNet.SDK.10
winget install Microsoft.PowerShell
winget install Microsoft.VisualStudio.Community
winget install Microsoft.VisualStudioCode
winget install OpenJS.NodeJS.LTS
```

### Tier 2: Important (strongly recommended)

```pwsh
winget install Microsoft.AzureCLI
winget install Microsoft.OpenJDK.25
winget install Microsoft.SQLServer.2025.Express
winget install Microsoft.SQLServerManagementStudio.22
```

OpenJDK is required for `keytool` if you generate WireMock HTTPS certificates locally.

### Tier 3: Optional (nice to have)

```pwsh
winget install Bruno.Bruno
winget install GitHub.Copilot
winget install GitHub.cli
winget install JetBrains.Toolbox
winget install Microsoft.Azure.FunctionsCoreTools
winget install Microsoft.Azure.StorageEmulator
winget install Microsoft.Azure.StorageExplorer
winget install Notepad++.Notepad++
winget install OpenAI.Codex
```
