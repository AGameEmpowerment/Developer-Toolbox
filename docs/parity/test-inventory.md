---
post_title: Test inventory
author1: Engineering team
post_slug: test-inventory
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [architecture, testing]
ai_note: AI assisted
summary: Verifiable source test markers separated from discovery and execution.
post_date: 2026-10-27
---

## Scope and method

Reviewed 2026-10-27, exact source commit `46c7d9d500d15d9481762c181ea4336db0c6df24`. Repository
`AGameEmpowerment/Developer-Toolbox`.

This is a tracked-source declaration inventory, not a runtime test total. NUnit attributes are separated into `[Test]`,
inline `[TestCase]`, source `[TestCaseSource]` and `[Theory]` markers; multiple inline rows can belong to one authored
method. The separate authoredAttributedMethodDeclarations total maps each marker to the next C# method and deduplicates
by source line; per-file method names and lines are retained in JSON. This is a syntax scan, not Roslyn or framework
discovery. xUnit Fact/Theory and JUnit annotations are source markers, with deduplicated authored method declarations
listed separately. Chained and comma-separated C# attributes are included. Python counts AST test functions. JavaScript
counts literal test call sites; parameterized sites are a subset and must not be added to them. Data rows and repeated
helper registration are not expanded. node:test imports identify the separate Node runner. Standalone PowerShell/Bash
contract scripts are a separate area.

This source-only inventory does not execute adapter discovery or application tests and does not import execution
results. Consult separately linked execution evidence for discovered cases and pass/fail/skip totals at the selected
commit. Repeated browsers, personas, roles, fixtures and parameter sources are not expanded; snapshots are not counted
as tests. Code coverage is unavailable without a measured report. Test counts do not establish assertion quality.

| Framework | Test area | Authored definitions and separate source markers |
| --- | --- | --- |
| JUnit | `src/DeveloperToolbox.Java.Example/core` | authoredAttributedMethodDeclarations: **32**; testAnnotations: **32** |
| JUnit | `src/DeveloperToolbox.Java.Example/functions` | authoredAttributedMethodDeclarations: **6**; testAnnotations: **6** |
| JUnit | `src/DeveloperToolbox.Java.Example/lambda` | authoredAttributedMethodDeclarations: **11**; testAnnotations: **11** |
| NUnit | `tests/DeveloperToolbox.Example.App.Tests` | Test: **16**; TestCase: **26**; TestCaseSource: **1**; authoredAttributedMethodDeclarations: **23** |
| NUnit | `tests/DeveloperToolbox.Example.Data.Tests` | Test: **6**; TestCase: **40**; authoredAttributedMethodDeclarations: **18** |
| NUnit | `tests/DeveloperToolbox.Example.Functions.Tests` | Test: **9**; TestCase: **69**; TestCaseSource: **5**; authoredAttributedMethodDeclarations: **32** |
| NUnit | `tests/DeveloperToolbox.Example.Lambda.Tests` | Test: **19**; TestCase: **26**; authoredAttributedMethodDeclarations: **27** |
| NUnit | `tests/DeveloperToolbox.Example.Lib.Tests` | Test: **1**; authoredAttributedMethodDeclarations: **1** |
| NUnit | `tests/DeveloperToolbox.Example.Messaging.Tests` | Test: **22**; TestCase: **81**; authoredAttributedMethodDeclarations: **40** |
| NUnit | `tests/DeveloperToolbox.Example.Resources.Tests` | Test: **31**; TestCase: **12**; TestCaseSource: **1**; authoredAttributedMethodDeclarations: **36** |
| Node test | `src/DeveloperToolbox.Example/DeveloperToolbox.Example.Client` | authoredCallSites: **18**; parameterizedCallSites: **0** |
| Python test functions | `src/DeveloperToolbox.Example/DeveloperToolbox.Example.Python` | authoredFunctions: **31** |
| Python test functions | `tests` | authoredFunctions: **117** |
| Standalone shell scripts | `tests` | scripts: **16** |
| Vitest | `src/DeveloperToolbox.Example/DeveloperToolbox.Example.Client` | authoredCallSites: **13**; parameterizedCallSites: **0** |
| Vitest | `src/DeveloperToolbox.Example/DeveloperToolbox.Example.Node` | authoredCallSites: **36**; parameterizedCallSites: **12** |

### Repository totals by framework

| Framework | Authored definitions and separate source markers |
| --- | --- |
| JUnit | authoredAttributedMethodDeclarations: **49**; testAnnotations: **49** |
| NUnit | Test: **104**; TestCase: **254**; TestCaseSource: **7**; authoredAttributedMethodDeclarations: **177** |
| Node test | authoredCallSites: **18**; parameterizedCallSites: **0** |
| Python test functions | authoredFunctions: **148** |
| Standalone shell scripts | scripts: **16** |
| Vitest | authoredCallSites: **49**; parameterizedCallSites: **12** |

## Reproduction and evidence

The adjacent `test-inventory.json` includes every counted path, marker totals and SHA-256 of its Git blob at the
selected commit. `inventory-source.py` contains the exact counting implementation. Run it from the repository root; it
reads source text and hashes from Git blobs, independently of checkout line endings, and rejects changed source inputs.
The recorded commit pins the reviewed source snapshot, independently of later inventory-tooling changes. Without
`--check`, it regenerates the two inventory documents. `--check` validates both reports without rewriting them.

```powershell
python docs/reviews/inventory-source.py --root . --repo AGameEmpowerment/Developer-Toolbox --output-dir docs/parity --date 2026-10-27 --source-commit 46c7d9d500d15d9481762c181ea4336db0c6df24 --check
```

Full source scan excludes untracked build output, dependencies and Git submodule contents. Upstream vendored
dependencies are not silently added to downstream test totals. Existing historical discovery and execution evidence
remains separate from this current source inventory.
