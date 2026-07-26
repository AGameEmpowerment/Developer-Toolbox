import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const env = process.env as Record<string, string | undefined>;

let originalAppVersion;
let originalNodeEnv;
let originalLoggingLevel;
let originalAppInsightsEnabled;
let originalOpenTelemetryEnabled;
let originalOpenTelemetrySeqEnabled;
let originalOpenTelemetrySeqEndpoint;
let originalOpenTelemetrySeqApiKey;
let originalOpenTelemetryAspireEnabled;
let originalOpenTelemetryAspireEndpoint;
let originalOtelExporterOtlpEndpoint;
let originalAppInsightsKey;

describe("get-client-env-data", () => {
  beforeEach(() => {
    originalAppVersion = process.env.APP_VERSION;
    originalNodeEnv = process.env.NODE_ENV;
    originalLoggingLevel = process.env.APP_LOGGING_LEVEL;
    originalAppInsightsEnabled = process.env.APP_INSIGHTS_ENABLED;
    originalOpenTelemetryEnabled = process.env.APP_OPEN_TELEMETRY_ENABLED;
    originalOpenTelemetrySeqEnabled =
      process.env.APP_OPEN_TELEMETRY_SEQ_ENABLED;
    originalOpenTelemetrySeqEndpoint =
      process.env.APP_OPEN_TELEMETRY_SEQ_ENDPOINT;
    originalOpenTelemetrySeqApiKey = process.env.APP_OPEN_TELEMETRY_SEQ_API_KEY;
    originalOpenTelemetryAspireEnabled =
      process.env.APP_OPEN_TELEMETRY_ASPIRE_ENABLED;
    originalOpenTelemetryAspireEndpoint =
      process.env.APP_OPEN_TELEMETRY_ASPIRE_ENDPOINT;
    originalOtelExporterOtlpEndpoint = process.env.OTEL_EXPORTER_OTLP_ENDPOINT;
    originalAppInsightsKey = process.env.APP_INSIGHTS_KEY;
  });

  afterEach(() => {
    vi.resetModules();

    env.APP_VERSION = originalAppVersion;
    env.NODE_ENV = originalNodeEnv;
    env.APP_LOGGING_LEVEL = originalLoggingLevel;
    env.APP_INSIGHTS_ENABLED = originalAppInsightsEnabled;
    env.APP_OPEN_TELEMETRY_ENABLED = originalOpenTelemetryEnabled;
    env.APP_OPEN_TELEMETRY_SEQ_ENABLED = originalOpenTelemetrySeqEnabled;
    env.APP_OPEN_TELEMETRY_SEQ_ENDPOINT = originalOpenTelemetrySeqEndpoint;
    env.APP_OPEN_TELEMETRY_SEQ_API_KEY = originalOpenTelemetrySeqApiKey;
    env.APP_OPEN_TELEMETRY_ASPIRE_ENABLED = originalOpenTelemetryAspireEnabled;
    env.APP_OPEN_TELEMETRY_ASPIRE_ENDPOINT =
      originalOpenTelemetryAspireEndpoint;
    env.OTEL_EXPORTER_OTLP_ENDPOINT = originalOtelExporterOtlpEndpoint;
    env.APP_INSIGHTS_KEY = originalAppInsightsKey;
  });

  it("should only expose safe runtime metadata to EnvProvider consumers", async () => {
    env.APP_VERSION = "2.3.4";
    env.NODE_ENV = "production";
    env.APP_LOGGING_LEVEL = "Error";
    env.APP_INSIGHTS_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_SEQ_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_SEQ_ENDPOINT =
      "http://localhost:10150/ingest/otlp/v1/logs";
    env.APP_OPEN_TELEMETRY_SEQ_API_KEY = "seq-api-key";
    env.APP_OPEN_TELEMETRY_ASPIRE_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_ASPIRE_ENDPOINT = "https://localhost:4318/v1/logs";
    env.OTEL_EXPORTER_OTLP_ENDPOINT = "https://localhost:4318";
    env.APP_INSIGHTS_KEY =
      "InstrumentationKey=11111111-1111-1111-1111-111111111111";

    const { getClientEnvData } = await import("./get-client-env-data");

    await expect(getClientEnvData()).resolves.toEqual({
      appVersion: "2.3.4",
      environment: "production",
      loggingLevel: "Error",
    });
  });
});
