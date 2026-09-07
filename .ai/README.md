# AI Engineering Library

`.ai/` is this repository's convention for portable AI behavior.
It is not assumed to be an industry-standard auto-discovery folder. Native tool
entry points redirect here so that policy has one version-controlled source.

## Structure

| Path | Purpose |
| --- | --- |
| `constitution.md` | Repository-wide engineering policy and precedence |
| `instructions/` | Canonical domain-, language-, framework-, and file-specific guidance with portable `paths` metadata |
| `agents/` | Reusable agent roles and personas |
| `prompts/` | Reusable task and output workflows |
| `collections/` | Curated bundles of related AI assets |
| `skills/INDEX.md` | Canonical registry spanning every supported skill root |

## Curated Scope

Keep repository assets focused on:

- .NET 10, C#, ASP.NET APIs, NuGet, and Azure Functions.
- Node.js 24, Next.js, React, localization, and accessibility using public packages.
- Generic Azure DevOps YAML, Terraform, and delivery-planning examples.
- Docker/Podman-compatible container workflows, local emulators, and service
  integration testing.
- QA planning, Playwright/browser automation, security, performance, SQL
  Server, Cosmos DB, and observability.

Do not retain a role, prompt, collection, or skill only because it may be useful
in an unrelated future repository. Discover or install it when that need becomes
concrete.

Repository skills are separated by native discovery behavior:

- Codex creates and installs repository skills under `.agents/skills/`, which
  is also the canonical shared library.
- Claude creates and installs repository skills under `.claude/skills/`.
- `sync_ai_assets.ps1` (repository root) generates marked native adapters:
  `.github/instructions/` from canonical instructions using Copilot `applyTo`,
  `.claude/rules/` from the same instructions using Claude `paths`, redirect
  wrappers under `.claude/skills/`, selected subagent roles under
  `.claude/agents/`, and slash commands for `.ai/prompts/` under
  `.claude/commands/`. It also generates selected Codex subagent definitions
  under `.codex/agents/`. Re-run it after changing any canonical asset.
- `.ai/skills/` contains only the cross-tool registry, not installable skills.
- `.codex/skills/` is not used because Codex does not discover it natively.

`AGENTS.md`, `CLAUDE.md`, and `.github/copilot-instructions.md` require agents to
inspect these roots. Duplicate skill names are not merged; a Claude-side copy
bearing the generation marker is a redirect to the canonical definition.

## Ownership Rules

- Edit canonical instruction content under `.ai/instructions/`; never edit the
  generated `.github/instructions/` or `.claude/rules/` representations.
- Treat portable `paths` metadata as canonical. The sync utility translates it
  into host-specific metadata and embeds the canonical body in each adapter.
- Keep `.github/` for GitHub-specific discovery and platform configuration.
- Do not duplicate the constitution in vendor entry points.
- Keep tool-specific behavior in the matching vendor folder only when it cannot
  be expressed portably.
- Preserve attribution and licensing on imported assets.
