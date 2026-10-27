---
post_title: Local OIDC and image builds
author1: Engineering team
post_slug: local-oidc-and-image-builds
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [local-development, documentation]
ai_note: AI assisted
summary: Build and customize the local Keycloak base.
post_date: 2026-10-27
---

## Build the reusable base

Run from the repository root with a reachable Docker or Podman engine:

```powershell
./Build-ToolboxImage.ps1 -Profile oidc -ContainerRuntime docker -PassThru
```

The image is `localhost/toolbox-oidc-keycloak:latest`, based on
`quay.io/keycloak/keycloak:latest`. The build reuses cached images and does not
start services or publish to a registry. The source revision is recorded as an
OCI label. Uncommitted changes require separate working-tree evidence.

## Application ownership

Copy [the derived Dockerfile](../../containers/oidc/examples/project.Dockerfile)
into your application and build it with the same engine and image store.
Use [the profile descriptor](../../containers/oidc/examples/profile.json) as an
acceptance target. Each project owns its realm import, exact callback URLs,
clients, synthetic identities, audiences, scopes and application authorization.
The default realm supplies only generic admin and user personas.

## Runtime contract

Follow [the complete startup contract](../../containers/oidc/keycloak/README.md).
The startup script supports default and project profiles, validates required
inputs, rejects unsafe imports and writes private runtime files. Keep credentials
out of Dockerfile arguments, image layers and committed configuration. A local
identity container is separate from the three shared Compose groups.

## Verification

```powershell
./tests/test_build_toolbox_image.ps1
./tests/test_oidc_contract.ps1
./tests/test_oidc_startup.ps1 -ContainerRuntime docker -OutputPath ./artifacts/oidc/startup
```

The live suite allows 300 seconds for each provider's cold initialization. Override
`-ReadinessTimeoutSeconds` for the selected host (20–600 seconds). It reuses the
selected local `latest` images and records their exact image IDs in the receipt.

The first two checks use stubs/source contracts. The startup suite uses disposable
containers and a dedicated network. Neither substitutes for application browser
or authorization acceptance. Protocol targets and evidence schemas live under
[containers/oidc/contracts](../../containers/oidc/contracts/consumer-matrix.md).
