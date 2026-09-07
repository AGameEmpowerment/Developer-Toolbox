# Repository Skills Registry

This is the canonical map for local skill discovery. A skill is a directory
containing `SKILL.md`; supporting `references/`, `scripts/`, `assets/`, and
`agents/` directories remain relative to that file.

## Discovery Roots

| Root | Purpose | Native discovery |
| --- | --- | --- |
| `.agents/skills/` | Canonical shared library; Codex creation/install target | Codex |
| `.claude/skills/` | Claude creation/install target plus generated wrappers | Claude Code |

Claude Code does not natively scan `.agents/skills/`; without a wrapper it
cannot auto-trigger those skills from their descriptions. To close that gap,
`sync_ai_assets.ps1` (repository root) generates a redirect wrapper in
`.claude/skills/<name>/` for every shared skill. Wrappers carry a generation
marker, contain no policy of their own, and point back to the canonical
`SKILL.md`. Re-run the script after adding, renaming, or removing a shared
skill; never hand-edit a generated wrapper.

Agents must inspect both roots before deciding that a local skill is
unavailable. A duplicate name where the Claude copy carries the generation
marker is an intentional redirect, not a divergent definition. For any other
duplicate, prefer the current tool's root and do not merge definitions.

## Creation And Installation

- Default to a repository-local installation unless the user explicitly asks
  for a global/user installation.
- Codex repository install/create target: `.agents/skills/<name>/SKILL.md`.
- Claude repository install/create target: `.claude/skills/<name>/SKILL.md`.
- Do not install skills under `.ai/skills/`; this directory holds only this
  registry.
- Do not create `.codex/skills/`; Codex does not discover that repository path.
- Honor an explicit global/user installation request by using the active tool's
  normal user-level skill location instead of a repository path.

Treat the directory contents as the live inventory rather than maintaining a
second, manually duplicated name list here.

## External Discovery

When no repository skill applies, use the installed `find-skills` capability or:

```bash
npx skills find <query>
npx skills list -g
npx skills check
```

Review an external skill before installation, place a repository copy in the
active tool's discovery root above, and preserve its license and attribution.
Use a global install command only when the user explicitly requests global scope.
