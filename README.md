---
post_title: Developer Toolbox
author1: Michael Carey
post_slug: developer-toolbox
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [developer-toolbox, local-development]
ai_note: AI assisted
summary: Shared local infrastructure with independent C#, Java, Node.js, and Python examples.
post_date: 2026-10-13
---

Developer Toolbox provides shared local infrastructure, independent C#,
Java, Node.js, and Python examples, a Next.js client, Dev Container automation,
delivery templates, and a cross-tool AI engineering library.

> [!IMPORTANT]
> This repository is for local development. Containers, pipelines, manifests,
> NuSpec files, scripts, samples, and AI assets are templates or examples,
> and are not production-ready. Review and adapt them before production use.

## Choose Your Starting Point

| Task | Guide |
| --- | --- |
| Set up this checkout | [Quick start](#quick-start) and [wiki setup guide](https://github.com/AGameEmpowerment/Developer-Toolbox/wiki/Getting-Started) |
| Refresh an existing checkout | [Keep your checkout on main](#keep-your-checkout-on-main) |
| Add Toolbox support to another project | [Bootstrap installation](#install-toolbox-support-in-another-repository) and [complete installation guide](https://github.com/AGameEmpowerment/Developer-Toolbox/wiki/Installing-Toolbox-Support) |
| Start or inspect local resources | [Container guide](containers/readme.md) and [AWS guide](containers/aws/README.md) |
| Design or customize local identity | [OIDC base images and project customizations](docs/guides/local-oidc.md) |
| Build a local base or project image | [Local image build command](docs/guides/local-oidc.md#build-images-locally) |
| Choose image versions and pull behavior | [Container image policy](docs/guides/container-image-policy.md) |
| Run an example in your language | [Language guides](#sample-applications) |
| Use or maintain AI guidance | [AI library](.ai/README.md) and [skill registry](.ai/skills/INDEX.md) |
| Review current limitations and evidence | [Wiki review findings](https://github.com/AGameEmpowerment/Developer-Toolbox/wiki/Review-Findings) |
| Refresh the wiki after a merge | [Wiki maintenance and commit history](https://github.com/AGameEmpowerment/Developer-Toolbox/wiki/Wiki-Maintenance-and-Commit-History) |

## What Is Included

| Path | Contents |
| --- | --- |
| `.devcontainer/` | .NET 10 base, .NET 8/9/10, Java 25, Node 24, Python 3.14, Docker-in-Docker, editor tools, and launchers |
| `containers/` | Three Compose groups with 13 long-running resource services and two initialization jobs |
| `src/` | Seven .NET application/library projects, three Java modules, Node.js/Python SDK and serverless examples, and a Next.js client |
| `tests/` | Seven NUnit projects, Python helper regressions, shell setup/bootstrap suites, and opt-in disposable resource fixtures |
| `setup/` | Consumer bootstrap templates, copy manifest, and optional example settings generator |
| `devops/` | Five disabled Azure DevOps pipeline examples, four Cloud Foundry manifests, NuSpec packaging, and a Terraform placeholder |
| `.ai/` and `.agents/skills/` | Canonical engineering policy, 47 instructions, 55 roles, 45 prompts, 13 collections, and 63 shared skills |
| `.github/`, `.claude/`, `.codex/` | Platform configuration and generated native AI adapters |
| `docs/` | [OIDC implementation plan](docs/local-oidc-plan.md), local identity guide, dated review evidence and execution limits |

Shared images contain infrastructure and required initialization only. Sample
applications run separately in your developer environment. Starting the shared
resources requires no application language SDK or real cloud credentials.

## Local OIDC Image

Build the reusable local Keycloak base with Docker or Podman:

```powershell
./Build-ToolboxImage.ps1
```

The default `oidc` profile produces `localhost/toolbox-oidc-keycloak:latest`.
Use `-ContainerRuntime docker` or `-ContainerRuntime podman` to select an engine.
Applications derive their own images and supply their own runtime realms,
clients, callbacks and synthetic users. Keep both builds in the same engine
and image store. See the [startup contract](containers/oidc/keycloak/README.md),
[local identity guide](docs/guides/local-oidc.md), and
[implementation status](docs/local-oidc-plan.md).

## Quick Start

### Dev Container

Install Git, Docker with a running engine, Visual Studio Code, and the Dev
Containers extension. For the command-line flow below, also install PowerShell
7 and GitHub CLI, authenticate `gh` if needed:

```powershell
gh repo clone AGameEmpowerment/Developer-Toolbox -- --branch main
Set-Location Developer-Toolbox
pwsh -File ./.devcontainer/start_devcontainer.ps1
```

The Bash host launcher is `./.devcontainer/start_devcontainer.sh`. Opening the
folder and choosing **Dev Containers: Reopen in Container** is also supported.
For either script launcher, install the Dev Container CLI or Node.js with `npx`
on the host; the editor reopen workflow can be used without that separate CLI.
The post-create hook prepares optional tooling and certificates; it leaves the
resource stack stopped and does not restore credentialed packages.

Inside the Dev Container, start the resources when needed:

```powershell
./start_developer_toolkit.ps1
```

Or use `./start_developer_toolkit.sh`. These guarded commands require a Dev
Container or Codespace. The outer host engine and the inner Docker-in-Docker
daemon are separate; host containers are not the inner service stack.

The frontend and language examples use public package registries. No private
package credentials are required. Keep application credentials outside the checkout.

### Native Host

Use the [Windows toolchain guide](INSTALL-WINDOWS-WINGET.md) or
[Linux/WSL guide](INSTALL-LINUX-APT.md). Install the SDK for the examples you
choose; installing all four languages is unnecessary.

```powershell
./docker_setup.ps1 -ContainerRuntime auto
```

```bash
./docker_setup.sh --runtime auto
```

Automatic selection prefers a reachable Docker engine, then Podman. Podman
requires Linux server version 6 or later and a compatible Compose provider.
Docker is the supported Dev Container baseline; a rootful Podman machine for
the outer container remains a best-effort option. See the
[wiki setup guide](https://github.com/AGameEmpowerment/Developer-Toolbox/wiki/Getting-Started)
for explicit engine selection and prerequisites.

### Keep Your Checkout on Main

An existing checkout can remain on a feature branch after its PR is merged.
Commit or stash local changes and finish any pending merge before switching
branches. From the repository root, update local `main` from GitHub:

```powershell
git fetch origin
git switch main
git pull --ff-only origin main
git rev-parse main origin/main
git diff main origin/main -- README.md
```

These Git commands also work in Bash. Matching commit hashes confirm local
`main` matches the fetched remote; an empty diff confirms the README matches.
If local `main` has extra or divergent commits, inspect them before proceeding.
The fast-forward-only pull stops on divergence so you can preserve local work.

A squash merge creates a new commit on `main`, so the old PR branch can show
different history even when its changes are already merged. Switch to updated
`main` and create any follow-up branch there rather than merging `main` back
into a completed PR branch.

## Local Development Services

Setup creates ignored `containers/.env`, adds missing template keys, generates
managed `auto` passwords/keys, prepares WireMock trust material, prepares missing images,
and builds/starts all three groups. Explicit local values are preserved.

| Group / Compose file | Services and host endpoints |
| --- | --- |
| `dev_common_shared` / `docker-compose-common.yml` | Redis `10120`; RedisInsight UI `10121`; smtp4dev SMTP `10130`, UI `10140`; Seq UI/API `10150`; WireMock HTTP `10080`, HTTPS `10443` |
| `dev_azure_shared` / `docker-compose-azure.yml` | SQL Server `10433`; Cosmos HTTPS `10081`, Explorer HTTPS `10181`; Azurite Blob/Queue/Table `10000–10002`; Service Bus AMQP `5672` and `10170`, management `10171` |
| `dev_aws_shared` / `docker-compose-aws.yml` | PostgreSQL `10432`; DynamoDB Local `10180`; RabbitMQ AMQP `10190`, UI `10191`; Moto S3/SQS/SNS/EventBridge `10195` |

Host ports bind to loopback. The AWS group also runs `dynamodb-volume-init` and
`aws-init` as one-shot jobs. Moto uses in-memory state, restores its default
topology on restart, and does not execute Lambda handlers. It needs no account,
license token, or engine socket.

Long-running services use `unless-stopped`: manual stops persist until an
explicit start or setup run. Normal teardown retains ten named data volumes.
Relocated Azure/AWS volumes keep their original `dev_common_shared_*` names;
run setup once before direct Compose startup on a fresh installation.

Inspect all groups and stop them without deleting data:

```powershell
foreach ($group in @('common', 'azure', 'aws')) {
    docker compose --env-file containers/.env `
      -f "containers/docker-compose-$group.yml" -p "dev_${group}_shared" ps
}
./docker_down.ps1 -ContainerRuntime auto
```

The Bash teardown is `./docker_down.sh --runtime auto`. Preserve `.env` when
retaining data: initialized database/broker passwords and Seq's storage key stay
with their volumes. Volume deletion requires explicit cleanup and force flags.

Most local images intentionally follow floating current tags. Cosmos follows
`vnext-latest`; SQL Server and Full-Text Search are coupled to matching builds;
the client runtime uses `node:24-slim`. This policy accepts upstream changes for
developer tooling. Production adopters must use tested immutable tags or digests.

## Sample Applications

| Example | Runtime and behavior | Guide |
| --- | --- | --- |
| C# | .NET 10; EF Core SQL Server/PostgreSQL, 18 SDK/resource commands, Functions, and Lambda handlers | [C# examples](src/DeveloperToolbox.Example/README.md) |
| Java | JDK 25 and Maven; Hibernate transactions, SDK checks, Functions, and Lambda handlers | [Java examples](src/DeveloperToolbox.Java.Example/README.md) |
| Node.js | Node 24; 15 SDK checks, native Functions queues, Blob/S3 receipts, and Lambda handlers | [Node.js examples](src/DeveloperToolbox.Example/DeveloperToolbox.Example.Node/README.md) |
| Python | Python 3.14; 15 SDK checks, native Functions queues, Blob/S3 receipts, and Lambda handlers | [Python examples](src/DeveloperToolbox.Example/DeveloperToolbox.Example.Python/README.md) |
| Next.js client | Node 24, Next.js 16.4, React 19.3, public semantic UI, four locales, and example routes | [Client guide](src/DeveloperToolbox.Example/DeveloperToolbox.Example.Client/README.md) |

The C# console and Java CLI can read `containers/.env` directly via
`TOOLBOX_ENV_FILE`. The optional settings generator writes ignored C# application,
per-language Functions, and shared Node/Python JSON settings. Existing JSON is
retained unless `--force` is explicitly requested:

```powershell
python setup/configure_examples.py
```

A .NET-only developer can use the console without Python:

```powershell
$env:TOOLBOX_ENV_FILE = (Resolve-Path containers/.env).Path
dotnet build src/DeveloperToolbox.Example/DeveloperToolbox.Example.App --configuration Release
dotnet run --project src/DeveloperToolbox.Example/DeveloperToolbox.Example.App --configuration Release --no-build -- validate sqlserver postgres storage-queue
```

`validate` uses already-running resources and returns `0` for success, `1` for
failed/timed-out/canceled checks, and `2` for invalid arguments. The cooperative
per-operation timeout defaults to 45 seconds; cleanup can extend elapsed time.
Validation writes synthetic data, including retained database messages, mail,
and Seq events. It does not change container lifecycle or check management UIs.

Functions hosts run separately using Core Tools 4. Lambda tests invoke handlers
directly; shared setup supplies no Lambda runtime or event-source dispatcher.

For the client, use Node 24 and
run from `src/DeveloperToolbox.Example/DeveloperToolbox.Example.Client`:

```bash
npm ci
npm run verify
npm run build:test
npm run dev
```

`build:test` builds the deterministic public example catalog. Optional runtime
settings are listed in the client's `.env.local.example`.

## AI Toolkit and Automation

`AGENTS.md`, `CLAUDE.md`, and `.github/copilot-instructions.md` direct tools to
`.ai/constitution.md`, matching `.ai/instructions/*.md`, and scoped instructions.
Discover skills through `.ai/skills/INDEX.md`, `.agents/skills/`, and
`.claude/skills/`. Repository-local Codex skills belong in `.agents/skills/`.

The [GitHub platform guide](.github/readme.md) covers workflows,
Dependabot, and Copilot configuration. Keep `.github/` documentation under
non-README filenames so this root README remains the repository home page.

Generated files carry `Generated by sync_ai_assets.ps1`. Edit canonical sources
and regenerate adapters when those assets change:

```powershell
pwsh -NoProfile -File ./sync_ai_assets.ps1
git diff --check
```

See [Scripts and Automation](https://github.com/AGameEmpowerment/Developer-Toolbox/wiki/Scripts-and-Automation)
for the complete launcher and helper catalog. The active Resource Examples
Validation workflow builds/tests each language independently and runs isolated
resource checks. Other workflows validate AI adapters, bootstrap scripts and
Copilot setup. Their YAML definitions do not establish their enabled state on
GitHub. Azure DevOps pipelines remain disabled-trigger local examples.

## Install Toolbox Support in Another Repository

From the Toolbox checkout, supply the exact root of an existing non-bare Git
worktree:

```powershell
./Install-ToolboxSetup.ps1 -TargetRepositoryPath C:\path\to\MyProject
```

```bash
./install_toolbox_setup.sh /path/to/MyProject
```

The installers add missing paths from `setup/toolbox-bootstrap-files.txt`,
preserving existing files and links. They copy Dev Container support, editor settings, the bridge skill, and `setup_toolbox.*` wrappers.
They keep application examples and Compose definitions in the separate Toolbox
checkout.

From the consumer repository, `./setup_toolbox.ps1 -SkipSetup` or
`./setup_toolbox.sh --skip-setup` obtains/updates that checkout without starting
services. Omit the skip option to start resources. Use `DEVELOPER_TOOLBOX_ROOT` or
the launcher's path option to select another checkout. Follow the
[installation guide](https://github.com/AGameEmpowerment/Developer-Toolbox/wiki/Installing-Toolbox-Support)
for safe refreshes and branch selection.

## Verification and Known Limits

Current validation is recorded in [the parity review](docs/parity/verification.md).
The resource definitions include 13 long-running services and two one-shot jobs.
Keycloak remains an independently built, project-owned identity resource.

The resource images build locally. The .NET tests have 484 passed cases, zero
failures and two optional database cases skipped. The NuGet scan reports no
vulnerable packages across the 14 solution projects. Java verification passes 117
JUnit cases, and the live OIDC startup suite passes all 25 checks with a 300-second
readiness budget. Floating image tags, generated local trust material,
emulator feature limits and sample configuration remain local-development constraints.
No delivery template was deployed or published.

## Architecture and Wiki

See [architecture](docs/architecture.md), [current test inventory](docs/parity/test-inventory.md),
[OneBox adoption](docs/guides/onebox-adoption.md), and the
[wiki](https://github.com/AGameEmpowerment/Developer-Toolbox/wiki).
The wiki update is maintained on `codex/toolbox-resource-parity` in the separate
wiki repository. It records committed source revisions and original execution
hashes; the feature branch remains unmerged into the default branch.
