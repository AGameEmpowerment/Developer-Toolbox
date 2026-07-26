# System Uptime Tracker API Starter

This project is the ASP.NET Core API host for the repository. It currently
contains reusable application scaffolding rather than uptime-tracking domain
features.

The retained baseline includes:

- dependency injection and strongly typed configuration
- ASP.NET Core Identity with local account bootstrap support
- optional Microsoft identity platform authentication
- policy-based authorization and antiforgery protection
- SQL Server and Entity Framework Core integration
- health checks, structured request tracing, and problem details
- OpenAPI/Scalar development tooling
- optional Redis-backed distributed caching with in-memory fallback
- OpenTelemetry logging, metrics, and tracing

## Configuration

Configuration is loaded from `appsettings.json`, environment-specific settings,
environment variables, and developer secrets. Environment variable keys use
double underscores, for example `ConnectionStrings__DefaultConnection`.

Never commit connection strings, client secrets, redaction keys, or telemetry
credentials. The checked-in values are non-secret placeholders for local
development.

Important sections are:

- `ConnectionStrings` for SQL Server and optional Application Insights
- `DataProtection` for persisted ASP.NET Core key storage
- `Auth` for local Identity and optional local JWT validation
- `FeatureManagement` for opt-in development surfaces
- `Cors` and `ForwardedHeaders` for proxy-aware hosting
- `OpenTelemetry` for observability exports
- `Redis` and `Cache` for optional distributed caching

## Local identity bootstrap

When the identity store has no active administrator, the controlled
`/api/identity/self-create` flow creates the initial administrator. Later
self-created users remain roleless until an administrator assigns roles.

Cookie-authenticated write requests require an antiforgery token from
`GET /api/auth/antiforgery-token`. Bearer-token API calls do not send the
antiforgery header.

## Operational endpoints

- `GET /_health` reports application health.
- `GET /api/operations/metadata` exposes safe version and startup metadata.
- OpenAPI and Scalar are available only when their feature flag is enabled.

See the repository-level `CONTRIBUTING.md` for local setup. Delivery files under
`devops/` are examples and must be reviewed and configured before use outside a
local development environment.
