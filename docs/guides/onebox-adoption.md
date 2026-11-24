---
post_title: OneBox adoption
author1: Engineering team
post_slug: onebox-adoption
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [local-development, documentation]
ai_note: AI assisted
summary: Use Toolbox resources in a project-owned test environment.
post_date: 2026-10-27
---

## Resource ownership

| Mode | Owner | Lifecycle and state |
| --- | --- | --- |
| Shared Toolbox | Developer | Persistent local resources started explicitly; no application SDK required |
| Project-owned OneBox or Testcontainers | Application test harness | Run-specific resources, isolated data, readiness and cleanup |
| Hosted processes with shared resources | Application | Language-specific processes use explicit host endpoints |

Do not run product acceptance against mutable shared development data. Reuse
Toolbox Dockerfiles and protocol contracts while letting the application own its
fixture, seed data and authorization. An image build establishes packaging;
protocol and application behavior require separately executed checks.

## Adoption sequence

1. Select only the services required by the project.
2. Assign run-specific container, network, volume and database names.
3. Resolve endpoints for each caller: host, browser or container.
4. Gate application startup on resource readiness and successful initialization.
5. Run deterministic seed and migration steps owned by the application.
6. Record source revision, image identity and pass/fail/skip evidence.
7. Clean up only resources created by that run, including failure paths.

## Identity and observability

Derive a project-owned image from the [OIDC base](local-oidc.md). Keep application
claims and permissions in the consuming project. An issuer must be reachable and
consistent for every client and validator. Shared Seq and WireMock are useful for
manual development; isolated tests should own mutable mail, mock and log state
when assertions depend on it.

## Failure diagnosis

Check the first failed boundary: engine connectivity, image availability, service
health, initialization, endpoint resolution, application startup, then assertions.
Retain sanitized failure diagnostics before cleanup. Do not include passwords,
tokens, imported identities or private realm documents in public receipts.
