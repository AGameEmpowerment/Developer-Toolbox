# System Uptime Tracker Docker Deployment Template

> This folder is a local-development example and deployment template. Review all hosts, credentials, certificates, volumes, and retention settings before adapting it to a real environment.

This folder is the complete production Docker release surface:

- `../pipelines/Build-SystemUptimeTracker.yml` builds and publishes the Docker
  artifact together with the IIS artifacts.
- `Deploy-Production-Docker.yml` deploys the artifact on a configured
  self-hosted agent and publishes it through IIS at the example URL
  `https://docker-app.example.com`.
- `Publish-SystemUptimeTrackerDockerArtifacts.ps1` assembles the release artifact.
- `Build-SystemUptimeTrackerDockerImages.ps1` builds local production images.
- `Deploy-SystemUptimeTrackerDocker.ps1` deploys the production-style stack.
- `Deploy-SystemUptimeTrackerDockerPackage.ps1` deploys an extracted build artifact.
- `docker-compose.yml` defines the frontend, backend, and standalone SQL Server.
- `docker-compose.backend-debug.yml` is an explicit local debug override.
- `SystemUptimeTracker.Environment.ps1` initializes and validates release settings.
- `New-SystemUptimeTrackerDockerEnvironment.ps1` materializes Azure DevOps variables
  into the server-local environment file.
- `Install-SystemUptimeTrackerDockerCompose.ps1` idempotently installs the official
  Windows Server standalone Compose executable when the Docker CLI plugin is
  unavailable.
- `Configure-SystemUptimeTrackerDockerIisProxy.ps1` binds the public Docker hostname
  to the loopback-only frontend port.
- `Backup-SystemUptimeTrackerDatabase.ps1` writes a timestamped, checksummed backup
  into the persistent SQL volume before migrations run.
- `systemuptimetracker.production.env.example` is the checked-in environment contract.

## Persistent SQL data

The production SQL service is independent from the shared development
containers. It stores `/var/opt/mssql` in the explicitly named
`systemuptimetracker-production-sql-data` volume by default. Rebuilding or replacing
the SQL container reattaches that volume and preserves the database.
Pre-migration backups are stored separately in
`systemuptimetracker-production-sql-backups`.

Normal deployment:

```powershell
pwsh ./devops/docker/Deploy-SystemUptimeTrackerDocker.ps1
```

The deployment builds new application images, starts SQL, waits for its health
check, and starts the backend with EF Core startup migrations enabled. EF
applies only pending migrations and records them in `__EFMigrationsHistory`.
Pass `-SkipStartupMigrations` only when the packaged idempotent SQL scripts
were applied separately.

Do not use `docker compose down --volumes` during an update. Removing
`systemuptimetracker-production-sql-data` is a destructive data-deletion operation
and should happen only after a verified backup.

## Deploying the published artifact

Extract `docker-package.zip`, configure
`systemuptimetracker.production.env`, and run:

```powershell
pwsh ./Deploy-SystemUptimeTrackerDockerPackage.ps1
```

The package-native script builds the staged backend and frontend contexts,
starts the standalone SQL service with its persistent volume, and enables
pending EF Core migrations by default.

The Azure DevOps deployment pipeline uses `C:\Apps\SystemUptimeTracker\docker` as
its release root. Application artifacts can be replaced on every deployment;
database files remain in the Docker-managed
`systemuptimetracker-production-sql-data` volume.

## Local development SQL

Local development uses the standalone `systemuptimetracker-sql` container by
default. Its Compose project is `systemuptimetracker`, its host endpoint is
`127.0.0.1:11433`, and its persistent data volume is
`systemuptimetracker-sql-data`. These local names are intentionally independent of
the production release names above.

Start the local SQL service:

```powershell
pwsh ./devops/docker/Start-SystemUptimeTrackerLocalSql.ps1
```

Select the standalone database for AppHost and direct API launches:

```powershell
pwsh ./devops/docker/Set-SystemUptimeTrackerLocalDatabase.ps1
```

The shared development SQL service remains available as an explicit
alternative:

```powershell
pwsh ./devops/docker/Set-SystemUptimeTrackerLocalDatabase.ps1 -Target Shared
```

Both commands read credentials from ignored local environment files and write
the resulting connection string to .NET user secrets. No database credential
is committed to source control.

## Full local Docker development test

To build and run the same SQL, backend, and frontend container topology used
by the deployment template, run:

```powershell
pwsh ./devops/docker/Start-SystemUptimeTrackerLocalDocker.ps1
```

This uses the production Dockerfiles with a local development configuration,
the `systemuptimetracker` Compose project, and the persistent local SQL volumes. It
creates an ignored local application-login value when needed, provisions that
login in SQL, enables startup migrations, and verifies both application health
endpoints:

- UI: `http://localhost:8001`
- API: `http://localhost:8002`
- SQL: `127.0.0.1,11433`

Use `-SkipBuild` to restart already-built `local` images, or `-NoCache` for a
completely clean image rebuild. AppHost remains available as the faster
source-level development workflow; this command is the deployment-shaped
Docker integration test.

Use `-NoCache` when the integration test needs a completely fresh image build.
Database reset is intentionally handled by the separate
`Reset-SystemUptimeTrackerDockerDatabase.ps1` script so that normal build and
startup commands do not delete persistent data.
