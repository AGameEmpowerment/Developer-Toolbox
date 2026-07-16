# Codex Agents Instructions

This file provides Codex-specific guidance for this repository. It is intentionally short and delegates to the existing instruction library under `.github/`.

## Primary Sources Of Truth

1. `.github/copilot-instructions.md` (general-purpose repository rules and baselines)
2. `.github/instructions/*.instructions.md` (specialized, domain-specific rules)

If any guidance in this file conflicts with the sources above, the more specific file wins, with `.github/instructions/*.instructions.md` taking highest priority.

## Required Behavior

- Always load and follow `.github/copilot-instructions.md` before generating or modifying code.
- When a domain applies (security, performance, accessibility, devops, architecture, etc.), consult the matching file in `.github/instructions/` first and follow it.
- Treat this repository as a local developer toolbox. Do not treat any pipeline, manifest, NuSpec, script, container configuration, sample application, or AI instruction as production-ready.
- When editing delivery, infrastructure, packaging, or setup files, preserve clear notes that they are templates/examples for local development only.
- Mirror existing codebase patterns and versions discovered in project files; do not assume versions or introduce new frameworks.
- For this repository only: prefer new, clean code and clear contracts over preserving legacy compatibility. Make breaking changes when they improve design clarity, and refactor impacted code/tests/configuration in the same change.
- Before committing changes to the local git repository, ask the user for explicit approval, except when the user asks Codex to review/address pull request comments; in that case, fix relevant comments, resolve each thread with a note, and commit the local changes without waiting for a second confirmation.
- Before pushing changes to any remote git repository, ask the user for explicit approval.
- Use `.github/skills/INDEX.md` as the canonical map for available skills.
- Prefer global Skills CLI discovery (`npx skills find <query>`) and the installed `find-skills` skill when searching for new capabilities.

## How To Choose Which Files To Consult First

- If the task is general or cross-cutting, start with `.github/copilot-instructions.md`.
- If the task is domain-specific, open the matching file in `.github/instructions/` first (security, performance, accessibility, architecture, devops, language or framework-specific guidance).
- If the task requests an agent role or persona, open the relevant `.github/agents/*.agent.md` file.
- If the task requests a structured prompt or a specific output format, open the matching `.github/prompts/*.prompt.md` file.
- If the task references curated guidance bundles, open the related `.github/collections/*.collection.yml` or `.github/collections/*.md` file.
- If the task is about skills, open the referenced `.github/skills/*/SKILL.md` file.

## Referenced .github Locations

- `.github/copilot-instructions.md`
- `.github/instructions/` (all `*.instructions.md` files)
- `.github/agents/` (all `*.agent.md` files)
- `.github/prompts/` (all `*.prompt.md` files)
- `.github/collections/` (all `*.collection.yml` and `*.md` files)
- `.github/skills/` (all skill folders and their `SKILL.md` files)
- `.github/skills/INDEX.md` (canonical skill discovery map)
- `.github/workflows/` (AI-related workflows, if applicable)
- `.github/COPILOT-SETUP.md`

## Scope

This file exists only to point Codex at the Copilot instruction library and specialized instruction files. All substantive guidance lives in those files.
