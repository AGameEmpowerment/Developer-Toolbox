# AI Engineering Constitution

This repository is a local developer toolbox and multi-language scaffolding template. C# (.NET), Java, JavaScript (Node.js, including TypeScript), and Python are first-class primary languages. It provides reusable examples, containerized services, infrastructure templates, and development tooling. This constitution guides all AI coding tools consistently and delegates specialized concerns to `.ai/instructions/`.

`AGENTS.md`, `CLAUDE.md`, and `.github/copilot-instructions.md` are discovery
entry points. This file is the authoritative repository-wide policy. Vendor
entry points must redirect here and must not maintain divergent copies.

---
## Priority Guidelines

When generating code for this repository or any project created from it:

1. Version Compatibility: Detect concrete language, framework, and library versions from the target project after scaffolding (do NOT assume versions beyond what is declared).
2. Instruction Files First: Always consult specialized instruction files in `.ai/instructions/` (security, performance, accessibility, architecture, etc.) before applying external heuristics.
3. Codebase Patterns: After a project is seeded, scan existing source folders for naming, error handling, testing, dependency injection, logging, and configuration patterns. Mirror them exactly.
4. Architectural Consistency: Maintain a Mixed architecture style (this template can evolve into layered, DDD, microservices, or serverless). Never introduce architectural boundaries not already established.
5. Code Quality: Treat maintainability, performance, security, accessibility, and testability as equal priorities (All). If a trade-off is required, prefer clarity + security + testability, then performance.
6. Language Support: Shared resources must expose language-neutral protocols or supported SDKs for all four primary languages. Keep SDK versions and idiomatic implementation choices specific to each language rather than treating one language's framework as universal. Examples and validators run independently outside the shared container collection, using only the developer's chosen language SDK.
7. Local Dependencies: Prefer free, open-source dependencies. Every library, package, image, container, and other dependency must be usable for the intended local development without a licensing barrier. Free developer editions are permitted even when production editions are paid. Verify licenses and document feature limitations; default startup must not require a purchased license or real cloud credentials.

### Shared Containers And Independent Examples

- `containers/docker-compose-{common,azure,aws}.yml` split shared infrastructure resources and their required initialization jobs into common, Azure, and AWS projects. Do not bundle example applications, Functions/Lambda handlers, SDK validators, or sample queue dispatchers into the shared image collection.
- Creating the shared collection must require neither a host .NET SDK nor a host Java/JDK installation. Support developers with either SDK, another primary language, or no application SDK. Use an ephemeral tool from an existing infrastructure image when setup needs a utility unavailable on the host.
- Developers can build and run an example or validator outside the collection to exercise its services. Keep each language's build, unit tests, and live resource checks independently runnable; no developer must install every primary language to validate their chosen examples.
- Local endpoint guards must validate the effective destination selected by the installed SDK. When a separate validator parses raw connection strings, reject ambiguous duplicate or noncanonical keys instead of validating a different effective connection; regressions must check the SDK constructor's selected destination without contacting a cloud endpoint.
- Dotenv setup must preserve explicit credentials and paths, including literal hashes. Test both shell implementations against Compose's quoting and inline-comment behavior before changing placeholder recognition.

### Dependency License Rules

- Prefer dependencies that are both open source and free for the intended developer-toolbox use. This applies to libraries, packages, images, containers, tooling, and their included dependencies.
- The primary requirement is unblocked local development: every selected dependency must permit the intended use without purchasing a license. A free developer edition is allowed even if the production product is proprietary and paid; SQL Server Developer is an example. Check organizational/developer eligibility and required license terms, rather than assuming that "free to download" permits the intended use.
- Comply with the dependency's actual license, including applicable notices, attribution, and redistribution conditions. Document the license and source of newly adopted dependencies.
- Do not add dependencies requiring paid licenses, license tokens, or paid cloud accounts to default local startup. Do not pin obsolete versions to evade licensing requirements.
- Do not adopt dependencies requiring payment or an unavailable license for local development. Select a free, open-source alternative or a freely licensed developer edition instead. A paid production offering alone does not disqualify its free developer edition.
- If a dependency's license, eligibility, or permission for the intended developer use is unclear, ask the user before adopting it. Identify the dependency and what is uncertain; do not guess. If terms conflict with these rules, report the conflict and resolve it with the user before proceeding. Do not invent license permissions or bypass restrictions.

---
## Technology Version Detection

Because this template does not yet define concrete application code, AI assistants MUST dynamically detect versions post-seeding:

1. Language Versions
	- For .NET: Inspect `.csproj` `<TargetFramework>` / `<LangVersion>` or global.json. Only use C# features available in that language version.
	- For Node/React/TypeScript: Inspect `package.json` engines, dependency versions, `tsconfig.json` (target/module/lib). Respect configured ECMAScript/TypeScript targets.
	- For Java: Inspect `pom.xml`, Gradle toolchains, compiler release settings, and the supported Functions/Lambda runtime version.
	- For Python: Read `pyproject.toml` / `requirements.txt` / runtime spec.
2. Framework Versions
	- ASP.NET Core / Blazor: Detect from NuGet package versions and SDK references.
	- React / React DOM / React Router: Use only documented APIs present in detected versions.
3. Library Versions
	- Always verify existence of an API before suggesting usage (no speculative future features).
	- When absent or ambiguous, generate conservative, widely supported patterns.

Never introduce language or framework features that exceed detected versions. Always annotate version assumptions in comments ONLY when non-obvious (e.g., performance-critical or security-sensitive code paths) using minimal, purpose-driven commentary.

### Container Image Versions and Pulls

The architect set this policy on 2026-10-25 for Toolbox and the projects that
use it. The full text, with examples, is in
[docs/guides/container-image-policy.md](../docs/guides/container-image-policy.md).

1. Development and test containers use `latest` by default. This covers
   everything that only runs locally or in test environments: OneBox,
   Testcontainers, the Aspire AppHost, the test runner, Playwright browsers,
   service mocks (smtp4dev, SQL Server, WireMock, Seq, Keycloak), Ryuk and
   helper containers, and Toolbox-managed devcontainers. No digest pins, and no
   tests whose only job is to enforce pins. The one exception is the
   devcontainer: it may keep pinned versions, including its
   `devcontainer-lock.json` feature lock.
2. Pin only when a tool needs a matching version, and then by tag, not digest.
   Each pinned tag gets a one-line comment explaining why. Today that means
   Playwright (the browser image matches the Playwright package version, for
   example `v1.63.0-noble`), the .NET SDK (`10.0`, because the projects target
   .NET 10), Node (`24` only if the build requires it, otherwise latest/LTS).
   A product-line tag such as the Cosmos DB
   Emulator's `vnext-latest` is not a pin.
3. Production images stay pinned. Any image that is deployed, or is the base
   for something deployed, stays pinned, by digest if that is what is there
   today. That includes the Server runtime image the Build-Docker pipeline
   publishes and any other base that feeds a shipped image.
4. The Keycloak OIDC mock (the Okta stand-in) is
   `quay.io/keycloak/keycloak:latest`. That is and stays the accepted version
   in ExamplePortal and Toolbox.
5. Reuse local images. A local copy of a tag is reused rather than pulled on
   every run. It is pulled only when it is missing, with the registry mirror as
   fallback. A local copy is refreshed with an explicit `docker pull`. No step
   forces a pull, and no helper images get pulled.
6. Keep it simple. If pinning is not clearly required by one of these rules,
   do not pin.

Separate rule: always keep the latest `mcp/seq-otlp` image on the machine.

Container image sources must prefer the primary approved publisher: official
Docker Hub images first when available, then Microsoft Container Registry or
GitHub Container Registry for Microsoft/GitHub-published assets. Use the registry
mirror as the fallback when the primary source is unavailable or
organizational policy requires the mirror.

---
## Context & Specialized Instruction Files

Prioritize these (if present) in order:

1. `.ai/instructions/security-and-owasp.md` (security baseline)
2. `.ai/instructions/performance-optimization.md`
3. `.ai/instructions/a11y.md`
4. `.ai/instructions/devops-core-principles.md`
5. `.ai/instructions/dotnet-architecture-good-practices.md` (when .NET code exists)
6. Other technology-specific instruction files (MAUI, WPF, Terraform, etc.) as the stack expands

If a conflict arises between a specialized file and this general file, defer to the specialized file. Do NOT merge contradictory guidance; follow the more specific domain rules.

---
## Codebase Scanning Instructions (Post-Seeding)

When the generated project contains actual source code:

1. Identify comparable files (e.g., controllers, React components, services, repositories) before adding new ones.
2. Analyze patterns:
	- Naming: Match existing casing conventions (PascalCase for C# types, camelCase for JS/TS variables, kebab-case for folders when present).
	- Error Handling: Mirror existing approach (e.g., exceptions + middleware in ASP.NET, try/catch with logging in Node, error boundaries in React).
	- Logging: Use the same abstractions (e.g., `ILogger<T>` in .NET, structured logging libraries in Node) with consistent log levels.
	- Configuration: Centralize configuration (e.g., `appsettings.*.json` / environment variables / `.env` files) following existing patterns.
	- Dependency Injection: Reuse current DI container idioms (e.g., `builder.Services.Add...` in ASP.NET, constructor injection in classes, React Context providers, or inversion via hooks).
	- Testing: Match test folder structure, assertion libraries, naming conventions (`Given_When_Then`, descriptive `it()` blocks, or `[Fact]`/`[TestMethod]`).
3. Resolve conflicting patterns by prioritizing:
	1. Most recent files
	2. Files with higher explicit test coverage
	3. More specialized instruction files
4. Do not introduce patterns absent from the existing codebase (e.g., new logging frameworks, alternate DI containers, speculative architecture layers).

---
## Code Quality Standards

### Maintainability
- Self-document through clear function/class naming; avoid redundant comments.
- Single responsibility per function/class; refactor large multi-purpose functions.
- Keep public interfaces minimal and explicit.

### Performance
- Apply optimizations only where measured bottlenecks exist (no premature optimization).
- Use asynchronous, non-blocking I/O in web/server code.
- Reuse dependency-injected singletons for expensive resources (DB clients, HTTP clients).

### Security
- Enforce least privilege; deny by default for protected operations.
- Use parameterized queries / ORM-safe APIs; never concatenate user input into queries.
- Never hardcode secrets—use environment variables or secret managers.
- Sanitize/validate all external input (HTTP, CLI, event bus). Output encode for UI contexts.

### Accessibility (UI Code)
- Use semantic HTML elements first; ARIA only to fill gaps.
- Ensure keyboard navigability for all interactive components; manage focus intentionally.
- Provide text alternatives for all informative graphics.

### Testability
- Favor pure functions or clear side-effect boundaries.
- Inject dependencies (do not instantiate concrete implementations inside business logic).
- Keep tests deterministic and isolated; avoid shared mutable state.

---
## Documentation Requirements (Standard)

- Mirror existing documentation style (once code exists).
- For C# use XML doc comments only for public APIs that require clarity.
- For JS/TS use JSDoc only where complex behavior needs explanation.
- Comment WHY not WHAT: design constraints, performance trade-offs, security assumptions.
- Use annotation tags (`TODO`, `FIXME`, `SECURITY`, `PERF`) sparingly and purposefully.

---
## Testing Approach (All)

### Unit Testing
- Use NUnit for .NET tests and NSubstitute when mocks are needed. Use native NUnit constraint assertions and await asynchronous assertions. Preserve per-case isolation; do not enable parallel execution for tests that mutate process-wide state. Jest/Vitest and React Testing Library remain the JavaScript/TypeScript tools.
- One conceptual expectation per test method/block.

### Integration Testing
- Exercise real wiring: database connections (containerized), service bus, HTTP middleware.
- Use container orchestration (Docker Compose) for ephemeral dependencies where practical.

### End-to-End Testing
- For UI stacks (React/Blazor), simulate user flows (e.g., Playwright/Cypress). Reuse accessible selectors (role, name) rather than brittle DOM paths.

### TDD / BDD (If Practiced)
- When patterns show Given/When/Then, preserve that narrative style.
- Add tests before implementing complex business logic or cross-cutting concerns.

---
## Technology-Specific Guidelines (Template Baseline)

### .NET / C# / ASP.NET Core / Blazor
- Detect target frameworks (e.g., `net8.0`, `net9.0`)—never assume higher.
- Use async/await per existing pattern; avoid blocking calls (`.Result` / `.Wait()`).
- Register services in a centralized composition root (e.g., `Program.cs` builder pipeline).

### JavaScript / TypeScript / React
- Respect `tsconfig.json` compiler targets; avoid unsupported ECMAScript proposals.
- Use function components with hooks unless existing code prefers classes.
- Keep state colocated; escalate to context/state managers only when reuse or cross-cut concerns justify it.

### Java
- Detect the declared JDK and Maven/Gradle versions; use supported cloud-runtime releases for serverless examples.
- Reuse SDK clients, close resources, and propagate failures so queue consumers can retry safely.
- Use JUnit or the existing project test framework, with deterministic unit tests and separate live-container validation.

### Python
- Detect the declared Python runtime and dependency versions; keep environment dependencies isolated.
- Use supported SDKs and context managers for resource lifetimes; validate event payloads before side effects.
- Use unittest, pytest, or the existing project framework; separate unit tests from live-container validation.

### Infrastructure (Terraform / DevOps)
- Follow existing folder naming (`terraform/`, `devops/pipelines/`).
- Parameterize environment-specific values; avoid hardcoding.
- Keep reusable modules small and composable.

### PowerShell / Scripting
- Maintain idempotent setup scripts (`docker_setup.ps1`, `docker_down.ps1`).
- Validate required tooling before execution (e.g., Docker daemon running) using defensive checks.

---
## Version Control & Semantic Versioning

- This template currently uses semantic versioning (`0.0.x` in `CHANGELOG.md`). Continue incrementing: MAJOR (breaking), MINOR (features), PATCH (fixes).
- Record changes in `CHANGELOG.md` with concise bullet entries; highlight breaking changes clearly.
- Tag releases accordingly (e.g., `v0.1.0`).

---
## General Best Practices

- Consistency > novelty. Prefer existing patterns in seeded projects.
- Avoid over-abstraction early; refactor only after duplication is measured.
- Centralize cross-cutting concerns (logging, validation, auth) before feature code depends on them.
- Keep configuration externalized (environment vars, config files) and never commit secrets.
- Provide accessible, secure defaults; opt-in to advanced features explicitly.

---
## Project-Specific Guidance

- Because this repository is a starter template, AI assistants must refrain from inventing architectural layers or technology choices not present in the target initialized project.
- After seeding a new project: perform an initial scan (files, package manifests, project files) before generating new code.
- If no pattern exists yet (e.g., first service class), generate a minimal, conventional implementation and let subsequent files reinforce it—do not prematurely generalize.
- When specialized instructions conflict with generic performance or style advice, specialized instructions win.
- When asked to review or address pull request comments, handle the review end-to-end: search broadly for hidden and outdated comments, fix active relevant comments, add a resolution note explaining each resolved thread, mark remaining comments resolved, and commit the local PR-review changes without waiting for a second confirmation. Pushing to a remote still requires explicit user approval.

## Repository Skills Discovery

- Use `.ai/skills/INDEX.md` as the canonical discovery map for skill usage.
- Prefer Skills CLI (`npx skills`) for skill discovery and installation workflows.
- Use `npx skills find <query>` for discovery and `npx skills add <owner/repo@skill> -g -y` for global install.
- Search `.agents/skills/` and `.claude/skills/` as directed by
  `.ai/skills/INDEX.md` before concluding that no repository skill applies.
- Create or install repository-local Codex skills under `.agents/skills/` and
  repository-local Claude skills under `.claude/skills/`; do not use
  `.codex/skills/` or `.ai/skills/` for installable skill content.
- Default skill creation and installation to repository-local scope unless the
  user explicitly requests a global/user installation.
- If a `find-skills` capability is available in the environment, prefer it for discovering relevant skills before inventing project-local automation; otherwise, fall back to `npx skills find`.

---
## Examples (Template Context)

Since there is little source code, examples derive from existing repository assets:

- Container orchestration lives under `containers/` using Docker Compose—future services should integrate via shared networks/compose overrides rather than ad-hoc scripts.
- Scripts (`*.ps1`) demonstrate imperative environment setup; future automation should prefer declarative provisioning (Terraform) where possible, respecting existing directory layout.
- Documentation style: top-level `README.md` uses descriptive sections with fenced code blocks and warnings—follow that clarity style for future root-level docs.

---
## When Uncertain

If ambiguity exists (e.g., multiple equally valid patterns or missing conventions):
1. Choose the simplest, widely supported approach.
2. Add a terse `TODO` note describing the assumption.
3. Defer complex pattern introduction until concrete requirements emerge.

---
## Out of Scope

Do NOT:
- Introduce unrequested third-party frameworks (e.g., swapping logging libraries) without precedent.
- Use experimental language features not enabled by project configuration.
- Generate verbose boilerplate comments explaining obvious code.

---
## Enforcement Summary

AI assistants MUST:
- Scan for versions & patterns before code generation.
- Align with specialized instruction files for domain concerns.
- Favor clarity, security, testability, and accessibility while preserving performance.
- Avoid speculative architecture, dependencies, or APIs.

AI assistants SHOULD:
- Propose incremental refactors only after patterns stabilize.
- Surface security/performance/accessibility considerations succinctly when deviations are necessary.

AI assistants MAY:
- Insert minimal doc comments for public APIs in emerging codebases.
- Suggest test scaffolds to bootstrap coverage early.

---
## Final Note

This file sets general guardrails. For any domain-specific work (security, accessibility, performance, architecture patterns), always consult the corresponding `.ai/instructions/*.md` file first. Consistency and explicit pattern detection are the foundation—no assumptions beyond observable artifacts.

