# GitHub Copilot Repository Entry Point

The authoritative repository policy is [`.ai/constitution.md`](../.ai/constitution.md).
Read it completely before generating, reviewing, or modifying code.

Then load only the assets relevant to the task:

- Specialized instructions: [`.ai/instructions/`](../.ai/instructions/)
- Agent definitions: [`.ai/agents/`](../.ai/agents/)
- Reusable prompts: [`.ai/prompts/`](../.ai/prompts/)
- Curated collections: [`.ai/collections/`](../.ai/collections/)
- Cross-tool skill registry: [`.ai/skills/INDEX.md`](../.ai/skills/INDEX.md)
- Codex/shared skills: [`.agents/skills/`](../.agents/skills/)
- Claude skills: [`.claude/skills/`](../.claude/skills/)

Path-specific files under [`.github/instructions/`](instructions/) are generated
Copilot adapters. They embed canonical `.ai/instructions/` content with native
`applyTo` metadata because Copilot does not discover `.ai/` directly. Never edit
those generated files; update `.ai/instructions/` and run
`pwsh ./sync_ai_assets.ps1`.

Only GitHub-specific workflows, Dependabot configuration, generated adapters,
this entry point, and other platform metadata remain under `.github/`.
