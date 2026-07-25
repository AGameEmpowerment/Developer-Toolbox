---
name: dotnet-10-upgrade
description: Upgrades existing .NET applications to .NET 10 (LTS). Use when modernizing services targeting .NET 6–9, reducing technical debt, or standardizing on the current LTS. Applies to ASP.NET Core, worker services, libraries, and test projects. Common trigger phrases include "upgrade to .NET 10", "retarget to net10.0", "migrate from .NET 8 to .NET 10", "update to latest .NET LTS".
compatibility: Requires .NET 10 SDK installed
metadata:
  author: ".NET Stack team"
  version: 1.0
  updated: 2026-02-24
  source-repo: ICS-Eng/agent-skills

---

# .NET 10 Upgrade Skill

## Goal

Safely upgrade an existing .NET application to **.NET 10 (LTS)** while
minimizing risk, preserving behavior, and reducing long-term technical debt.

Do **not** use this skill when:
- creating a brand-new project (start directly on .NET 10)
- performing framework-agnostic refactoring 
- upgrading only NuGet packages without changing target frameworks
- the project is already on .NET 10
- the project has significant compatibility blockers that require code changes before upgrading (address those blockers first)

This skill follows a **three-phase approach**:
1. Assess
2. Plan
3. Execute

Do **not** skip phases.

---

## Phase 1: Assess

### Identify Current State

1. Identify all project types in the solution:
   - ASP.NET Core
   - Worker services
   - Class libraries
   - Test projects
   - Tooling or build-only projects

2. Record the current target frameworks:

```xml
<TargetFramework>netX.Y</TargetFramework>
```

3. Identify:
   - Runtime dependencies
   - Reflection-heavy code
   - Platform-specific APIs
   - Native or COM interop
   - Third-party libraries with framework constraints

---

## Phase 2: Plan

### Decide Upgrade Strategy

Follow these rules:

- Prefer **LTS → LTS** upgrades
- Use **multi-targeting** temporarily if required
- Upgrade **non-critical services first**
- Keep changes mechanical before behavioral

### Dependency Planning

1. Identify all NuGet packages:

```bash
dotnet list package
```

2. Check for outdated packages and available updates:

```bash
dotnet list package --outdated
```

3. Verify .NET 10 compatibility for each dependency using these approaches (in order of preference):
   - **Try the upgrade first** — update `TargetFramework` to `net10.0` and run `dotnet restore`. Packages that fail to restore are incompatible.
   - **Check NuGet.org** — search for each package at `https://www.nuget.org/packages/{PackageName}` and inspect the **Frameworks** tab on the latest version to confirm `net10.0` or `netstandard2.0`/`netstandard2.1` support.
   - **Check the package source repo** — if compatibility is unclear from NuGet.org, look at the project's GitHub repository for release notes, target framework listings, or open issues about .NET 10 support.
   - **Use `dotnet upgrade-assistant`** — run `upgrade-assistant analyze` on the solution to get a compatibility report that flags problematic dependencies.

4. Flag:
   - Deprecated packages (shown by `dotnet list package --deprecated`)
   - Abandoned dependencies (no updates in 12+ months, no .NET 10 support)
   - Framework-bound libraries (target only `net8.0` or older without `netstandard` support)

If a dependency blocks the upgrade:
- Document the package name, current version, and the specific incompatibility
- Propose a replacement (check NuGet.org for alternatives) or isolation strategy (wrap behind an interface and multi-target that project)

---

## Phase 3: Execute

### Update Target Frameworks

For each project:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
</PropertyGroup>
```

If multi-targeting temporarily:

```xml
<PropertyGroup>
  <TargetFrameworks>net10.0;net8.0</TargetFrameworks>
</PropertyGroup>
```

---

### Update SDKs and Tooling

Ensure:
- Build agents include the .NET 10 SDK
- CI pipelines use compatible images
- Local developer environments are updated

Verify with:

```bash
dotnet --list-sdks
```

---

### Address Breaking Changes

Systematically review:
- ASP.NET Core startup and middleware
- Authentication and authorization APIs
- Serialization behavior
- Diagnostics and logging
- EF Core and database providers
- Test frameworks and runners

Prefer **explicit fixes** over suppressing warnings.

- Review the [.NET 10 Breaking Changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0) documentation.
- Run `dotnet upgrade-assistant` for automated migration help.
- Common issues include:
  - Binary incompatible (may simply require recompilation)
  - Source incompatible (e.g., API obsoletions or removals)
  - Changes in behaviors (e.g., nullable reference types)
  - Package version conflicts (may require dependency updates)
  - Outdated packages (run `dotnet list package --outdated`)

---

### Validate

1. Run full test suite:

```bash
dotnet test
```

2. Build in Release mode:

```bash
dotnet build -c Release
```

3. Validate runtime behavior in a non-production environment:
   - Startup
   - Health checks
   - Auth flows
   - Background processing

---

## Post-Upgrade Cleanup

After a successful upgrade:

- Remove temporary multi-targeting
- Remove conditional compilation
- Update documentation
- Record upgrade notes for future migrations

---

## Safety Rules

- Never delete code without justification so as not to lose functionality
- Never disable tests to "get green", surface the underlying issue instead of the failed test
- Never upgrade dependencies blindly, always verify compatibility and impact
- Prefer explicit errors over silent behavior changes

---

## Expected Outcome

After applying this skill:
- The application targets **.NET 10**
- Tests pass without suppression
- CI pipelines are compatible
- The project is aligned with the current LTS baseline
- Technical debt related to platform versioning is reduced

---

## Notes for ICS Teams

This skill is intentionally conservative.
Stability and supportability are more important than adopting new features.

Modernization should be **repeatable, boring, and predictable**.
