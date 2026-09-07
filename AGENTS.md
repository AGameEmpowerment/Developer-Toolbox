# Repository Agent Contract

This is the tool-neutral entry point for coding agents working in this repository.
The authoritative AI policy and reusable assets live under `.ai/`; vendor folders
exist only for native discovery and tool-specific extensions.

## Authority And Precedence

Read guidance in this order before generating or modifying code:

1. `.ai/constitution.md` — repository-wide policy and engineering baseline.
2. `.ai/instructions/*.md` — specialized guidance selected for the
   applicable domain, language, framework, or file type.
3. The closest nested `AGENTS.md` or tool-specific instruction file that applies
   to the files being changed.

More specific guidance wins. When specialized instructions conflict with the
constitution, follow the specialized instructions for their declared scope.

## Required Behavior

- Always read `.ai/constitution.md` before generating or modifying code.
- Consult matching files in `.ai/instructions/` for security, performance,
  accessibility, DevOps, architecture, language, and framework work.
- Treat this repository as a local developer toolbox. Pipelines, manifests,
  NuSpec files, scripts, containers, samples, and AI assets are templates or
  examples for local development, not production-ready artifacts.
- Preserve explicit local-development-only notes when editing delivery,
  infrastructure, packaging, or setup files.
- Detect and mirror versions and patterns already present in project files.
- Prefer clean contracts over legacy compatibility in this repository; refactor
  affected code, tests, and configuration together when making a breaking change.
- Ask for explicit approval before committing, except when addressing pull
  request review comments. Always ask before pushing.

## Asset Discovery

- Agent role or persona: `.ai/agents/`
- Specialized instructions: `.ai/instructions/`
- Reusable prompts: `.ai/prompts/`
- Skill registry and precedence: `.ai/skills/INDEX.md`
- Curated collections: `.ai/collections/`

## Skill Discovery Across Tools

Do not assume that a skill exists in only one tool folder. Before concluding
that no local skill applies, inspect both repository discovery locations:

1. `.agents/skills/*/SKILL.md` — skills created or installed by Codex and shared
   repository skills.
2. `.claude/skills/*/SKILL.md` — skills created or installed by Claude.

When duplicate skill names exist, prefer the current tool's discovery path and
never merge two definitions implicitly. Read the selected `SKILL.md` completely
and resolve its relative resources from that skill's directory.

Files under `.github/instructions/` and `.claude/rules/` are generated adapters
derived from canonical `.ai/instructions/*.md` files. Most entries under
`.claude/skills/`, `.claude/agents/`, and `.claude/commands/` are generated
redirects produced by `sync_ai_assets.ps1` from canonical sources in
`.agents/skills/` and `.ai/`. Generated files carry a marker comment. Never edit
them directly; change the canonical source and re-run the script.

Unless the user explicitly requests a global/user installation, treat skill
creation or installation in this repository as repository-local. Codex must write
`.agents/skills/<name>/SKILL.md`; it must not write `.codex/skills` or
`.ai/skills`. A global installation explicitly requested by the user may use the
tool's normal user-level location.

Use global Skills CLI discovery (`npx skills find <query>`) or an installed
`find-skills` skill only when the repository skill registry does not already
cover the task.

## Vendor Entry Points

- GitHub Copilot: `.github/copilot-instructions.md`
- Claude Code: `CLAUDE.md`
- Codex and other `AGENTS.md` consumers: this file

These entry points must redirect to `.ai/` rather than becoming independent
copies of repository policy.
