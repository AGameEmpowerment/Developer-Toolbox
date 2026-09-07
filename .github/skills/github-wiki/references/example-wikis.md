# Example wikis

These wiki archetypes are guidelines, not templates. Match their depth,
structure, and honesty, then adapt the page set to the repository at hand.

| Wiki | Pages | What it demonstrates best |
|---|---|---|
| [Developer-Toolbox](https://github.com/AGameEmpowerment/Developer-Toolbox/wiki) | 14 | A toolbox or starter repository. `Home` opens with a **Local development only** boundary callout, a goal-to-page table, an inventory table of top-level areas, and a **Repository Snapshot** with exact counts. `Review-Findings` records the commands run, verified results, blocked checks, and remaining issues. `AI-Asset-Catalog` groups assets by category with counts in headings. `Customization-Guide` tells adopters what to prune. |
| Test platform | 10 | `Home` carries a **Reviewed repository state** table (repository, visibility, default branch, branch and commit reviewed, solution, projects, target framework, package versions, active plan areas). `Source-Document-Index` lists review inputs, authority rules, and a maintenance checklist. `Runners-and-Pipelines` names **path rule drift** explicitly. `Quality-Roadmap` gives phased priorities and a definition of done. |
| Shared automation framework | 27 | Sidebar grouped into **Start Here**, **Using The Stack**, **Supporting Systems**, and **Maintenance**. `Project-Inventory` and `Configuration-Reference` are exhaustive tables. `Consumer-Extension-Patterns` and `Migration-Guide` serve downstream teams. `Change-Log` keeps dated milestones. |
| Full-stack starter | 19 | Sidebar grouped into **Codebase**, **Working In The Repo**, and **Delivery**. `Home` has **Quick Facts**, a numbered **Start Here**, and a **Fast Start** command block. Every page carries a review date and ends with related pages and revision notes. `Troubleshooting` uses a **Symptoms / Checks** pattern. |
| Browser automation suite | 18 | `Home` has a **Current baseline** table, a task-to-page table, an accessible Mermaid architecture diagram, and an **Authoritative sources** section. `Current-State-and-Limitations` separates implemented coverage from known gaps and pipeline drift. `Writing-Automation` is a numbered recipe that ends with updating documentation. |

## Patterns shared by all five

- Refreshed from the actual checkout, `gh` metadata, and the existing wiki clone, and the Home page
  says so with the date and commit.
- Tables for every inventory; prose only to explain.
- Exact counts (tracked files, tests discovered, pages, services, pipelines) rather than "several".
- Build, test, and validation commands were actually run during the refresh and their results
  recorded, with blocked checks named rather than skipped silently.
- Documentation drift and stale surfaces are called out, with the code treated as the source of
  truth.
- Relative Markdown links between pages (`[Title](Page-Name)`), no `[[wikilinks]]`.
- Wiki commits use messages such as `docs(wiki): refresh project documentation` or
  `Refresh wiki for current repository state`.

