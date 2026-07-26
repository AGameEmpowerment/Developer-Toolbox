# Utils

## auth

Has the utilities to start and clear impersonation, as well as a server-side method to require sign on for a page

## fetc-parameters

content-type-json.js gives a part of a fetch object to declare accept and content-type as json, in the hopes that we can reduce the number of places we have to type that boilerplate.

get-fetch-cache-parameters.js gives you A utility that lets you create part of a fetch object that determines how next will cache it. Note that server-api-get uses this utility to manage caching in the nextjs server.

## localization

See the readmen inside that folder for specifics

## storybooko-utilities

You should be able to leave this alone, but it works in coordination with the settings and code configuration for the storybook to get strings loaded for all storybooks in the application.

## encyption.js

Server-side code to encrypt and decrypt a value. We use this with impersonation.

## mock-help-promise

This is a utility that is really nice with storybook when you want to evaluate storybook in different load states. The help promise lets you write mock services (see the mock service in the feature flags hooks for an example). Your mock promise will always succeed, fail, or stay in loading based on what you configure. Then your mock service can do the same.

If you write client components to accept a service as a property, but then default the implementation to the real service (typically in a hook you write for said component), you can then inject a service in your storybook to test each behavior you can get from data loads and submits etc. It's a really really useful tool that lets you develop large chunks of functionality entirely in storybook, then you write the real service for true data and you are done.

## server-api-get.js

The util file exists as this is where people probably will think to look. The implementation is in the route.js in the api/[..routeparts] folder beause it is extremely similar to the action in that file, and they should be changed in sync with eachother.

This helper should preserve the same diagnostics contract as the passthrough route:

- return or throw errors that carry the active `traceId`
- avoid leaking raw backend exception details into user-facing messages
- keep any detailed logging on the server side only

## error-reference.js

This file centralizes the trace ID and generic-error helpers used across the frontend.

- `createTraceId()` creates a local fallback trace identifier when a request fails before the backend returns one.
- `extractTraceId()` reads the trace ID from headers, payloads, nested errors, and messages.
- `appendTraceId()` and `getPublicErrorDetail()` make sure user-facing text stays generic but still includes the trace ID needed for support.
- `createTraceableError()` produces an `Error` instance that keeps the trace ID attached for logging and error boundaries.

## server-feature-flag-service.js

Gives a method you can call server side to know if a flag is true
const isBobFlagOn = await isFeatureFlagOn("bob")

## set-cookie-and-reload.js

Used when changing language, but you could have other uses for it. It sets a cookie value and reloads the page.

## set-utils

Used in the use-dictionary hook, but they are potentially useful functions for other uses, hence the inclusion in utils

## testHelper.js

Came with the original scaffold, and is still useful for frontend unit test work.

## Logging and Telemetry Integration

This document describes how to use the logging and telemetry utilities in the SystemUptimeTracker web application.

### Overview

The application can send telemetry through multiple optional server-side sinks while keeping secrets out of the browser. The integration uses a **secure architecture** where all telemetry is routed through the server. `APP_INSIGHTS_ENABLED` controls Azure Application Insights, `APP_OPEN_TELEMETRY_ENABLED` is the master switch for OpenTelemetry sinks, and the server-side frontend logger can export logs to Seq and Aspire in parallel when those sink flags are enabled:

- **Server-side logging** (`logger-server.js`): ASP.NET Core-style logging levels with optional Azure Application Insights plus optional OpenTelemetry export to Seq and Aspire
- **Client-side logging** (`logger-client.js`): Secure client logging that routes all telemetry through `logger-server-actions.js`
- **Custom event tracking**: Track specific user interactions and business events
- **User context management**: Associate telemetry with authenticated users

### Security Architecture

#### Key Security Features

1. **Secrets Stay Server-Side**: The Application Insights connection string and Seq OTLP API key are NEVER exposed to the browser
2. **Server Action Routing**: All client-side telemetry is routed through Next.js server actions in `logger-server-actions.js`
3. **No Direct Client-to-AppInsights Communication**: The client logger calls server actions, which then send telemetry to Azure
4. **Trace ID Correlation**: Client and server loggers should preserve the same `traceId` when one is attached to an error so backend telemetry can be searched from a user-reported ID

#### Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                         BROWSER (Client)                        │
├─────────────────────────────────────────────────────────────────┤
│  LoggerClientWrapper.jsx                                         │
│       │                                                          │
│       ▼                                                          │
│  logger-client.js                                                │
│       │ (Server Action Calls)                                    │
└───────┼─────────────────────────────────────────────────────────┘
        │ HTTPS
        ▼
┌─────────────────────────────────────────────────────────────────┐
│                         SERVER (Node.js)                         │
├─────────────────────────────────────────────────────────────────┤
│  logger-server-actions.js                                        │
│       │ (delegates to logger-server.js)                          │
│       ▼                                                          │
│  logger-server.js                                                │
│       │ (applicationinsights SDK)                                │
│       │                                                          │
│       ▼                                                          │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ Azure Application Insights                                   │ │
│  │ (Connection String: APP_INSIGHTS_KEY - NEVER EXPOSED)   │ │
│  └─────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

### Configuration

#### Environment Variables

Configure the following environment variables in your `.env` file:

```dotenv
# Logging level: Trace, Debug, Information, Warning, Error, Critical, None
APP_LOGGING_LEVEL=Information

# Azure Application Insights toggle and connection string
APP_INSIGHTS_ENABLED=false
# Format: InstrumentationKey=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx;...
APP_INSIGHTS_KEY=your-connection-string-here

# Master switch for OpenTelemetry sinks
APP_OPEN_TELEMETRY_ENABLED=true
# Optional additional Seq export
APP_OPEN_TELEMETRY_SEQ_ENABLED=true
APP_OPEN_TELEMETRY_SEQ_ENDPOINT=http://localhost:10150/ingest/otlp/v1/logs
APP_OPEN_TELEMETRY_SEQ_API_KEY=your-seq-api-key

# Optional Aspire export
APP_OPEN_TELEMETRY_ASPIRE_ENABLED=true

# Optional explicit Aspire endpoint override. When AppHost launches the client,
# it injects OTEL_EXPORTER_OTLP_ENDPOINT at runtime if the app does not already have one.
# APP_OPEN_TELEMETRY_ASPIRE_ENDPOINT=http://localhost:4318/v1/logs

# Application version for telemetry context
APP_VERSION=1.0.0

# Optional shared app name / cloud role overrides
APP_NAME=SystemUptimeTracker
APP_INSIGHTS_CLOUD_ROLE=systemuptimetracker-web
```

#### Log Levels

| Level       | Value | Description                                  |
| ----------- | ----- | -------------------------------------------- |
| Trace       | 0     | Very detailed logs, only for development     |
| Debug       | 1     | Debugging information                        |
| Information | 2     | General operational entries (default)        |
| Warning     | 3     | Indications of possible issues               |
| Error       | 4     | Errors and exceptions that cannot be handled |
| Critical    | 5     | Fatal errors causing premature termination   |
| None        | 6     | Disable logging                              |

### Usage

#### Server-Side Logging

Use the server logger in server components, server actions, and API routes:

```javascript
import { createLogger } from "@/utils/logger-server";

// In a server component or server action
export default async function MyServerComponent() {
  const log = await createLogger("MyComponent");

  await log.info("Component rendering started");

  try {
    const data = await fetchData();
    await log.debug("Data fetched successfully", { count: data.length });
    return <MyComponent data={data} />;
  } catch (error) {
    await log.error("Failed to fetch data", error, {
      component: "MyComponent",
    });
    throw error;
  }
}
```

#### Available Server Logger Methods

```javascript
const log = await createLogger("CategoryName");

// Log at various levels
await log.trace("Very detailed message");
await log.debug("Debug message", { key: "value" });
await log.info("Informational message");
await log.warn("Warning message");
await log.error("Error message", error, { context: "..." });
await log.critical("Critical error", error);

// Track custom events
await log.trackEvent("EventName", { prop: "value" }, { metric: 123 });

// Track metrics
await log.trackMetric("MetricName", 42, { dimension: "value" });
```

#### Client-Side Logging

For client components, use the secure client logger that routes through the server:

```javascript
"use client";

import {
  createClientLogger,
  trackEvent,
  trackPageView,
} from "@/utils/logger-client";

// Create a logger instance for a category
const log = createClientLogger("StudentManagement");

// Log messages (routed through server)
await log.info("Page loaded");
await log.warn("Something might be wrong");
await log.error("Something failed", new Error("details"));

// Track a custom event
await log.trackEvent("StudentAdded", {
  class: "Seminary",
});

// Track page view
await log.trackPageView("StudentList");
```

#### Standalone Client Functions

For one-off logging without creating a logger instance:

```javascript
import {
  logInfo,
  logWarn,
  logError,
  trackEvent,
  trackPageView,
  trackException,
} from "@/utils/logger-client";

// Quick logging
await logInfo("Something happened");
await logWarn("Warning message");
await logError("Error occurred", new Error("details"));

// Quick event tracking
await trackEvent("ButtonClicked", { buttonName: "Submit" });
await trackPageView("Dashboard");
await trackException(new Error("Unhandled error"));
```

### File Structure

```
src/utils/
├── logger-server.js       # Server-side logger (wraps Application Insights)
├── logger-server-actions.js # Next.js server action wrappers for client telemetry
├── logger-server.test.js  # Tests for server logger
├── logger-client.js       # Client-side logger (routes to server)
├── logger-client.test.js  # Tests for client logger
└── env/
    └── env-provider.jsx   # EnvProvider for environment context

src/components/
└── LoggerClientWrapper.jsx # Client component that initializes logging
```

### Automatic Startup Events

#### Server Startup

When the server initializes, it automatically logs:

- **"Application Started"** with version number and environment

#### Client Startup

When the client loads, `LoggerClientWrapper` automatically logs:

- **"Frontend application startup completed"** with user context (if authenticated)
- Initial page view tracking

### Trace IDs In Telemetry

When an error includes a `traceId`, both the client and server logging helpers should carry that value forward in structured properties. This allows a user-visible `Trace ID` from the UI or API response to line up with the detailed server-side telemetry event that captured the original exception.

### Security Considerations

1. **Connection String Protection**: The connection string is kept entirely server-side. It is NEVER exposed to the browser.

2. **No Client-Side SDK**: Unlike traditional implementations, we do NOT use `@microsoft/applicationinsights-web` for telemetry. All telemetry goes through server actions.

3. **PII Prevention**: Never log personally identifiable information. Use non-PII identifiers (like token subject IDs) for user context.

4. **Environment Isolation**: Use different Application Insights resources for each environment (dev, test, prod).

### Migration from appInsights.js

If you were previously using `appInsights.js` directly, update your imports:

#### Before (DEPRECATED)

```javascript
import { trackEvent, trackException } from "@/utils/telemetry/appInsights";
```

#### After (RECOMMENDED)

```javascript
import { trackEvent, trackException } from "@/utils/logger-client";
```

### Troubleshooting

#### Server-Side Logs Not Appearing

1. Check `APP_LOGGING_LEVEL` is not set to `None`
2. Verify `APP_INSIGHTS_ENABLED` is `true` and `APP_INSIGHTS_KEY` contains a valid connection string if Application Insights is expected
3. If OTLP telemetry is expected, verify `APP_OPEN_TELEMETRY_ENABLED` is `true`
4. If Seq export is expected, verify `APP_OPEN_TELEMETRY_SEQ_ENABLED` and `APP_OPEN_TELEMETRY_SEQ_ENDPOINT`
5. If Aspire export is expected, verify `APP_OPEN_TELEMETRY_ASPIRE_ENABLED` and either `APP_OPEN_TELEMETRY_ASPIRE_ENDPOINT` or `OTEL_EXPORTER_OTLP_ENDPOINT`
6. Check the target Application Insights resource, Seq instance, or Aspire dashboard collector is reachable

#### Client-Side Telemetry Not Working

1. Ensure `LoggerClientWrapper` wraps your component tree in `layout.jsx`
2. Check browser console for errors
3. Verify server actions are accessible

#### Testing Locally

Set `APP_LOGGING_LEVEL=Debug` to see detailed logging output in the console during development.
