# DevOps Pipelines

> These pipelines are examples for local development and project bootstrapping. Their service connections, variable groups, agents, environments, and definition IDs must be configured before use.

## Build Pipeline

`Build-SystemUptimeTracker.yml` is the canonical Azure DevOps pipeline for the
current SystemUptimeTracker release flow.

It performs these phases:

- restore, build, and test the solution
- build the standalone `SystemUptimeTracker.Web` runtime
- generate idempotent EF Core SQL migration scripts
- package the API, web, and Docker release trees and metadata
- publish the resulting artifact set to Azure DevOps

The workflow publishes three Azure DevOps build artifacts:

- `$(APPLICATION_NAME)-Api` containing `server-package/`, `database/`,
  `deployment-assets/`, and shared release metadata
- `$(APPLICATION_NAME)-Web` containing `web-package/`
- `$(APPLICATION_NAME)-docker` containing `docker-package.zip` and its release
  metadata. The package includes:
  - `backend/` Docker build context
  - `frontend/` Docker build context
  - `docker-compose.production.yml` (packaged from `devops/docker/docker-compose.yml`)
  - `systemuptimetracker.production.env.example`
  - `docker-artifact-manifest.json`

The build intentionally does **not** build Docker images. The Docker deployment
flow downloads the published artifact, runs `docker
build` for the backend and frontend contexts, and then runs `docker compose`
with the packaged production compose file.

The packaged Compose file owns a standalone SQL Server service. It mounts
`/var/opt/mssql` from the explicitly named
`systemuptimetracker-production-sql-data` volume by default and does not use the
development SQL instance or `dev_common_shared_mssql-data`. Replacing the SQL
container therefore retains the production database files.

The backend waits for the production SQL health check and then applies pending
EF Core migrations when `API_APPLY_STARTUP_MIGRATIONS=true` (the production
environment template and `Deploy-SystemUptimeTrackerDocker.ps1` defaults). Pass
`-SkipStartupMigrations` only when those migrations have already been applied
through a separate controlled deployment step. The generated idempotent SQL
scripts remain in `database/` for reviewed/manual deployments.

Never use `docker compose down --volumes` during a normal update. Volume
deletion is a separate destructive operation and must be paired with a verified
backup.

`Build-SystemUptimeTracker.yml` stops at publishing plain artifacts. IIS and
Docker deployments remain separate pipelines that consume the same successful
build.

## Deployment Pipeline Templates

- `../iis/Deploy-Production-Iis.yml` downloads a configured build and deploys
  the web and API lanes to IIS. The example URLs are
  `https://app.example.com` and `https://api.example.com`.
- `../docker/Deploy-Production-Docker.yml` downloads the Docker artifact from
  a configured build definition, builds the packaged images, applies pending
  migrations, and exposes the UI through IIS at `https://docker-app.example.com`.
- `Sync-Production-Configuration.yml` is a manual administrative pipeline for
  synchronizing variable group 14. It creates the standalone SQL credential
  and matching IIS connection string together when they are not already
  configured.

Both deployment jobs use the configured agent pool and optional agent demand.
The example Azure DevOps environments are `SystemUptimeTracker-IIS` and
`SystemUptimeTracker-Docker`.
