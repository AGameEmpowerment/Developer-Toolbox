# GitHub Copilot Instructions (General Purpose Template)

This repository is a multi-language scaffolding template intended to seed new projects (e.g., .NET (C# / ASP.NET / Blazor), React, TypeScript/JavaScript frontends, infrastructure (Terraform), DevOps pipelines, and containerized services). It intentionally contains minimal source code. These instructions guide Copilot to operate consistently and to defer to the more specialized instruction files in `.github/instructions/` whenever generating or modifying code.

---
## Priority Guidelines

When generating code for this repository or any project created from it:

1. Version Compatibility: Detect concrete language, framework, and library versions from the target project after scaffolding (do NOT assume versions beyond what is declared).
2. Instruction Files First: Always consult specialized instruction files in `.github/instructions/` (security, performance, accessibility, architecture, etc.) before applying external heuristics.
3. Codebase Patterns: After a project is seeded, scan existing source folders for naming, error handling, testing, dependency injection, logging, and configuration patterns. Mirror them exactly.
4. Architectural Consistency: Maintain a Mixed architecture style (this template can evolve into layered, DDD, microservices, or serverless). Never introduce architectural boundaries not already established.
5. Code Quality: Treat maintainability, performance, security, accessibility, and testability as equal priorities (All). If a trade-off is required, prefer clarity + security + testability, then performance.

---
## Technology Version Detection

Because this template does not yet define concrete application code, Copilot MUST dynamically detect versions post-seeding:

1. Language Versions
	- For .NET: Inspect `.csproj` `<TargetFramework>` / `<LangVersion>` or global.json. Only use C# features available in that language version.
	- For Node/React/TypeScript: Inspect `package.json` engines, dependency versions, `tsconfig.json` (target/module/lib). Respect configured ECMAScript/TypeScript targets.
	- For Python (if added): Read `pyproject.toml` / `requirements.txt` / runtime spec.
2. Framework Versions
	- ASP.NET Core / Blazor: Detect from NuGet package versions and SDK references.
	- React / React DOM / React Router: Use only documented APIs present in detected versions.
3. Library Versions
	- Always verify existence of an API before suggesting usage (no speculative future features).
	- When absent or ambiguous, generate conservative, widely supported patterns.

Never introduce language or framework features that exceed detected versions. Always annotate version assumptions in comments ONLY when non-obvious (e.g., performance-critical or security-sensitive code paths) using minimal, purpose-driven commentary.

---
## Context & Specialized Instruction Files

Prioritize these (if present) in order:

1. `.github/instructions/security-and-owasp.instructions.md` (security baseline)
2. `.github/instructions/performance-optimization.instructions.md`
3. `.github/instructions/a11y.instructions.md`
4. `.github/instructions/devops-core-principles.instructions.md`
5. `.github/instructions/dotnet-architecture-good-practices.instructions.md` (when .NET code exists)
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
- Follow existing framework: e.g., xUnit/NUnit/MSTest for .NET; Jest/Vitest for JS/TS; React Testing Library for components.
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

- Because this repository is a starter template, Copilot must refrain from inventing architectural layers or technology choices not present in the target initialized project.
- After seeding a new project: perform an initial scan (files, package manifests, project files) before generating new code.
- If no pattern exists yet (e.g., first service class), generate a minimal, conventional implementation and let subsequent files reinforce it—do not prematurely generalize.
- When specialized instructions conflict with generic performance or style advice, specialized instructions win.

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

Copilot MUST:
- Scan for versions & patterns before code generation.
- Align with specialized instruction files for domain concerns.
- Favor clarity, security, testability, and accessibility while preserving performance.
- Avoid speculative architecture, dependencies, or APIs.

Copilot SHOULD:
- Propose incremental refactors only after patterns stabilize.
- Surface security/performance/accessibility considerations succinctly when deviations are necessary.

Copilot MAY:
- Insert minimal doc comments for public APIs in emerging codebases.
- Suggest test scaffolds to bootstrap coverage early.

---
## Final Note

This file sets general guardrails. For any domain-specific work (security, accessibility, performance, architecture patterns), always consult the corresponding `.github/instructions/*.instructions.md` file first. Consistency and explicit pattern detection are the foundation—no assumptions beyond observable artifacts.

