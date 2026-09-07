# GitHub wiki mechanics

Facts about how GitHub wikis work that the workflow depends on. Verify anything
that looks repository-specific against `gh repo view` output before relying on it.

## Where the wiki lives

| Item | Value |
|---|---|
| Web URL | `https://github.com/<owner>/<repo>/wiki` |
| Git remote | `https://github.com/<owner>/<repo>.wiki.git` (SSH: `git@github.com:<owner>/<repo>.wiki.git`) |
| Default wiki branch | Usually `master`, even when the project uses `main`. Check with `git symbolic-ref --short HEAD` inside the clone. |
| Enabled flag | `gh repo view --json hasWikiEnabled` or `gh api repos/<owner>/<repo> --jq .has_wiki` |

The wiki is a separate git repository. Project branches, pull requests, and
CODEOWNERS do not apply to it. Anyone with write access to the repository can
push directly to the wiki, and every push publishes immediately.

## What the GitHub CLI can and cannot do

`gh` has no wiki subcommand and the REST and GraphQL APIs expose no wiki page
endpoints. Use `gh` for:

- Authentication state: `gh auth status -h github.com`.
- Repository identity and settings: `gh repo view --json nameWithOwner,url,description,defaultBranchRef,hasWikiEnabled,isPrivate,visibility`.
- Project history and metadata: `gh release list`, `gh pr list --state merged`,
  `gh api repos/{owner}/{repo}/languages`, `gh api repos/{owner}/{repo}/topics`.
- Reading files on another branch without checking it out:
  `gh api repos/{owner}/{repo}/contents/<path>?ref=<branch> --jq .content | base64 -d`
  (or `git show origin/<branch>:<path>` from a fetched clone, which is simpler).

Use git for everything that touches wiki content: clone, pull, commit, push.
`gh repo clone <owner>/<repo>.wiki` is not reliable; use `git clone` with the
full `.wiki.git` URL. Because `gh` configured git credentials for GitHub,
`git clone` and `git push` against the wiki remote reuse the `gh` login.

## Clone location

Never clone the wiki inside the project working tree. It would show up as an
untracked folder in the project's `git status` and could be committed by
mistake. Preferred locations, in order:

1. A sibling folder next to the project: `../<repo-folder>.wiki`. If it
   exists, confirm `git -C ../<repo-folder>.wiki remote get-url origin` points
   at the same wiki, then `git pull --ff-only`.
2. The operating system's temporary directory (`$env:TEMP` on Windows,
   `$TMPDIR` or `/tmp` elsewhere) under a folder named after the repository.

## Empty and disabled wikis

- `hasWikiEnabled: false`: the wiki feature is off. Only a repository admin
  can turn it on (Settings, Features, Wikis). Stop and report.
- Enabled but cloning fails with `repository not found` or an authentication
  prompt even though `gh auth status` is fine: the wiki has no pages yet.
  GitHub creates the wiki git repository the first time a page is saved in
  the browser. Ask the user to create a Home page at `<url>/wiki`, then clone.
- Restricted editing: repository settings can limit wiki edits to
  collaborators. A rejected push with a permissions error means the current
  account lacks write access; report it rather than retrying with other
  credentials.

## Page files and names

- Every page is a Markdown file at the root of the wiki repository. GitHub
  also accepts AsciiDoc, Org, RDoc, and other markups, but new pages should be
  Markdown (`.md`).
- The file name is the page name. Hyphens render as spaces:
  `Getting-Started.md` is the page "Getting Started" at `/wiki/Getting-Started`.
- Avoid these characters in file names: `/ \ : * ? " < > |` and leading dots.
  Avoid spaces; use hyphens.
- `Home.md` is the landing page shown at `/wiki`.
- `_Sidebar.md` renders in the right-hand sidebar on every page and
  `_Footer.md` renders under every page. Both are optional. There is no
  `_Header.md`.
- Subfolders are allowed for assets (for example `images/`), but keep pages at
  the root so page names and links stay predictable.
- Renaming a page is a `git mv`. Update every link that pointed at the old
  name in the same commit.

## Links

| Purpose | Syntax |
|---|---|
| Link to another wiki page by title | `[[Getting Started]]` (GitHub converts spaces to hyphens) |
| Link with custom text | `[[Install the plugin\|Getting-Started]]` |
| Standard Markdown link to a page | `[Getting Started](Getting-Started)` (relative, no `.md`) |
| Link to a section on a page | `[Install](Getting-Started#install)` |
| Link to a file in the project | `https://github.com/<owner>/<repo>/blob/<default-branch>/<path>` |
| Image stored in the wiki | `![Alt text](images/diagram.png)` with the file committed under `images/` |

A wiki link whose target page does not exist renders as a red "create page"
link. The bundled inventory script reports these as broken links.

## Rendering notes

- GitHub Flavored Markdown applies: tables, task lists, fenced code blocks
  with language hints, and Mermaid diagrams in ```` ```mermaid ```` blocks all
  render.
- Raw HTML is sanitized; do not rely on it.
- A table of contents is not generated automatically. The sidebar is the
  navigation surface, so keep it complete and ordered.
- The wiki search indexes page titles and bodies. Descriptive titles matter
  more than long pages.

## Commit and push conventions

- Commit in the wiki clone with a clear message such as
  `docs(wiki): refresh project documentation for a1b2c3d on main`.
- Push with `git push origin HEAD` so the current wiki branch name does not
  need to be guessed.
- Never force-push. If the remote moved, `git pull --rebase`, reconcile, and
  push again.
- After pushing, confirm the remote head matches the local commit:

  ```powershell
  git rev-parse HEAD
  git ls-remote https://github.com/<owner>/<repo>.wiki.git HEAD
  ```


