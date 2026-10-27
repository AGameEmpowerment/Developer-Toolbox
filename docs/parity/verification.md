---
post_title: Resource parity verification
author1: Engineering team
post_slug: resource-parity-verification
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [testing, local-development]
ai_note: AI assisted
summary: Current resource comparisons, image builds and independent test execution.
post_date: 2026-10-27
---

## Source state

Reviewed 2026-10-27. Code and test baseline: `46c7d9d500d15d9481762c181ea4336db0c6df24` on
`codex/toolbox-resource-parity`. Reference source: `618951fbd522f1a52ea68af130512cc5931a536a`.

The [source inventory](test-inventory.json) counts exact Git blobs at that commit.
Executions occurred before the commit; their receipts retain base commit
`66f452566f589a05c520d2679b320d020284d5f5`, dirty-tree state and checkout hashes.
The [original execution-input inventory](https://github.com/AGameEmpowerment/Developer-Toolbox/blob/46c7d9d500d15d9481762c181ea4336db0c6df24/docs/parity/test-inventory.json)
preserves those hashes. Every recorded input was verified to match this committed
baseline after checkout line-ending normalization. Branch changes remain unmerged.

## Resource parity and builds

Complete `docker compose config --format json` documents match for common, Azure
and AWS after normalizing absolute checkout paths only. The comparison covers all
13 long-running services and two one-shot jobs, including images, build contexts,
ports, environment, health checks, volumes and restart behavior. See
[the comparison receipt](resource-parity.json).

| Local image | Build | Image ID |
| --- | --- | --- |
| `dev_azure_shared-mssql:latest` | Passed | `sha256:500942ad03e704e3240e5e125d28c4e7a6db0c42dc39eaeba78596fc8470e0e5` |
| `dev_aws_shared-aws-emulator:latest` | Passed | `sha256:88497a3834f3c558dac110bc60eed421decbac0ae7a6060548054fbbf4967e7a` |
| `localhost/toolbox-oidc-keycloak:latest` | Passed | `sha256:c8987ad08291db9716d4f1acce0b68e71be63ac1229d9b88496ee7e71a98be8a` |
| `dev_aws_shared-aws-init:latest` | Passed | `sha256:5688ebec12b1a3903f11035c6f5203ba6b792e74229f293927e695d5eca14c5a` |

The complete shared stack was not started during this review. Images were built with Docker; Podman behavior was checked with source/stub suites, not a live Podman build. Existing shared data and settings were preserved.

## Executed results by framework and area

| Framework | Area | Passed | Failed | Skipped / not run |
| --- | --- | --- | --- | --- |
| NUnit | DeveloperToolbox.Example.Lib.Tests | 1 | 0 | 0 skipped |
| NUnit | DeveloperToolbox.Example.Functions.Tests | 178 | 0 | 0 skipped |
| NUnit | DeveloperToolbox.Example.Data.Tests | 44 | 0 | 2 skipped |
| NUnit | DeveloperToolbox.Example.Lambda.Tests | 45 | 0 | 0 skipped |
| NUnit | DeveloperToolbox.Example.Resources.Tests | 53 | 0 | 0 skipped |
| NUnit | DeveloperToolbox.Example.App.Tests | 60 | 0 | 0 skipped |
| NUnit | DeveloperToolbox.Example.Messaging.Tests | 103 | 0 | 0 skipped |
| Python unittest | tests | 109 | 0 | 8 skipped |
| Python unittest | Python SDK/Functions/Lambda | 31 | 0 | 0 skipped |
| Vitest | Node SDK/Functions/Lambda/CLI | 86 | 0 | 0 skipped |
| Vitest | Next.js client | 15 | 0 | 0 skipped |
| Node test | Client dependency-security contracts | 18 | 0 | 0 skipped |
| JUnit | Java/core | 68 | 0 | 0 skipped |
| JUnit | Java/functions | 31 | 0 | 0 skipped |
| JUnit | Java/lambda | 18 | 0 | 0 skipped |
| Standalone PowerShell | Nine stub/source suites | 9 scripts | 0 scripts | Case totals not collected |
| Standalone Bash | Six stub/source suites | 6 scripts | 0 scripts | Case totals not collected |
| Standalone PowerShell | OIDC live startup | 25 | 0 | 0 skipped; 0 not run |

NUnit discovered 486 adapter-expanded cases: 484 executed and passed; two opt-in
database cases were skipped (individual TRX outcomes: `NotExecuted`). There are
177 authored methods. JavaScript parameter expansion
and repeated helper registration explain why executed cases differ from source
call-site totals. Standalone shell scripts are not added to framework test totals.
JUnit has 49 authored methods; Surefire reports 117 cases after parameter
expansion. These remain separate counts.
No browser/role matrix was executed; snapshots are not counted as tests.

The Bash bootstrap suite passed an isolated rerun after an earlier attempt collided
with generated-adapter replacement. All six final Bash suites passed.

## OIDC startup qualification

The live suite passes all 25 expanded cases from 12 authored check sites using
`quay.io/keycloak/keycloak:latest`. The default readiness budget is now 300 seconds
per provider, configurable from 20 to 600 seconds. The receipt records the exact
resolved upstream/base/derived image IDs, source hashes and owned cleanup passing.
No image pin was added. See [the startup receipt](oidc-startup.json).

The earlier 180-second attempt passed three checks, failed upstream readiness and
left 21 cases not run. It remains historical evidence in `priorExecutions` in the
verification JSON; the passing run above supersedes it. These checks establish
startup and HTTP login smoke behavior, not full browser or application acceptance.

## Host Java and Maven

The Windows host uses Microsoft JDK **25.0.5.0.2** and Maven **3.10.0**. Both official
ZIP archives were checksum-verified and installed under the current user's
programs directory, with user `JAVA_HOME` and `PATH` updated. Start a new terminal
to receive those variables. The JDK remains on the Java 25 LTS line required by the
examples. [Toolchain evidence](toolchain.json) records versions and archive hashes.

The WinGet replacement MSI failed after removing the previous JDK. Installing the
verified Microsoft ZIP restored Java. Maven verify now passes all three modules:
68 core, 31 Functions and 18 Lambda JUnit cases, with all packaging successful.
Surefire XML hashes are recorded separately from the authored source inventory.

## Other checks and dependency findings

Frontend formatting/lint, the Next.js build, adapter synchronization, inventory
reproduction and Git whitespace checks passed. The client dependency audit reports
one moderate and four high findings; the copied braces mitigation remains in place.
The [public NuGet vulnerability audit](nuget-audit.json) reports zero vulnerable
package entries across all 14 solution projects. MailKit 4.18.2 selects patched
MimeKit/PKCS packages. The Data project explicitly selects PKCS 10.0.14 because
SqlClient still requests vulnerable 9.0.11. These changes remediate the recorded
PKCS advisories without suppressing audit warnings. Other package versions follow
the reference; the frontend audit findings above remain separate.

Code coverage is **unavailable**: no measured coverage report was generated or
imported. Test counts do not measure assertion quality. Source inventories,
adapter discovery and actual executions remain separate.

## Evidence and reproduction

[verification.json](verification.json) contains per-area runner counters, image IDs
and hashes of local output evidence. Local NUnit TRX reports are under ignored
`artifacts/parity/dotnet-remediation/`; private machine paths are not copied here. The source
inventory includes per-file authored methods/markers and Git-blob SHA-256 inputs;
execution receipts preserve their original checkout hashes separately.

```powershell
dotnet build DeveloperToolbox.Example.slnx --configuration Release
dotnet test DeveloperToolbox.Example.slnx --no-build --configuration Release --logger trx --results-directory artifacts/parity/dotnet
mvn --batch-mode --no-transfer-progress -f src/DeveloperToolbox.Java.Example/pom.xml verify
python -m unittest discover -s tests -p 'test_*.py'
./tests/test_oidc_contract.ps1
./tests/test_oidc_startup.ps1 -ContainerRuntime docker -OutputPath ./artifacts/oidc/new-run
./Build-ToolboxImage.ps1 -ContainerRuntime docker -PassThru
```

Run `npm test` in the independent Node package, `npm run verify` and
`npm run build:test` in the client, and the Python package's unittest suite in its
virtual environment. Resource image setup reads the SQL base publisher version to
select matching FTS; do not supply an unrelated version to a direct build.

## Intentional adaptations

Names, image labels, package registries, UI components and identity claims are
organization-neutral. Public host adapters and disabled delivery examples remain.
Organization-specific credentials, private package integrations and historical
consumer execution reports are not imported as current evidence. The Node CLI
tests retain all cases with bounded child processes and a runner limit that allows
cold Windows SDK imports.
