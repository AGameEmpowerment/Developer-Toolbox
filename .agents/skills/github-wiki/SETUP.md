# github-wiki setup

The `github-wiki` skill reviews the repository that contains your current
working directory and refreshes that repository's GitHub wiki. It behaves the
same from any of the three install locations below, so pick the one that fits
how you work.

## Prerequisites

- **git** on your `PATH`.
- **GitHub CLI** (`gh`) logged in to github.com with access to the repository
  and its wiki:

  ```powershell
  gh auth login
  gh auth status -h github.com
  ```

  `gh` also configures git credentials, so wiki clones and pushes reuse the
  same login.
- **Python 3** or **PowerShell 7** for the optional inventory script under
  `scripts/`. The skill still works without either; the agent gathers the
  same facts by hand.
- The repository's wiki must be enabled (repository Settings, Features,
  Wikis) and must have at least one page. GitHub creates the wiki's git
  repository the first time a page is saved in the browser.

## Install option A: global user skill

Copy this folder (the one containing `SKILL.md`) into your user skills
directory so it is available in every repository:

| Agent | Windows | macOS / Linux |
|---|---|---|
| Claude Code | `%USERPROFILE%\.claude\skills\github-wiki\` | `~/.claude/skills/github-wiki/` |
| Codex | `%USERPROFILE%\.agents\skills\github-wiki\` | `~/.agents/skills/github-wiki/` |

Keep one real copy and make the other location a symbolic link if you use
both agents, so the two never drift.

## Install option B: project-local skill

Copy the same folder into the repository whose wiki you maintain so the skill
travels with that repository:

| Agent | Path inside the repository |
|---|---|
| Claude Code | `.claude/skills/github-wiki/` |
| Codex | `.agents/skills/github-wiki/` |

Commit it like any other project file. A project-local copy takes precedence
over a global copy with the same name in most agents, so keep them at the
same version.

## First run

From inside the repository:

```text
Give this repository's GitHub wiki a complete, thorough update.
```

The skill will report the repository and default branch, warn if you are not
on the default branch, clone the wiki to a sibling folder named
`<repo-folder>.wiki`, inventory the wiki and the project, propose a page plan,
write the pages in the wiki clone, and then stop and ask before it commits and
pushes anything.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `gh: command not found` or `not logged in` | Install the GitHub CLI and run `gh auth login`. |
| Clone of `<url>.wiki.git` fails with `repository not found` while the wiki is enabled | The wiki has no pages yet. Create the Home page at `<url>/wiki` in the browser, then retry. |
| `hasWikiEnabled: false` | A repository admin must turn on Wikis under Settings, Features. |
| Push rejected with a permissions error | Your account lacks write access, or wiki editing is restricted to collaborators. Ask a repository admin. |
| Push rejected because the remote moved | Someone edited the wiki in the browser. Run `git pull --rebase` in the wiki clone, reconcile, and push again. Never force-push. |
| The inventory script exits with code 1 | The wiki is disabled, the clone path is inside the project, or the existing clone cannot be verified. Use the warning in the report to correct the path or repository state. |
| The inventory script exits with code 2 | It found broken wiki links or orphan pages. Fix them before asking for approval; the report lists each one. |
