# Skills Index (Canonical Map)

This file is the single discovery map for AI skills in this repository.

Skill usage in this repository is based on:

- **Instruction skills**: `.github/skills/*/SKILL.md` (workflow guidance for Codex/Copilot)
- **Global Skills CLI**: `npx skills` and `https://skills.sh/` (discovery/install ecosystem)
- **Installed discovery skill**: `find-skills` (use it first when looking for new capabilities)

## Instruction Skills (`.github/skills/*/SKILL.md`)

- `appinsights-instrumentation`
- `azure-resource-visualizer`
- `azure-role-selector`
- `azure-static-web-apps`
- `github-issues`
- `make-skill-template`
- `microsoft-code-reference`
- `microsoft-docs`
- `nuget-manager`
- `vscode-ext-commands`
- `vscode-ext-localization`
- `web-design-reviewer`
- `webapp-testing`

## Skills CLI (Global)

Use the installed Skills CLI for discovery and install:

```bash
npx skills find <query>
npx skills list -g
npx skills add <owner/repo@skill> -g -y
npx skills check
```

Browse catalog:

- `https://skills.sh/`

## Suggested Flow

1. Find skill: `npx skills find <query>`
2. Review skill details at `https://skills.sh/`
3. Install globally: `npx skills add <owner/repo@skill> -g -y`
