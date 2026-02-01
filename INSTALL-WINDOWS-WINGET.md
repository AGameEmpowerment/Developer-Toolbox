---
post_title: "Windows install commands (winget)"
author1: "AGameEmpowerment"
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

### Required tools

```pwsh
winget install Microsoft.DotNet.SDK.8
winget install Microsoft.DotNet.SDK.9
winget install Microsoft.DotNet.SDK.10
winget install Microsoft.PowerShell
winget install Git.Git
winget install Docker.DockerDesktop
winget install Microsoft.VisualStudio.2022.Community
winget install Microsoft.SQLServer.2022.Express
```

### Optional tools from README

```pwsh
winget install Microsoft.VisualStudioCode
winget install Microsoft.SQLServerManagementStudio
winget install Microsoft.AzureCLI
winget install Postman.Postman
winget install LINQPad.LINQPad
winget install JetBrains.Toolbox
winget install Notepad++.Notepad++
```

### Node.js LTS and npm tools (optional)

```pwsh
winget install OpenJS.NodeJS.LTS
```

```pwsh
npm i -g azure-functions-core-tools@4 --unsafe-perm true
npm i -g @usebruno/cli
```
