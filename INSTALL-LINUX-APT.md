---
post_title: "Linux install commands (apt)"
author1: "AGameEmpowerment"
post_slug: "install-linux-apt"
microsoft_alias: "n/a"
featured_image: "n/a"
categories: ["documentation", "setup"]
tags: ["linux", "apt", "install", "tooling"]
ai_note: "Created with AI assistance."
summary: "Copy/paste commands to install required and optional tools on Linux using apt and npm."
post_date: "2026-01-31"
---

## Linux setup commands (apt)

Copy and paste the following commands into a Bash shell. These steps target
Ubuntu/Debian. Adjust package names and repositories for other distributions.

### Base prerequisites

```bash
sudo apt update
sudo apt install -y \
	ca-certificates \
	curl \
	gnupg \
	lsb-release \
	apt-transport-https \
	software-properties-common
```

### Microsoft package repository (for .NET, PowerShell, Azure CLI)

```bash
sudo mkdir -p /etc/apt/keyrings
curl -sSL https://packages.microsoft.com/keys/microsoft.asc | \
	sudo gpg --dearmor -o /etc/apt/keyrings/microsoft.gpg
sudo chmod go+r /etc/apt/keyrings/microsoft.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/microsoft.gpg] \
https://packages.microsoft.com/ubuntu/$(lsb_release -rs)/prod \
$(lsb_release -cs) main" | \
	sudo tee /etc/apt/sources.list.d/microsoft-prod.list > /dev/null
sudo apt update
```

### Required tools

```bash
sudo apt install -y \
	git \
	docker.io \
	docker-compose-plugin \
	powershell \
	dotnet-sdk-8.0 \
	dotnet-sdk-9.0 \
	dotnet-sdk-10.0 \
	azure-cli
```

### VS Code (optional)

```bash
sudo install -d -m 0755 /etc/apt/keyrings
curl -sSL https://packages.microsoft.com/keys/microsoft.asc | \
	sudo gpg --dearmor -o /etc/apt/keyrings/packages.microsoft.gpg
sudo chmod a+r /etc/apt/keyrings/packages.microsoft.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/packages.microsoft.gpg] \
https://packages.microsoft.com/repos/code stable main" | \
	sudo tee /etc/apt/sources.list.d/vscode.list > /dev/null
sudo apt update
sudo apt install -y code
```

### Node.js LTS (optional)

```bash
sudo apt install -y nodejs npm
```

### Optional tools from README

Azure Functions Core Tools (npm):

```bash
npm i -g azure-functions-core-tools@4 --unsafe-perm true
```

Bruno CLI (npm):

```bash
npm i -g @usebruno/cli
```

Postman (snap):

```bash
sudo snap install postman
```

JetBrains Toolbox (snap):

```bash
sudo snap install jetbrains-toolbox --classic
```

Notepad++ is Windows-only. LINQPad is Windows-only.
