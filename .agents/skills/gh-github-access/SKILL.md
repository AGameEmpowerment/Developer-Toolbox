---
name: gh-github-access
description: Use the GitHub CLI for authenticated, repository-grounded GitHub access from Codex, especially when the current working repo or an explicitly named repo needs private/internal GitHub data, enterprise SSO-protected wiki pages, issues, pull requests, releases, files, raw contents, or GitHub API resources. Use when a task needs `gh auth`, `gh repo`, `gh pr`, `gh issue`, `gh release`, `gh api`, or wiki clone workflows, and when existing GitHub access techniques should be preferred over copying tokens or asking for secrets.
---

# GitHub CLI Access

## Core Workflow

Prefer authenticated `gh` commands when GitHub resources are private,
internal, enterprise SSO-protected, or easier to query structurally through the
GitHub API.

Ground every GitHub action in a repository:

```powershell
gh auth status
gh repo view --json nameWithOwner,description,url,visibility,defaultBranchRef
git remote -v
```

Use the current working repository when the user asks about "this repo", local
files, the current branch, the current PR, or repo-local docs and skills. Use
an explicit `OWNER/REPO` only when the user names a different repository or
the current working tree does not identify the target.

If authentication is missing or the active account cannot see the resource,
ask the user to authenticate or grant access. Do not ask the user for a token,
print tokens, commit tokens, or place tokens in scripts, docs, logs, or
environment files.

## Authentication

Use the existing authenticated session when available:

```powershell
gh auth status -h github.com
```

When login is required, prefer GitHub CLI's normal browser or device-code
flows instead of manual token handling:

```powershell
gh auth login -h github.com --web
```

If multiple GitHub accounts exist, inspect and switch accounts through `gh`
rather than changing credential files by hand:

```powershell
gh auth status
gh auth switch -h github.com
```

Avoid `gh auth token` unless a GitHub tool specifically requires a token and
there is no safer direct `gh` command. If a token must be used, keep it in
memory or an approved secret store and never display it.

## Repository Content

Prefer current-repo commands when already inside the target repo:

```powershell
gh repo view --json nameWithOwner,url,defaultBranchRef
gh api repos/{owner}/{repo}/contents --jq '.[] | [.type, .name, .path] | @tsv'
```

Use `gh api` for precise file and directory discovery:

```powershell
gh api repos/OWNER/REPO/contents --jq '.[] | [.type, .name, .path] | @tsv'
gh api repos/OWNER/REPO/contents/PATH/TO/DIR --jq '.[] | [.type, .name, .path] | @tsv'
gh api repos/OWNER/REPO/contents/PATH/TO/FILE --jq '.content' | certutil -decode -f - output.tmp
```

Use `gh repo clone` when many files must be searched locally:

```powershell
gh repo clone OWNER/REPO <temp-path> -- --depth 1
rg "pattern" <temp-path>
```

Clone into a clearly temporary location, keep generated clones out of commits,
and remove only the exact clone path you created after confirming it is inside
the intended workspace or temp directory.

## Wikis

For GitHub wikis, clone the `.wiki` repository. This is often the easiest way
to bypass browser SSO friction while still using the user's approved GitHub
session:

```powershell
gh repo clone OWNER/REPO.wiki <temp-path> -- --depth 1
Get-ChildItem <temp-path>
rg "pattern" <temp-path>
```

Read only the pages needed for the task. Summarize private wiki content rather
than copying large sections into source-controlled docs.

## Pull Requests And Issues

For current-repo PRs and issues, omit `--repo` unless the task names another
repo:

```powershell
gh pr view --json number,title,author,state,baseRefName,headRefName,body,files,commits,reviews,comments
gh issue list --state open --json number,title,author,updatedAt
```

Use structured JSON output when reviewing or summarizing:

```powershell
gh pr view NUMBER --repo OWNER/REPO --json title,author,state,baseRefName,headRefName,body,files,commits,reviews,comments
gh pr diff NUMBER --repo OWNER/REPO
gh issue view NUMBER --repo OWNER/REPO --json title,author,state,body,comments,labels
```

For lists, filter before reading details:

```powershell
gh pr list --repo OWNER/REPO --state open --json number,title,author,updatedAt
gh issue list --repo OWNER/REPO --state open --label LABEL --json number,title,author,updatedAt
```

Do not post comments, approve PRs, merge, close issues, dispatch workflows, or
mutate GitHub state unless the user explicitly asks for that action.

## Releases And Packages

Use releases commands for current version and asset discovery:

```powershell
gh release list --repo OWNER/REPO --limit 10
gh release view TAG --repo OWNER/REPO --json name,tagName,isLatest,publishedAt,url,assets
```

Download assets only when needed, and write them to a temporary or explicitly
requested location.

## Pagination And Rate Limits

Use pagination for API endpoints that can return many items:

```powershell
gh api --paginate repos/OWNER/REPO/pulls --jq '.[].number'
```

If rate limits or permissions block the task, report the exact limitation and
the command that exposed it without exposing secret values.

## Safety Rules

- Prefer `gh` over browser scraping for private/internal GitHub resources.
- Resolve the target repo from the current working tree before reaching for a
  hard-coded `OWNER/REPO`.
- Prefer `--json` and `--jq` over ad hoc parsing.
- Keep commands read-only unless mutation is explicitly requested.
- Never expose tokens, cookies, authorization headers, or private raw payloads.
- Avoid persisting downloaded GitHub content unless the task requires it.
- Keep temporary clones and downloads out of source control.
