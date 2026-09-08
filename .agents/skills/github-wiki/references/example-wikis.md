# Example wikis

These wiki archetypes are guidelines, not templates. Match their depth,
structure, and honesty, then adapt the page set to the repository at hand.

| Wiki | Pages | What it demonstrates best |
|---|---|---|
| [Developer-Toolbox](https://github.com/AGameEmpowerment/Developer-Toolbox/wiki) | 14 | A toolbox or starter repository. `Home` opens with a **Local development only** boundary callout, a goal-to-page table, an inventory table of top-level areas, and a **Repository Snapshot** with exact counts. `Review-Findings` records commands, verified results, blocked checks, and remaining issues. |
| Test platform | 10 | `Home` carries a **Reviewed repository state** table. `Source-Document-Index` lists review inputs, authority rules, and a maintenance checklist. `Quality-Roadmap` gives phased priorities and a definition of done. |
| Shared automation framework | 27 | Sidebar grouped into **Start Here**, **Using The Stack**, **Supporting Systems**, and **Maintenance**. Inventory, configuration, extension, migration, and change-log pages serve downstream users. |
| Full-stack starter | 19 | Sidebar grouped into **Codebase**, **Working In The Repo**, and **Delivery**. Pages carry review dates, related pages, revision notes, and symptom/check troubleshooting entries. |
| Browser automation suite | 18 | `Home` has a current-baseline table, task-to-page table, accessible Mermaid architecture diagram, and authoritative-sources section. Current state is separated from known gaps and pipeline drift. |

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
