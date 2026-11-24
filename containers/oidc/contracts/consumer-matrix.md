---
post_title: Local OIDC consumer contracts
author1: Engineering team
post_slug: local-oidc-consumer-contracts
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [oidc, testing]
ai_note: AI assisted
summary: Generic browser and service profiles for project-owned local identity.
post_date: 2026-10-27
---

## Consumer profiles

The shared base supplies local identity infrastructure. Each application owns its
realm, exact callbacks, synthetic users, scopes, claims and authorization rules.
These profiles describe acceptance targets; they are not execution evidence.

| Profile | Grant and authentication | Ownership |
| --- | --- | --- |
| Default browser | Confidential authorization code with S256 PKCE | Two generic personas, admin and user; no application permissions |
| Project browser | Authorization code and rotating refresh tokens; client_secret_post | Project-generated native realm and exact runtime callback |
| Project service | Client credentials; client_secret_basic | Project-owned audience and scopes; no browser login or refresh token |

## Qualification

Use [protocol-profiles.json](protocol-profiles.json) for the required checks and
[evidence-format.json](evidence-format.json) for external-driver receipts.
Source contract validation does not establish browser login, token validation,
refresh replay protection or application authorization. Projects must execute
and record their own protocol and product acceptance checks.

## Build and startup

Build the base from the repository root with `./Build-ToolboxImage.ps1`.
Use [the startup contract](../keycloak/README.md) and
[the derived-image example](../examples/project.Dockerfile). Keep the base and
project image in the same Docker or Podman image store. Runtime secrets belong
in temporary private files or environment values, never in the image.
