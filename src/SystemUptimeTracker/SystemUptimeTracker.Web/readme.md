# System Uptime Tracker Web Starter

`SystemUptimeTracker.Web` is the repository's Next.js App Router starter. It
contains the reusable web shell, authentication plumbing, API proxy, telemetry,
localization, feature flags, error handling, and component-test setup. It does
not yet implement uptime-tracking product features.

## Local development

From this directory:

```powershell
npm ci
npm run dev
```

Use `npm run format`, `npm run lint`, `npm test`, and `npm run build` to validate
changes. Configuration belongs in ignored local environment files or a secret
store; do not commit authentication keys, client secrets, or telemetry
credentials.

The primary runtime values are:

- `APP_BASE_URL`: browser-visible application origin
- `API_BASE_URL`: backing ASP.NET Core API origin
- authentication values documented under `src/app/api/auth/`
- OpenTelemetry and Application Insights values documented under `src/utils/`

## Request and error conventions

The catch-all API route forwards authenticated requests to the API and
preserves backend trace IDs. User-visible failures should remain generic and
include the trace ID for correlation rather than exposing exception details.

## Delivery templates

The scripts under `devops/iis` and `devops/docker` can package this application
with the API. They are local-development examples and must be configured and
reviewed before use in a real environment.
