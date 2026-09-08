# GitHub Copilot Setup

This repository uses `.ai/` as the canonical, cross-tool AI engineering library.
GitHub Copilot keeps its required entry point under `.github/`; that file
redirects to authoritative content rather than maintaining a second policy set.

Important: this is a local developer toolbox. Its AI assets and engineering
artifacts are templates or examples and are not production-ready.

## Architecture

| Path | Responsibility |
| --- | --- |
| `.ai/constitution.md` | Authoritative repository-wide policy |
| `.ai/instructions/` | Canonical specialized instructions |
| `.ai/agents/` | Canonical agent definitions |
| `.ai/prompts/` | Canonical reusable prompts |
| `.ai/skills/INDEX.md` | Cross-tool skill registry and precedence |
| `.agents/skills/` | Codex-created/installed and shared repository skills |
| `.claude/skills/` | Claude-created/installed repository skills |
| `.github/copilot-instructions.md` | Copilot entry point into `.ai/` |
| `.github/instructions/` | Generated Copilot path-scoped adapters |
| `.claude/rules/` | Generated Claude path-scoped adapters |
| `.ai/collections/` | Curated bundles of repository-relevant assets |
| `.github/workflows/`, `.github/dependabot.yml` | GitHub platform automation |

## Editing Rules

Edit `.ai/constitution.md`, `.ai/instructions/`, `.ai/agents/`, or
`.ai/prompts/` when changing behavior. Instruction files use portable `paths`
metadata. Run `pwsh ./sync_ai_assets.ps1` to translate it into committed
Copilot `applyTo` adapters and Claude `paths` rules. Never edit generated files
under `.github/instructions/` or `.claude/rules/` directly.

For skills, choose the location based on discovery behavior:

- Codex creates and installs repository skills in `.agents/skills/`.
- Claude creates and installs repository skills in `.claude/skills/`.
- Do not put installable skills in `.ai/skills/` or `.codex/skills/`.
- Register discovery and precedence rules in `.ai/skills/INDEX.md`.

## Entry-Point Flow

```text
Copilot -> .github/copilot-instructions.md --+
        -> .github/instructions/ ------------+-> .ai canonical sources
Claude  -> CLAUDE.md ------------------------+
        -> .claude/rules/ -------------------+
Codex   -> AGENTS.md ------------------------+
                                              -> relevant skill root
```

This keeps repository behavior portable and reviewable in one place.

## Synchronization Check

Generated adapters are committed. Local validation and CI should regenerate
them and fail when the working tree changes:

```powershell
pwsh ./sync_ai_assets.ps1
$changes = git status --porcelain
if ($changes) {
    $changes
    git diff
    exit 1
}
```

A diff means a canonical asset changed without its adapters being refreshed,
or a generated adapter was edited directly. The
`workflows/ai-assets-sync.yml` workflow runs this check for relevant changes.
