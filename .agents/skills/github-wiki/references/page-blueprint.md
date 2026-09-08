# Wiki page blueprint

The target shape for a complete project wiki, distilled from the reference wikis listed in
[example wikis](example-wikis.md). Use it as a checklist, not a template to fill blindly: create a
page only when the repository has real content for it, and merge thin pages together. Page names
are shown as file names; hyphens render as spaces.

## Overall shape

| Property | Target |
|---|---|
| Page count | 10 to 25 pages for a typical service, library, or framework. Fewer than 8 usually means topics were merged that deserve their own page; more than 30 usually means duplication. |
| Page length | 80 to 300 lines. Long enough to be the complete answer for its topic; split when a page needs more than about six top-level sections. |
| Headings | Start each page with `##` sections. Do not add an `# H1`; GitHub renders the page title from the file name. |
| First section | A one- or two-paragraph `## Purpose` or an untitled lead paragraph stating what the page covers and who it is for. |
| Last sections | `## Related Pages` (links to the two to five most relevant pages) and, on pages that change often, `## Revision Notes` with dated bullets. |
| Dating | Every page carries `Last reviewed: YYYY-MM-DD.` near the top, or a sentence such as "This page reflects `main` at commit `abc1234` on 2026-08-21." |
| Tables | The default for any inventory: paths, settings, requirements, tasks, pipelines, tests, packages. Prose explains; tables enumerate. |
| Commands | Every command in a fenced block with a language hint, copied from the repository or verified to run. Show Windows PowerShell and Bash variants only where they differ. |
| Diagrams | At least one Mermaid `flowchart` on the architecture page, followed by a short text description of the same flow for readers who cannot see the diagram. |
| Numbers | Exact counts taken from the checkout: tracked files, projects, tests discovered, pages, services, pipelines. Counts make staleness visible on the next refresh. |
| Links | Relative Markdown links between pages: `[Getting Started](Getting-Started)`. Repository files use `https://github.com/<owner>/<repo>/blob/<default>/<path>` (or the relative `../blob/<default>/<path>`). |
| Honesty | Name what is stale, missing, disabled by default, or a template rather than production-ready. Use callouts such as `> **Project boundary:**` on the Home page when the repository is a toolbox, starter, or example. |

## Home page anatomy

`Home.md` is the most-read page and follows a consistent order:

1. **Lead paragraph.** What the repository is, in the project's own vocabulary, and who the wiki is
   for. Add a boundary callout if the contents are templates or examples.
2. **Review line.** "Last reviewed with `gh` and the local repository on 2026-08-21, against
   `main` commit `c78c8d2`."
3. **Quick Facts / Current baseline table.** Repository, visibility, default branch, branch and
   commit reviewed, primary solution or entry point, runtime targets, key framework and package
   versions, latest release, and headline counts (projects, tests discovered, services, pipelines).
4. **Start Here / Choose your starting point table.** Two columns: the task a reader has, and the
   page that answers it. Every content page appears exactly once.
5. **Architecture at a glance.** A short paragraph and a Mermaid diagram of the main components,
   or a link to the architecture page when the diagram is large.
6. **Fast Start.** The three to six commands that take a fresh clone to a running or tested state,
   when the repository has such a path.
7. **Authoritative sources.** Which files win when documentation disagrees (solution and project
   files, manifests, pipeline YAML, `AGENTS.md`), and a link to the source-index page if one exists.

## Sidebar and footer

- `_Sidebar.md` lists every page, grouped under three to five short bold or `###` headings in
  reading order, for example **Start Here**, **Build / Use**, **Operate**, **Maintenance**. Home is
  always first. Nested bullets are fine for a catalog page under its parent.
- Optionally end the sidebar with a **Project links** group: `[Repository](../)`,
  `[Issues](../issues)`, `[Pull requests](../pulls)`, `[Releases](../releases)`.
- `_Footer.md` is optional and, when present, is one line naming the documentation scope and the
  authoritative branch, for example "Internal documentation for `<repo>`. The `main` branch is
  authoritative."

## Core pages (every project)

| Page file | Purpose | Draw from |
|---|---|---|
| `Home.md` | See the anatomy above. | `README*`, `gh repo view`, manifests |
| `_Sidebar.md` | Grouped navigation for every page. | The final page plan |
| `Getting-Started.md` | Prerequisites table (requirement, purpose), clone, verify SDKs and package sources, restore/build, run tests, first meaningful run, and a **Verify the setup** checklist of expected results. | `README*`, setup scripts, dependency manifests |
| `Repository-Structure.md` or `Repository-Map.md` | Top-level path table (path, responsibility, canonical or generated), active projects, documentation layout, DevOps layout, agent and skill layout, and a **Stale or historical surfaces** section. | Inventory script tree, `git ls-files` |
| `Architecture.md` | Components, dependency chain, runtime lifecycle, and data flow with a Mermaid diagram plus text description; finish with **Current architecture tensions** when trade-offs exist. | Source code, project references |
| `Configuration-and-Secrets.md` | Configuration providers and precedence, every setting by name with purpose and default, environment variables, where secrets live (user secrets, Key Vault, variable groups) and what must never be committed. | `appsettings*`, `.env.example`, options classes |
| `Troubleshooting.md` | One `##` per failure mode, each with **Symptoms** and numbered **Checks**. Always include a "GitHub wiki access or push fails" entry and a final **Prepare an escalation** section. | Issues, CI logs, README notes |
| `Contributing.md` or `Contributing-and-Governance.md` | Branching, commit and PR conventions, minimum validation before a PR, ownership and review routing, security and privacy rules, and a **Maintain the wiki** section describing how this wiki is refreshed. | `CONTRIBUTING*`, `CODEOWNERS`, PR templates |

## Pages by project shape

Choose the block that matches the repository. Mixed repositories take pages from more than one.

### Application or service

| Page file | Purpose |
|---|---|
| `Local-Development.md` | Running, debugging, seeding data, ports, and local pitfalls. |
| `Local-Development-Services.md` or `Docker-and-Local-Services.md` | Compose services table (service, image, ports, purpose), environment file, start/stop/verify, certificate trust. |
| `API-and-Routes.md` | Endpoints grouped by resource with request and response shapes, or a link to generated docs and how to regenerate them. |
| `Data-and-Integration-Model.md` | Entities, stores, external integrations, and message flows. |
| `DevOps-and-Deployment.md` or `CI-CD-Pipelines.md` | Every pipeline definition, its trigger, stages, lanes and gates, required variable groups and service connections by name, and how to validate a pipeline change. |
| `Observability-and-Operations.md` | Health endpoints, logs, correlation IDs, dashboards, alerts, and runbooks. |
| `Security-and-Compliance.md` | Authentication model, secret handling, redaction, dependency scanning, and review expectations. |
| `Performance-and-Scalability.md` | Only when the repository has measured targets or documented limits. |

### Library, SDK, or shared framework

| Page file | Purpose |
|---|---|
| `Overview.md` and `Project-Inventory.md` | What ships (packages, tools, targets) and the exact solution, project, and tooling inventory with versions. |
| `Framework-Usage.md` | The most common consumer tasks with complete, runnable examples. |
| `Consumer-Extension-Patterns.md` | Supported extension points and the patterns to avoid. |
| `Configuration-Reference.md` | Every option a consumer can set, grouped by section. |
| `Testing-Guide.md` | How to run and extend the suites, categories, and filters. |
| `Migration-Guide.md` | How to move an older consumer to the current version. |
| `Build-and-Release.md` | Versioning, packaging, publishing, and release notes. |

### Test automation framework

| Page file | Purpose |
|---|---|
| `Site-and-Test-Catalog.md` or `Descriptor-Authoring-Guide.md` | Every configured target and test asset type, where each lives, and how to add one. |
| `Writing-Automation.md` | Step-by-step recipe for adding a page, fixture, or scenario, ending with "update documentation". |
| `Running-and-Debugging-Tests.md` | Discover, filter, select browsers or lanes, run headed, override targets, interpret failures. |
| `Browser-Captures-and-Diagnostics.md` | Capture triggers, artifact locations, reports, and how to protect evidence. |
| `Runners-and-Pipelines.md` | Runner boundaries, routing contracts, pipeline names, and known path-rule drift. |

### Marketplace, monorepo, toolbox, or catalog

| Page file | Purpose |
|---|---|
| `Plugin-Catalog.md`, `AI-Asset-Catalog.md`, or `Catalog.md` | Every item with name, path, owner, purpose, and version, grouped by category with counts in the headings. |
| `Ownership-and-Security.md` | Who owns and reviews each area, how routing works, and credential rules. |
| `Maintenance-and-Validation.md` | Audit scripts, validators, and the exact commands to run before merge, with the latest baseline result. |
| `Customization-Guide.md` or `Template-Customization.md` | How a downstream adopter prunes and adapts the repository. |
| `Migration-and-Troubleshooting.md` | Renames, merges, removals with replacements, plus common install and discovery failures. |

### Repositories with AI agent assets

| Page file | Purpose |
|---|---|
| `AI-Assisted-Development.md` or `Agent-and-Copilot-Guidance.md` | Instruction precedence (`AGENTS.md`, `CLAUDE.md`, `.ai/`, `.agents/skills/`), canonical versus generated adapters, how to sync them, and repository and data boundaries agents must respect. |

## Meta pages (strongly recommended for every refresh)

These pages are what make the reference wikis trustworthy. Include at least the first two.

| Page file | Purpose |
|---|---|
| `Current-State-and-Limitations.md` or `Review-and-Known-Issues.md` | Implemented coverage table with evidence, known functional gaps, documentation drift found between code and docs, operational limitations, and prioritized next work. Separates what works from what is planned. |
| `Source-Document-Index.md` or `Review-Findings.md` | The inputs used for this refresh (wiki clone, `gh` metadata, branch and commit, files read), a **Highest authority sources** table, commands run with their verified results, checks that were blocked and why, conflict-resolution rules, and a maintenance checklist for the next refresh. |
| `Change-Log.md` | Dated milestone table complementing the root `CHANGELOG`, plus a "What the <date> wiki refresh changed" section. |
| `Quality-Roadmap.md` or `Requirements-and-Decision-Log.md` | Strengths to preserve, highest-priority risks, phased priorities, definition of done, and open questions. Only when the repository or its owners have expressed direction; never invent a roadmap. |
| `Glossary.md` | Domain terms and acronyms a newcomer would not know. |
| `Visual-Diagrams.md` | A gallery of Mermaid diagrams when more than two or three exist. |

## Quality checklist for every page

- No `# H1`; the first heading is `##`. Headings are consistent within the wiki: either noun
  phrases ("Runtime Layers") or imperative tasks ("Override a named site"), not a mix.
- `Last reviewed` date or reviewed-commit sentence is present and absolute (`2026-09-05`).
- Every command is copied from the repository or verified to run, and sits in a fenced code block
  with a language hint.
- Every wiki link resolves to a page in the plan; every repository link uses the default branch.
- Tables enumerate; prose explains. No table has a single row, and no inventory is a paragraph.
- No secrets, tokens, personal email addresses, or machine-specific paths.
- Stale, disabled, historical, or template-only content is labelled as such.
- Content that only restates another page is replaced by a link to it.
- `## Related Pages` closes the page, and the sidebar lists the page exactly once.
