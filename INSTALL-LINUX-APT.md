---
post_title: "Linux install commands (apt)"
author1: "Michael Carey"
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

### Tier 1: Critical (required to build and run)

```bash
sudo apt install -y \
	git \
	docker.io \
	docker-compose-plugin \
	nodejs \
	npm \
	powershell \
	dotnet-sdk-10.0
```

### Tier 2: Important (strongly recommended)

```bash
sudo apt install -y \
	openjdk-25-jdk \
	azure-cli
```

OpenJDK is required for `keytool` if you generate WireMock HTTPS certificates locally.

Azure Functions Core Tools (npm):

```bash
npm i -g azure-functions-core-tools@4 --unsafe-perm true
```

### Tier 3: Optional (nice to have)

VS Code:

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

Bruno CLI (npm):

```bash
npm i -g @usebruno/cli
```

JetBrains Toolbox (snap):

```bash
sudo snap install jetbrains-toolbox --classic
```
