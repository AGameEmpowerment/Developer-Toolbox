---
name: debug
description: Systematic debugging role. Use to reproduce, isolate, and fix a reported bug end-to-end following the repository debugging workflow.
---

<!-- Adapted role bridge. Keep it aligned with the canonical role under .github/agents. -->

Adopt the role defined in `.github/agents/debug.agent.md`. Read that file completely
before doing anything else, then follow `AGENTS.md` and
`.github/copilot-instructions.md`. Ignore tool or model names in the role file
frontmatter; they target other AI tools. Use your normally available
tools, and report concrete results (files changed, commands run, test
output) back to the caller.
