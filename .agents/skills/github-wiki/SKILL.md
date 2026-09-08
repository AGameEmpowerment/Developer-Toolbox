---
name: github-wiki
description: >-
  Give the GitHub wiki of the active repository a complete, thorough update.
  Use whenever the user asks to update, refresh, rewrite, sync, audit, or
  create the project wiki, wiki pages, Home page, or sidebar, or to document
  the repository in its GitHub wiki, even when no wiki URL is supplied.
  Locates the wiki from the repository's GitHub remote, uses the GitHub CLI
  and git to review the existing wiki pages and the entire project structure,
  warns when the checkout is not on the default branch, drafts the full page
  set in a local wiki clone, and pushes only after the user's explicit
  approval.
compatibility: Requires git and an authenticated GitHub CLI (gh) session with access to the target repository and its wiki. Python 3 or PowerShell 7 runs the optional inventory script.
---

## Purpose

Bring a repository's GitHub wiki into line with what the repository actually
contains today. The wiki lives in its own git repository at
`<repo-url>.wiki.git`, so the work happens in a local wiki clone, never inside
the project working tree. The GitHub CLI supplies repository identity,
settings, and history; git supplies the wiki content and the project content.

Read [wiki mechanics](references/wiki-mechanics.md) before touching the wiki
clone and [page blueprint](references/page-blueprint.md) before planning the
page set. The blueprint distills the [example wikis](references/example-wikis.md)
that define the expected depth and style; consult them when a page's shape is
unclear.

This skill works the same whether it is installed as a marketplace plugin, a
global user skill, or a project-local skill. It always operates on the git
repository that contains the current working directory.

## Non-negotiable rules

- A wiki push is visible to everyone with repository access and has no review
  step. Never push, force-push, or delete wiki pages without the user's
  explicit approval in the current conversation. The original request to
  update the wiki is not that approval.
- Never clone the wiki inside the project working tree. Use a sibling folder
  or a temporary folder as described in the wiki mechanics reference.
- Preserve human-authored content that is still accurate. Rewrite what is
  stale, fill what is missing, and remove only what is wrong or duplicated.
  Say what was removed and why.
- Document only what the repository shows. Do not invent features, commands,
  owners, or dates. When something cannot be verified, leave it out or mark
  it as an open question for the user.
- Never copy secrets, tokens, connection strings, or personal email addresses
  into the wiki, even when they appear in repository files.
- Do not change the project repository itself. Wiki work produces commits in
  the wiki clone only. Suggest project changes (for example a README fix)
  separately.

## Workflow

### 1. Establish repository identity and branch state

1. Resolve the project root and inspect the checkout:

   ```powershell
   git rev-parse --show-toplevel
   git branch --show-current
   git status --short
   git remote -v
   ```

2. Verify the GitHub CLI session and repository identity, and read the wiki
   and default-branch settings:

   ```powershell
   gh auth status -h github.com
   gh repo view --json nameWithOwner,url,description,defaultBranchRef,hasWikiEnabled,isPrivate,visibility
   ```

   The wiki URL is `<url>/wiki` and the wiki git remote is `<url>.wiki.git`.

3. **Default-branch check.** Compare the current branch with
   `defaultBranchRef.name`. If they differ, warn the user before doing
   anything else. State the current branch, the default branch, and the risk:
   the wiki should describe what the default branch contains, and a feature
   branch may include unmerged or abandoned work. Then ask whether to
   continue from this branch, or to base the review on the default branch
   without switching (use `git fetch origin`, then read files with
   `git show origin/<default>:<path>` and list them with
   `git ls-tree -r --name-only origin/<default>`). Do not check out another
   branch or discard work unless the user asks for that. When no user is
   available to answer, base the review on the default branch and repeat the
   warning in the final report.
4. Also warn when the working tree has uncommitted changes or when the local
   branch is behind its remote (`git fetch origin`, then
   `git rev-list --count HEAD..origin/<branch>`). Uncommitted or unpushed
   work is not yet part of the project and must not be documented as if it
   were.
5. If `hasWikiEnabled` is false, stop and tell the user. Enabling the wiki
   is a repository setting (Settings, Features, Wikis) that needs admin
   rights; do not change it silently. If the wiki is enabled but the clone in
   the next step fails with "repository not found", the wiki has no pages
   yet. Ask the user to create the first page from `<url>/wiki` in the
   browser, then retry.

### 2. Clone or refresh the wiki and inventory it

1. Choose the wiki clone location. Prefer a sibling folder named
   `<repo-folder>.wiki` next to the project. If it already exists and is a
   git clone of the same wiki, pull it. Otherwise clone:

   ```powershell
   git clone <url>.wiki.git ../<repo-folder>.wiki
   ```

   If a sibling folder is not possible, clone into the operating system's
   temporary directory and report the path.
2. Run the bundled inventory script from the project root. Both scripts do
   the same job and produce the same report; pick whichever runtime exists:

   ```powershell
   python <skill-root>/scripts/wiki_inventory.py --repo . --wiki-dir ../<repo-folder>.wiki
   pwsh -NoProfile -File <skill-root>/scripts/wiki_inventory.ps1 -Repo . -WikiDir ../<repo-folder>.wiki
   ```

   The report lists repository identity and branch state, every wiki page
   with its title, size, last commit date, headings, wiki links, and broken
   links, plus a depth-limited project tree and the documentation-bearing
   files found in the project. Add `--json` (or `-Json`) for machine-readable
   output. If neither runtime is available, gather the same facts by hand with
   `git log`, `ls`, and `rg`.
3. Read every existing wiki page completely. Record for each page whether it
   is current, stale, duplicated, orphaned (no inbound link), or empty.

### 3. Review the entire project

Read the project thoroughly enough to describe it to a new team member. Cover
at least:

- Top-level layout and the purpose of every top-level folder.
- `README*`, `CONTRIBUTING*`, `CHANGELOG*`, `LICENSE*`, `SECURITY*`,
  `CODEOWNERS`, `docs/`, ADRs, and any agent instruction files such as
  `AGENTS.md` or `CLAUDE.md`.
- Build, test, package, and dependency manifests (for example `*.csproj`,
  `*.sln`, `package.json`, `pyproject.toml`, `requirements*.txt`,
  `Dockerfile`, `docker-compose*.yml`).
- CI/CD and automation under `.github/workflows/`, `azure-pipelines*.yml`,
  or similar, including required secrets and environments by name only.
- Configuration and environment samples (`appsettings*.json`, `.env.example`),
  describing the variables without their values.
- Public entry points: CLIs, APIs, scripts, plugin or skill manifests.
- Repository history and activity through the GitHub CLI, so the wiki can
  reflect recent direction:

  ```powershell
  git log --oneline -30
  gh release list --limit 10
  gh pr list --state merged --limit 20 --json number,title,mergedAt
  gh api repos/{owner}/{repo}/languages
  gh api repos/{owner}/{repo}/topics --jq .names
  gh api repos/{owner}/{repo}/contents/.github --jq '.[].name'
  ```

Run the project's own build, test, lint, or validation commands when they are
documented, non-destructive, and available on this machine (for example
`dotnet build`, `dotnet test --list-tests`, `npm test`, a repository audit
script). Record the exact command and result, including failures and checks
that were blocked by missing credentials or tools. These verified results feed
the Home page baseline table and the review-findings page. Never run
deployment, publishing, data-modifying, or long-running commands for this.

Note contradictions between the code and existing documentation. The code
wins; record the contradiction so the user can fix the README or other docs,
and surface it on the wiki's current-state or review-findings page.

### 4. Plan the page set

Compare the inventory against the [page blueprint](references/page-blueprint.md)
and produce a page plan before writing. For every page, decide one of: keep,
update, rewrite, create, merge into another page, or delete. Include:

- `Home.md` following the blueprint's Home anatomy: lead paragraph, review
  line with date and commit, quick-facts table, task-to-page table,
  architecture at a glance, fast start, and authoritative sources.
- `_Sidebar.md` listing every page once, grouped under three to five short
  headings in reading order. Add `_Footer.md` only if the project benefits
  from a one-line shared footer.
- Pages that match the project's real shape. A library needs inventory,
  usage, and configuration-reference pages; a service needs architecture,
  configuration, deployment, and operations pages; a test framework needs
  catalog, writing, and running pages; a marketplace or monorepo needs a
  catalog page.
- The meta pages the reference wikis rely on: a current-state-and-limitations
  page that separates implemented behavior from known gaps and drift, and a
  source-document-index or review-findings page that records the inputs,
  authority order, commands run, and verified results of this refresh.
- Aim for the blueprint's depth: roughly 10 to 25 pages of 80 to 300 lines
  each for a typical repository. Merge thin pages; split pages that need more
  than about six top-level sections.

Present the plan to the user in a short table (page, action, reason) when a
user is available to review it, then proceed. When running unattended, record
the plan in the final report.

### 5. Write the pages

Write in the wiki clone only. Follow these conventions:

- Start every page with `##` sections; do not add an `# H1`, because GitHub
  renders the title from the file name. Page files use hyphens for spaces
  (`Getting-Started.md` renders as "Getting Started"). Keep heading style
  consistent across the wiki: noun phrases or imperative tasks, not a mix.
- Open each page with a short purpose paragraph and a `Last reviewed:
  YYYY-MM-DD.` line (or a sentence naming the reviewed branch and commit).
  Close with `## Related Pages` and, on pages that change often,
  `## Revision Notes` with dated bullets.
- Link between pages with relative Markdown links, `[Getting
  Started](Getting-Started)`, which is what the reference wikis use. Link to
  repository files with `https://github.com/<owner>/<repo>/blob/<default>/<path>`
  URLs so links keep working from the wiki.
- Use tables for every inventory (paths, settings, requirements, tasks,
  pipelines, tests) and prose only to explain. Use exact counts from the
  checkout rather than vague quantities.
- Put every command in a fenced code block with a language hint. Commands
  must exist in the repository today or have been run during this review.
- Include at least one Mermaid `flowchart` on the architecture page or Home,
  followed by a text description of the same flow.
- Put the review line near the top of `Home.md` with today's date and the
  short commit hash of the project checkout the wiki describes, plus the
  quick-facts table from the blueprint.
- Label stale, historical, disabled-by-default, or template-only content as
  such, and add a boundary callout on Home when the repository is a toolbox,
  starter, or example rather than a production system.
- Keep every page self-contained enough to be read alone, and keep the
  sidebar in sync with the pages that exist.
- Keep the wording organization-neutral unless the repository itself is
  specific to one organization.

After writing, run the inventory script again with `--no-clone` (or
`-NoClone`) to confirm there are no broken wiki links and no orphaned pages.
Fix anything it reports.

### 6. Stop for the user's approval

Show the user:

- The wiki clone path and `git status --short` inside it.
- The page plan with the final action for each page, including deletions.
- A brief summary of the most significant content changes.
- Any default-branch, uncommitted-work, or code-versus-docs warnings.
- The proposed wiki commit message.

Ask for explicit approval to commit and push the wiki. Until it arrives, do
not commit or push. When approval cannot be obtained because no user is
available, leave the changes uncommitted in the wiki clone, and report the
path and the exact commands the user can run to review and push.

### 7. Commit and push after approval

1. Re-check `git status --short` in the wiki clone to confirm nothing changed
   unexpectedly.
2. Commit all wiki changes with a descriptive message, for example
   `docs(wiki): refresh project documentation for <short-hash> on <default-branch>`.
3. Determine the wiki's branch (`git symbolic-ref --short HEAD`, usually
   `master`) and push to it:

   ```powershell
   git push origin HEAD
   ```

4. Verify the result at `<url>/wiki`. Open the Home page and one updated page
   in the browser, or fetch the wiki clone again and confirm the pushed commit
   is the remote head:

   ```powershell
   git ls-remote <url>.wiki.git HEAD
   ```

   If the push is rejected because the wiki moved, pull with rebase, resolve
   conflicts in favor of the newer human edit unless it is clearly stale, and
   push again. Never force-push a wiki.

## Final report

Report:

- Repository, default branch, the branch that was reviewed, and any branch or
  uncommitted-work warnings that were raised.
- Wiki URL and local wiki clone path.
- Pages kept, updated, rewritten, created, merged, and deleted, with counts.
- Contradictions found between code and existing documentation that need a
  project change.
- Whether the wiki commit and push happened, the wiki commit hash when one
  exists, and the approval checkpoint that authorized it.
- Anything left for the user, such as enabling the wiki, creating the first
  page in the browser, or pushing the prepared commit.
