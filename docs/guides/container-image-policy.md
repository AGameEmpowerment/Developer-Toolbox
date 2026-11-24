---
post_title: Container image versions and pulls
author1: Engineering team
post_slug: container-image-policy
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [containers, docker, podman, images, local-development, testing]
ai_note: AI assisted
summary: Which image versions development, test and production containers use, when to pin, and when images are pulled.
post_date: 2026-10-25
---

This policy applies to Toolbox and projects that adopt its local resources.

## 1. Development and test containers use `latest`

This covers everything that only runs locally or in test environments:

- OneBox, Testcontainers and the Aspire AppHost
- the test runner and Playwright browsers
- service mocks: smtp4dev, SQL Server, WireMock, Seq and Keycloak
- Ryuk and other helper containers

Do not pin these by digest. Do not add tests whose only job is to enforce a
pin.

## 2. Pin only when a tool needs a matching version, and then by tag

Pin by tag, never by digest, and give each pinned tag a one-line comment that
says why. Today that means:

| Image | Pin | Why |
| --- | --- | --- |
| Playwright | The tag that matches the Playwright package the tests use, for example `v1.63.0-noble` | The browsers must match the package version |
| .NET SDK | `10.0` | The projects target .NET 10; `latest` could jump to a newer major version |
| Node | `24` only if the build requires it; otherwise `latest` or LTS | Match a declared engine requirement |

Toolbox's SQL Server base uses `latest`. Setup reads that local base's publisher
version label and selects the exactly matching Full-Text Search package at build
time, without pinning a CU or installing another copy of the engine.

A product-line tag such as the Cosmos DB Emulator's `vnext-latest` is not a
pin. It still tracks the newest release of the line the stack needs.

```dockerfile
# Browsers must match the Playwright package version used by the tests.
FROM mcr.microsoft.com/playwright/dotnet:v1.63.0-noble
```

The devcontainer is an exception: it may keep pinned versions, including the
feature lock in `.devcontainer/devcontainer-lock.json`. Keep that file, and
refresh it with `npx @devcontainers/cli upgrade --workspace-folder .` (check
first with `outdated`).

## 3. Production images stay pinned

Any image that is deployed, or is the base for something that is deployed,
stays pinned. Keep a digest pin where one exists today. This includes the
Server runtime image that the Build-Docker pipeline publishes
(`aspnet:10.0@sha256:…`) and any other base that feeds a shipped image.

## 4. The Keycloak OIDC mock uses `latest`

The Okta stand-in is `quay.io/keycloak/keycloak:latest`. That is, and stays,
the accepted version in Toolbox.

## 5. Reuse local images

- Reuse a local copy of a tag instead of pulling it on every run.
- Pull an image only when it is missing locally. If the primary registry
  fails, fall back to the registry mirror.
- Refresh a local copy yourself with an explicit pull, for example
  `docker pull quay.io/keycloak/keycloak:latest`.
- No script, Compose file or build step forces a pull, and helper images are
  never pulled on their own.

In practice:

| Tool | Do | Avoid |
| --- | --- | --- |
| Docker Compose | Use Toolbox setup's missing-image preparation and `pull_policy: never` | `pull_policy: always`, `build.pull: true` |
| `docker build` / `podman build` | Build without a pull option | `--pull`, `--pull=always` |
| `docker run` / `podman run` | Run without a pull option | `--pull always` |
| Scripts | Check with `image inspect` and pull only when the image is missing | An unconditional `pull` |

## 6. Keep it simple

If none of these rules clearly requires a pin, do not pin.

Compose always refreshes `latest` under its default/missing policy. Toolbox's
setup therefore inspects and prepares missing service images before Compose
runs. Direct Compose users must first run `docker_pull.ps1` or `docker_pull.sh`;
use `docker_setup.ps1` or `docker_setup.sh` for the SQL build's version metadata.
No private mirror is configured by default. Optional approved mirrors use `TOOLBOX_IMAGE_MIRRORS` in both shells, for example
`mcr.microsoft.com=registry.example/mcr,quay.io=registry.example/quay`.
These are registry prefixes, never credentials. A cached fallback is reused and
tagged locally; a missing fallback is pulled only after the primary fails.

## Separate rule: the `mcp/seq-otlp` image

Always keep the latest `mcp/seq-otlp` image on the machine. Rule 5's reuse
does not apply to it.
When this is a locally built MCP image, update/rebuild its owning source checkout
instead of assuming that the local tag is a publicly pullable repository.

## Operational guidance and verification

| Task | Command from the Toolbox root |
| --- | --- |
| Prepare missing images, derive SQL/FTS metadata, build and start services | `./docker_setup.ps1 -ContainerRuntime docker` or `bash docker_setup.sh --runtime docker` |
| Prepare missing shared images without starting services | `./docker_pull.ps1 -ContainerRuntime docker` or `bash docker_pull.sh --runtime docker` |
| Prepare only the images needed by direct host-example validation | `bash tests/prepare_host_images.sh docker` |

Use `podman` in place of `docker` for the selected runtime. Image preparation
does not refresh an existing tag. An explicit user-requested pull refreshes a
registry image; rebuilding its owning source refreshes a locally built image.
After an intentional SQL base refresh, run setup again so the Full-Text Search
package matches the selected local base's publisher version.

The accepted policy is independent of merge status. Its verified implementation
is `docs/container-image-policy` commit
`629c1065725643264c9aa3cccd18d1f45bd2ec93`, in open
[PR #67](https://github.com/AGameEmpowerment/Developer-Toolbox/pull/67) as reviewed
2026-10-25; `main` remains `78c526a8803fcdd8bb9584ec04ab9d0c8c1f46ff`.
[Dated validation](../reviews/container-image-policy-2026-10-25.md) records the
successful push/PR workflows, test accounting and production-pin boundary.
The [wiki policy](https://github.com/AGameEmpowerment/Developer-Toolbox/wiki/Container-Image-Policy)
publishes the same guidance with source qualification.
