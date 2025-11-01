# Devcontainer & Developer Toolbox Guidance

This file replaces the old project-specific Copilot instructions with focused guidance for maintaining developer devcontainers, Docker-based developer tools, and image maintenance best practices for this repository.

Purpose: provide practical, actionable rules and recommendations contributors should follow when editing, building, publishing, or updating devcontainers and Docker images in this repository.

Keep this doc small and change-focused. When you need broader coding conventions for other parts of the project, prefer the repository's root-level documentation (for example `CONTRIBUTING.md`, `README.md`, and `devops/readme.md`).

## Key responsibilities

- Keep devcontainers minimal and reproducible.
- Build images with security and caching in mind.
- Automate image builds, scans, and publishing in CI.
- Version and tag images deterministically.
- Provide clear, short troubleshooting notes for common developer workflows.

## Files of interest in this repo

- `devcontainers/` (if present): devcontainer setups, Dockerfiles, and VS Code config.
- `containers/` and `containers/__files/`: local example files used by images and compose.
- `docker-compose*.yml`: compose stacks for running dependent services locally.
- `mssql/`, `service-bus/`: examples and initialization scripts used by the toolbox images.
- `devops/`: CI and pipeline conventions for building and publishing images.

If you add a new devcontainer or Dockerfile, add a short note in this document describing intent, how to build, and how to run it locally.

## Best practices for devcontainer and Dockerfile authors

1. Small base images
    - Prefer official, minimal images (Debian slim, distroless, Alpine where compatible). Use pinned tags for reproducible builds (for example, `mcr.microsoft.com/dotnet/sdk:8.0-ubuntu.22.04` rather than `latest`).

2. Layering and cache friendliness
    - Order Dockerfile steps to maximize cache reuse: install OS packages first, then copy package manifests and run package installs, then copy source. This reduces rebuild time for iterative development.

3. Multi-stage builds
    - Use multi-stage builds to keep final images minimal. Build-time dependencies should not be present in runtime images.

4. Secrets and credentials
    - Never store secrets in Dockerfiles, images, or in the repository. Use build-time secrets (for example Docker BuildKit secrets), devcontainer `runArgs`, or environment variable injection at runtime. Document any required credentials and how to supply them locally.

5. Reproducible tooling versions
    - Pin versions for language runtimes, package managers, and CLI tools. Add simple checks (for example `dotnet --info`) in README or a build script to help developers verify their environment.

6. Image scanning and security
    - Integrate a vulnerability scanner into CI (Trivy, Snyk, or GitHub Advanced Security). Scans should run for every image build and fail the pipeline on high/critical findings unless an approved exception exists.

7. Non-root containers
    - Run services as a non-root user when feasible. Document any ports and capabilities required.

8. Build in CI and use reproducible tags
    - CI builds should produce deterministic tags using semantics like `image-name:sha-<short-commit>` for ephemeral test builds and `image-name:semver` for releases.
    - Keep a `latest` tag only if your release process clearly defines what `latest` means.

9. Layered caching for CI
    - Use build cache or registry cache to reduce CI build time. GitHub Actions has cache actions for Docker and Buildx. Document the cache strategy in `devops/`.

10. Small runtime images
    - Strip development tools from final runtime images to reduce attack surface and image size.

11. Health checks
    - Add HEALTHCHECK instructions for long-lived services so orchestrators and CI can verify readiness.

12. Compose for local developer stacks
    - Keep `docker-compose.yml` focused on dev ergonomics. Use overrides (for example `docker-compose.override.yml`) for developer-specific settings. Avoid committing secrets in compose files.

13. Documentation and shortcuts
    - Each devcontainer/Dockerfile should have a short README describing: build command, run command, exposed ports, volumes to mount (if any), and common troubleshooting steps. Keep examples concise.

14. Test the developer experience (DX)
    - Regularly verify that `devcontainer` builds and recommended compose flows work on a fresh machine. Add a simple CI job that attempts to build devcontainers and run smoke checks.

## CI & publishing recommendations

- Build matrix: run image builds for supported base OSes or variants.
- Scanning: fail builds on critical vulnerabilities and report medium findings for triage.
- Signing/Attestation: where possible, sign images in CI (Notary / Sigstore) and publish provenance.
- Promotion flow: use a two-step flow — build+scan in PRs, push ephemeral test tags; for releases, run a release job that publishes semver tags and updates an image manifest if multi-arch.
- Automated cleanup: implement TTL or lifecycle policies in registries to remove ephemeral images older than a set retention (for example 30 days).

## Tagging and versioning strategy

- Ephemeral/test builds: `image-name:pr-<pr-number>`, `image-name:sha-<short-commit>`
- Release builds: `image-name:v<major>.<minor>.<patch>`
- Latest: optionally `image-name:latest` with clearly documented semantic meaning

Keep the tagging strategy simple and well-documented in `devops/readme.md` or `devops/pipelines`.

## Troubleshooting & common commands

- Build (local):
  - docker build -t my-image:local -f containers/Dockerfile .
- Build with BuildKit and secrets:
  - DOCKER_BUILDKIT=1 docker build --secret id=npm,src=$HOME/.npmrc -t my-image:local .
- Run:
  - docker run --rm -it -p 8080:8080 -v ${PWD}:/workspace my-image:local
- Compose up:
  - docker compose -f docker-compose.yml up --build
- Scan with Trivy:
  - trivy image --exit-code 1 --severity CRITICAL,HIGH my-image:local

Document any repo-specific variants of these commands in the devops folder or in the corresponding Dockerfile README.

## Adding a new devcontainer or image

1. Create the Dockerfile and a short README in the same folder.
2. Add a devcontainer.json when relevant and reference the Dockerfile.
3. Add CI steps: build, scan, smoke-test. Prefer reusing shared pipeline templates from `devops/pipelines/`.
4. Update this document with a one-paragraph description of intent and any special run instructions.

## Maintenance checklist for maintainers

- Periodically (quarterly) review base image versions and rebuild to pick up OS/security patches.
- Monitor CVE feeds and proactively update images for critical fixes.
- Keep developer documentation up to date; if a change breaks a common DX flow, add a note and fix the CI.
- Remove unused images and compose files from the repo — keep a small set of supported developer stacks.

## Recommended tools and resources

- Docker Build for multi-arch and cache-friendly builds
- Trivy or Snyk for image scanning
- GitHub Actions Docker cache or registry cache for CI performance
- Docker Compose v2 (CLI integrated) for dev stacks
- VS Code Remote - Containers / Dev Containers extension for VS Code integration
- Sigstore (cosign) for signing images and attesting provenance

## Short examples to copy into new images

- Non-root user creation snippet:

```
RUN useradd -m dev && mkdir -p /workspace && chown dev:dev /workspace
USER dev
WORKDIR /workspace
```

- Multi-stage build pattern:

```
FROM node:20-alpine AS build
WORKDIR /src
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM node:20-alpine
WORKDIR /app
COPY --from=build /src/dist ./dist
CMD ["node", "dist/index.js"]
```

## Closing

Keep this doc focused on the developer toolbox and images. If you need project-specific coding conventions or testing rules, use the main `CONTRIBUTING.md` and the relevant service/module documentation.

If you'd like, I can also add a small CI job example (GitHub Actions or Azure Pipelines) that builds, caches, scans, and publishes a test tag for one of the Dockerfiles in this repo — say which CI system you'd prefer and I'll add it as a follow-up change.