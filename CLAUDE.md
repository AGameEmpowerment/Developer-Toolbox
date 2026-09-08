# Claude Code Repository Entry Point

Read and follow `AGENTS.md`, then read `.ai/constitution.md` completely before
generating, reviewing, or modifying code. Use applicable specialized guidance
from `.ai/instructions/`.

Use `.ai/agents/` for role definitions and `.ai/prompts/` for reusable task
workflows. These are the authoritative assets; do not create independent policy
inside this file.

## Skills

Search local skills in this order:

1. `.claude/skills/*/SKILL.md` for Claude-specific skills and overrides.
2. `.agents/skills/*/SKILL.md` for the shared repository skill library.

Use `.ai/skills/INDEX.md` as the canonical registry. If duplicate skill names
exist, use the Claude definition before the shared Codex definition. Read the
chosen `SKILL.md` completely and resolve its supporting files relative to its
own directory; a generated wrapper states which canonical directory to resolve
against.

## Generated Native Wrappers

`sync_ai_assets.ps1` (repository root) generates the Claude Code native
discovery surface from canonical sources: path-scoped `.claude/rules/` from
`.ai/instructions/`, `.claude/skills/` wrappers for shared skills in
`.agents/skills/`, `.claude/agents/` subagents for selected roles in
`.ai/agents/`, and `.claude/commands/` slash commands for `.ai/prompts/`.
Generated files carry a marker comment; never edit them directly. Re-run the
script after changing a canonical instruction, skill, agent role, or prompt.

Unless the user explicitly requests a global/user installation, treat skill
creation or installation in this repository as repository-local. Claude must write
`.claude/skills/<name>/SKILL.md`; it must not write `.agents/skills`,
`.codex/skills`, or `.ai/skills`. A global installation explicitly requested by
the user may use Claude's normal user-level location.
