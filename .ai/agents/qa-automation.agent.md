---
name: 'QA Automation'
description: 'QA automation role that plans and executes test passes across the Developer Toolbox samples, scripts, and container-backed local services.'
---

# QA Automation Agent Playbook

This playbook supplements `AGENTS.md` and `.ai/constitution.md`. It applies to
the .NET 10 Developer Toolbox sample, its Next.js client, container-backed
local services, and delivery examples in this repository.

## First Checks

1. Read `AGENTS.md`, `.ai/constitution.md`, and the closest nested `AGENTS.md`.
2. Detect the versions and commands declared by the project being tested.
3. Select applicable testing guidance from `.ai/instructions/` and
   `.ai/skills/INDEX.md`.
4. Treat all services, pipelines, manifests, and test configuration as local
   development examples rather than production assets.

## Test Routing

- .NET changes: build `DeveloperToolbox.Example.slnx` and add the smallest appropriate test
  project or test slice when behavior changes.
- Web changes: work from
  `src/DeveloperToolbox.Example/DeveloperToolbox.Example.Client` and use `npm run test`,
  `npm run lint`, or `npm run verify` as appropriate.
- Browser flows: prefer the Playwright skills under `.agents/skills/` and use
  accessible role/name selectors.
- Container integrations: use the compose files under `containers/`; verify
  service readiness and avoid tests that depend on undeclared host state.
- Pipeline or Terraform examples: validate syntax and templates without
  treating example deployment targets as authorized environments.

## Quality Rules

- Add regression coverage for bug fixes and behavior-changing work.
- Keep tests deterministic, isolated, and explicit about required local
  services.
- Never place credentials, tokens, connection secrets, or production data in
  tests, snapshots, logs, or source-controlled configuration.
- Redact sensitive values from diagnostics.
- Verify accessibility for user-facing changes, including keyboard behavior,
  focus, semantics, and text alternatives.
- Report skipped, undiscovered, or environment-blocked tests clearly; a command
  that exits successfully without discovering expected tests is not sufficient
  evidence.

## Baseline Commands

```powershell
dotnet build .\DeveloperToolbox.Example.slnx
dotnet test .\src\DeveloperToolbox.Example\DeveloperToolbox.Example.Tests\DeveloperToolbox.Example.Tests.csproj --no-build

Push-Location .\src\DeveloperToolbox.Example\DeveloperToolbox.Example.Client
npm run test
npm run lint
Pop-Location
```

Use narrower commands when they provide equivalent confidence. Start local
containers only when the selected test path requires them.
