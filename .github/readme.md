# GitHub Platform Configuration

This folder contains GitHub-specific automation and the Copilot entry point.
Portable AI policy and reusable behavior live under
`.ai/`; shared repository skills live under `.agents/skills/`.

## Contents

- `copilot-instructions.md`: the repository-wide Copilot entry point; it
  redirects to the AI constitution and registry.
- `instructions/`: generated path-scoped Copilot adapters derived from
  `.ai/instructions/`.
- `workflows/`: GitHub Actions workflows.
- `dependabot.yml`: dependency update configuration.
- `COPILOT-SETUP.md`: architecture and maintenance guidance.

Do not edit generated instruction adapters or recreate agents, prompts,
collections, or skills here. Update their canonical `.ai/` or `.agents/skills/`
files and run `pwsh ./sync_ai_assets.ps1` instead.

Many assets originated from or were adapted from GitHub's `awesome-copilot`
collection. Preserve attribution and license notices when modifying or
redistributing imported content.
