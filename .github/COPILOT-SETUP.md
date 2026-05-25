# GitHub Copilot Setup

This repository includes a large `.github` library intended to improve
repository-aware AI assistance for Copilot, Codex, and similar tooling.

## Current Structure

| Path | Purpose |
| --- | --- |
| `.github/copilot-instructions.md` | Primary repository guidance |
| `.github/instructions/` | File- or domain-specific instruction set |
| `.github/agents/` | Task-focused agent definitions |
| `.github/prompts/` | Reusable prompt templates |
| `.github/collections/` | Curated groupings of prompts, instructions, and agents |
| `.github/skills/` | Local skill modules and discovery index |
| `.github/workflows/` | Workflow automation related to Copilot setup |
| `.github/dependabot.yml` | Automated dependency maintenance |

Current inventory at the time of the March 10, 2026 documentation refresh:

- 41 instruction files
- 13 agent files
- 40 prompt files
- 14 collection manifests (13 `*.collection.yml` files plus `structured-autonomy-collection.yml`) plus companion markdown
- 13 skill folders

## How It Works

### Repository Instructions

`.github/copilot-instructions.md` provides the shared baseline for this
repository. It describes the repository as a multi-language starter and points
agents toward the more specific instruction files under `.github/instructions/`.

### Specialized Instructions

The instruction library covers:

- Security and OWASP
- Accessibility
- Performance
- DevOps and Docker
- .NET and C#
- React and Next.js
- Terraform and Azure
- Power Platform
- Markdown, prompts, collections, and agent authoring

These files are intended to be consulted selectively based on the task being
performed.

### Agents

The current agent set includes specialists for:

- Accessibility
- API architecture
- Debugging
- DevOps
- Dynatrace
- .NET
- Next.js
- React frontend work
- JFrog security
- SQL Server DBA work
- Planning
- Terraform
- WinForms

### Prompts

The prompt library is a reusable authoring and implementation toolkit. It
contains templates for:

- Architecture and ADR generation
- API and container scaffolding
- Documentation and README generation
- Testing breakdowns
- DevOps rollout planning
- SQL review and optimization
- Copilot instruction, prompt, and collection generation

### Collections

Collections are higher-level bundles that group related prompts, instructions,
and agents for scenarios such as Azure cloud work, DevOps on-call, testing
automation, project planning, and security review.

Important note:

- Several collection manifests currently point at assets that are not present in
  this repository. Treat collections as curation manifests, not guaranteed
  self-contained bundles.

### Skills

`.github/skills/INDEX.md` is the canonical discovery map for local skill usage.
The bundled skills cover areas like Application Insights, Azure resource
visualization, GitHub issues, NuGet management, VS Code command helpers, and web
application testing.

## Workflow Support

The current workflow support in `.github/workflows/copilot-setup-steps.yml`
restores the example projects and exercises basic Copilot configuration paths in
CI.

Key behaviors:

- Checks out the repository
- Sets up .NET 10
- Configures JFrog CLI
- Sets up Node.js 24
- Restores npm dependencies for the sample client
- Restores .NET dependencies for the example solution

## What Was Removed

The `.github/scripts/` folder was intentionally removed from this repository and
is no longer part of the supported Copilot setup story. Documentation and wiki
content should not reference local `.github/scripts` utilities.

## Recommended Usage

Use this library in layers:

1. Start with `.github/copilot-instructions.md`.
2. Pull in the relevant files from `.github/instructions/` for the task.
3. Use agent files when a task benefits from a dedicated persona or workflow.
4. Use prompt files as reusable starting points rather than ad hoc prompts.
5. Use skills when the repository already has a task-specific module.

## Repository Review Summary

From a documentation and maintainability standpoint, the `.github` library is
one of the strongest parts of this repository. Its main weakness is consistency:
the high-level collections promise more assets than are actually present. That
gap is documented in the root README and the wiki review page so consumers do
not mistake placeholders for implemented capability.
