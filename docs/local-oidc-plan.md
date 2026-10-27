---
post_title: Local OIDC implementation status
author1: Engineering team
post_slug: local-oidc-implementation-status
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [local-development, documentation]
ai_note: AI assisted
summary: Current local identity resources and remaining qualification.
post_date: 2026-10-27
---

## Implemented

| Artifact | Status |
| --- | --- |
| Reusable Keycloak Dockerfile and startup script | Present |
| Default and project startup profiles | Present |
| Generic browser and service descriptors | Present |
| Docker/Podman local image builder | Present; Docker build verified |
| Source contract and isolated startup suites | Present; see current verification |

## Consumer acceptance

Application-specific browser, token, refresh, logout and authorization checks
remain the consuming project's responsibility. Do not treat source contract
validation as proof of protocol or product acceptance. See
[the local OIDC guide](guides/local-oidc.md) and
[the current verification report](parity/verification.md).
