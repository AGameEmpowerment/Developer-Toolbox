---
post_title: Sequential container comparison
author1: Engineering team
post_slug: sequential-container-comparison
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [testing, containers, local-development]
ai_note: AI assisted
summary: Sequential build, deployment, live resource validation and teardown for both projects.
post_date: 2026-10-27
---

## Results

Both projects built and deployed all 15 shared container definitions. Each
completed two initialization jobs and started 13 long-running services.
Both produced the same three failed checks against the retained data:
Service Bus queue, topic and health. Service Bus restarted repeatedly.
The other 12 long-running services remained running without restarts.

| Executed checks per project | Reference passed / failed / skipped | Personal passed / failed / skipped |
| --- | --- | --- |
| Shared resources: standalone .NET CLI | 16 / 2 / 0 | 16 / 2 / 0 |
| Shared resources: Python HTTP probes | 3 / 1 / 0 | 3 / 1 / 0 |
| Service Bus: .NET CLI through disposable Python fixture | 2 / 0 / 0 | 2 / 0 / 0 |
| OIDC: standalone PowerShell | 25 / 0 / 0 | 25 / 0 / 0 |

The disposable Service Bus checks used fresh SQL volumes and passed queue and
topic operations in both projects. The shared-stack failure is associated with
retained database state: SQL error 5170 reports that
`/var/opt/mssql/data/SbGatewayDatabase.mdf` already exists when the emulator
tries to create it. Existing data was retained; no database repair or reset was
attempted. The fresh-volume result does not make the default deployment pass.

Both OIDC runs used the existing `quay.io/keycloak/keycloak:latest` image with
ID `sha256:a78d6b79572168cb25886b80a8b5b8d4ed4390743e3eecb810538683e209dd6b`.
The reference passed with its native 180-second readiness budget; the personal
project passed with its native 300-second budget. The earlier timeout was not
reproduced in this round. No pinned-image workaround was used.

All test containers and networks were removed. Disposable test volumes were
removed, all 13 original named volumes were retained, and unrelated containers
were left running. Built images remain available for local reuse.

## Source and execution evidence

Executed on **2026-10-27** with clean tracked source trees:

| Project | Exact tested source commit |
| --- | --- |
| Reference | `618951fbd522f1a52ea68af130512cc5931a536a` |
| Personal | `c7b0c8c0161edcc79cb9e29d0652dbcee6f1dabc` |

The [machine-readable evidence](container-comparison.json) includes SHA-256
hashes for 70 source inputs per project, built validator assembly hashes,
container image IDs, states, health, job exit codes, restart counts, per-check
results, OIDC receipts and cleanup checks. Hashes describe checkout bytes at
those commits; ignored credentials, certificates and generated settings are
excluded. Absolute checkout and output paths use role placeholders.

Host tools were Docker Engine **29.9.2** on Linux/AMD64, PowerShell **7.6.5**,
.NET SDK **10.0.402**, and Python **3.15.1**. BuildKit reused cached layers and
image preparation pulled only missing images.

## Sequential procedure

The reference completed its final OIDC cleanup at **23:13:04 UTC**. The personal
setup began at **23:13:13 UTC**. No container collection from one project ran
alongside the other project's collection.

Each project used this order:

1. Build and deploy using `docker_setup.ps1 -ContainerRuntime docker`. This
   invokes `docker_pull.ps1` and `docker compose up --build -d` for common,
   Azure and AWS resources. All three custom shared images built successfully.
2. Build that project's .NET App in Release with `dotnet build --no-restore`.
3. Wait for container readiness, record state and initialization-job exits,
   then execute all 18 resource commands individually with
   `validate <resource> --json --timeout-seconds 45` and the project's dotenv.
4. Probe RedisInsight `/api/health`, smtp4dev web, WireMock `__admin/mappings`
   and Service Bus `/health` over their published loopback HTTP ports.
5. Run `docker_down.ps1 -ContainerRuntime docker` and verify that shared
   containers and networks are gone. No cleanup-volume flags were used.
6. Run `tests/test_servicebus_containers.py --runtime docker` with the project's
   freshly built App assembly. Its unique SQL/broker fixture is removed with
   its own disposable volume before continuing.
7. Build OIDC using `Build-ToolboxImage.ps1 -Profile oidc -ContainerRuntime docker`,
   then deploy, test and clean up using
   `tests/test_oidc_startup.ps1 -ContainerRuntime docker`, with each script's
   native timeout default.

The pre-existing reference stack was first removed using its PowerShell
teardown script. Both default deployments reused the original named volumes.
Because local PostgreSQL and RabbitMQ credentials differed between projects,
the personal setup and validator received the retained volumes' existing
credentials through process environment variables. Neither `.env` file was
replaced. This comparison therefore measures both implementations against the
same retained state; it does not qualify the personal dotenv against fresh
PostgreSQL or RabbitMQ volumes.

Personal setup also emitted a nonfatal Windows certificate-store cleanup
warning because UI was unavailable. Setup returned zero; WireMock HTTP passed.
WireMock HTTPS trust was not qualified in this round.

## Counting method and scope

The 18 SDK cases are named resource commands from `SampleApplication.ResourceNames`,
executed once per shared deployment. Four explicit HTTP probes are separate
checks. The two disposable Service Bus cases repeat queue and topic checks
against fresh state and remain separate from the shared-stack results.
These are standalone checks, not NUnit test-method totals.

Each OIDC script has **12 authored `Invoke-Check` call sites**, expanding to
**25 executed named cases**. Expansion includes 10 invalid-input cases,
four invalid-import cases and two HTTP persona login cases. Both receipts
record 25 passing, zero failing, zero skipped and zero unexecuted cases.
Browser executions and snapshots are zero. Optional external derived-image
qualification was not run; the supplied project-profile fixture was tested.

Unit suites were not rerun for this container round. Earlier framework totals
remain in the [parity verification](verification.md) and its linked inventory.
Code coverage is **unavailable**; no measured coverage report was collected.
