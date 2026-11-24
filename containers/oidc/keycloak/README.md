---
post_title: Local Keycloak startup contract
author1: Michael Carey
post_slug: local-keycloak-startup-contract
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [oidc, keycloak, containers]
ai_note: AI assisted
summary: Build and extend the Toolbox local Keycloak image using the version 1 startup contract.
post_date: 2026-10-20
---

## Local identity base

This image is for local development and automated synthetic runs only. It
adds a startup preflight and a non-secret default realm template to upstream
Keycloak. Native Keycloak handles import, signing and all protocol behavior.
The [source-only contract](contract.json) describes version 1; it is not
copied into the provider image or parsed at runtime.

Build with PowerShell 7.3 or later and a reachable Docker or Podman engine.
No host application SDK is required. Run commands from the repository root.

The `oidc` build profile creates the reusable base image in the selected
local image store. It is the default, so no arguments are needed:

```powershell
./Build-ToolboxImage.ps1
```

The output is `localhost/toolbox-oidc-keycloak:latest`. The profile selects this
directory and Dockerfile, reuses the cached upstream image and records the
Toolbox source revision when Git is available. It works from another working
directory when invoked by the script's full path. Use
`-ContainerRuntime podman` for Podman's store. The command builds the image
and returns; applications own their runtime configuration and startup.
Build profiles are separate from the runtime `default` and `project` modes.

The profile follows the Dockerfile's upstream default,
`quay.io/keycloak/keycloak:latest`. `latest` is the accepted Keycloak version
for applications that use this Okta OIDC mock; do not pin the default by tag or
digest. The consumers' first inspected provider digest is recorded in the
[consumer baseline](../contracts/consumer-matrix.md) as history, not as a
build input. Record the actually loaded image ID and platform in the selected
engine/store with each receipt. A registry manifest digest alone does not
identify the local image configuration.

## Runtime inputs

Use fresh writable data for each automated run. Supply a canonical
`KC_HOSTNAME` HTTP(S) origin reachable by that run's browser and backchannel.
The caller validates its full URI and routing. The wrapper rejects common
unsafe shapes; discovery must still match the exact expected issuer.

| Input | Default mode | Project mode |
| --- | --- | --- |
| `TOOLBOX_OIDC_PROFILE` | `default` when omitted | Explicit `project` |
| `TOOLBOX_OIDC_REALM` | Fixed `toolbox-local`; conflicting value rejected | Required, `[A-Za-z0-9_-]`, 1–128 characters |
| `KC_HOSTNAME` | Required canonical origin | Required canonical origin |
| `TOOLBOX_OIDC_ADMIN_PASSWORD`, `TOOLBOX_OIDC_USER_PASSWORD`, `TOOLBOX_OIDC_CLIENT_SECRET` | All required; independently generated base64url values, 24–128 characters | Unused |
| `TOOLBOX_OIDC_CALLBACK_URI` | One exact HTTP(S) callback; no wildcard, userinfo or fragment | Owned by final realm import |
| Native import storage | Empty; wrapper copies the default template with private permissions | Exactly one readable regular `<realm>-realm.json`, without symlink ancestors |

The default realm contains only synthetic `admin` and `user` profiles with
matching realm roles, plus confidential code/S256 client `toolbox-local-web`.
Neither profile has Keycloak management or application business grants.
Management bootstrap, if needed, uses separate native Keycloak inputs.
Project mode never copies these users and requires no default secrets.
The default grant list describes required code-flow checks. Native Keycloak
can also issue refresh tokens; refresh rotation and replay qualification is
a separate project-profile protocol target.

Only `start` or `start-dev` with `--import-realm` is supported server startup.
The default command is `start-dev --import-realm`; version 1 does not promise
optimized startup. `--version` is passed through without realm inputs.
Startup delegates with `exec` to native `kc.sh` for signal delivery.
CLI hostname, config-file and HTTP-relative-path overrides are rejected so
they cannot invalidate the checked environment and fixed import paths.

Preflight failures exit 78 with a fixed `TOOLBOX_OIDC_E_*` diagnostic:
`COMMAND`, `HOSTNAME`, `PROFILE`, `REALM`, `SECRET`, `CALLBACK`,
`IMPORT_DIRECTORY`, `IMPORT_COUNT`, `IMPORT` or `WRITE`. Diagnose the input
named by that code without exposing its value. Private import contents and
credentials must never be copied into public diagnostics or receipts.

## Deriving a project image

Another repository can reference the built image in its Dockerfile:

```dockerfile
FROM localhost/toolbox-oidc-keycloak:latest
```

It can also select `image: localhost/toolbox-oidc-keycloak:latest` in its own
Compose configuration. Use the same engine/context/store as the build.
For another machine or store, export/import the image or publish it through
your chosen registry workflow; the build profile produces a local image.

The [neutral example](../examples/project.Dockerfile) adds only a
[non-secret descriptor](../examples/profile.json) under
`/opt/project-identity/` and selects project mode. It inherits startup and
the upstream non-root user. Descriptor metadata and optional templates here
are never implicitly imported. The orchestrator generates the final native
realm externally, checks the descriptor against it, and copies it privately
to `/opt/keycloak/data/import/<realm>-realm.json` before startup.

```powershell
# Verify the base exists in this engine before building; omit -Pull.
docker image inspect localhost/toolbox-oidc-keycloak:latest
if ($LASTEXITCODE -ne 0) { throw 'Build the local OIDC base in this engine/store first.' }
./Build-ToolboxImage.ps1 -ContextPath ./containers/oidc/examples `
    -Dockerfile project.Dockerfile -ImageName localhost/toolbox-oidc-example `
    -ContainerRuntime docker -PassThru
```

Use the same connection/store for both builds. Rebuilding the base does not
update an existing derived image or container. Rebuild the child and recreate
its owned container after every base change. Imports do not merge automatically;
native startup import skips a realm already present in persistent state.

## Verification and evidence

Readiness requires bounded management `/health/ready` on port 9000, exact
realm discovery on port 8080, then usable public signing JWKS. An HTTP-ready
process without its intended realm is a failure. The startup smoke uses
PowerShell HTTP checks on the host. Full browser/protocol drivers are planned
as external containers; the provider has no added packages or test tools.
Record effective user, runtime bytes and layers when qualifying it.

```powershell
./tests/test_oidc_contract.ps1
./tests/test_oidc_startup.ps1 -ContainerRuntime docker `
    -DerivedImageName localhost/toolbox-oidc-example:latest
```

Build both images above first. Omitting `-DerivedImageName` checks project
mode on the base alone and explicitly leaves derived qualification unrun.

See the [implementation plan](../../../docs/local-oidc-plan.md),
[consumer baseline](../contracts/consumer-matrix.md),
[protocol acceptance descriptors](../contracts/protocol-profiles.json) and
[receipt field contract](../contracts/evidence-format.json). A descriptor
defines intended checks; it is not proof that they ran. Live consumer, full
protocol, Podman and architecture support require separate measured receipts.

## License and provenance

The publisher's documented image is `quay.io/keycloak/keycloak`. Keycloak is
licensed under [Apache License 2.0](https://github.com/keycloak/keycloak/blob/main/LICENSE.txt),
which permits this local use without a paid license or cloud account. Retain
the upstream distribution's license and third-party notices; the base does not
remove them or add runtime dependencies. Record upstream reference, loaded
image ID/platform, source revision and dirty input hashes per build.

Upstream references verified on 2026-10-20:
[containers and native entrypoint](https://www.keycloak.org/server/containers),
[native import and placeholders](https://www.keycloak.org/server/importExport),
[external health probes](https://www.keycloak.org/observability/health) and
[canonical hostname configuration](https://www.keycloak.org/server/hostname).
