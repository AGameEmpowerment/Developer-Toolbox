import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const env = process.env as Record<string, string | undefined>;
let logSpy;
let debugSpy;
let infoSpy;
let warnSpy;
let errorSpy;

const mockedDefaultClient = {
  context: {
    tags: {},
    keys: {
      applicationVersion: "ai.application.version",
      cloudRole: "ai.cloud.role",
    },
  },
  trackTrace: vi.fn(),
  trackException: vi.fn(),
  trackEvent: vi.fn(),
  trackMetric: vi.fn(),
  trackPageView: vi.fn(),
  flush: vi.fn(({ callback }: { callback?: () => void } = {}) => {
    callback?.();
  }),
};

const mockedApplicationInsights = {
  setup: vi.fn().mockReturnThis(),
  setAutoCollectRequests: vi.fn().mockReturnThis(),
  setAutoCollectPerformance: vi.fn().mockReturnThis(),
  setAutoCollectExceptions: vi.fn().mockReturnThis(),
  setAutoCollectDependencies: vi.fn().mockReturnThis(),
  setAutoCollectConsole: vi.fn().mockReturnThis(),
  setUseDiskRetryCaching: vi.fn().mockReturnThis(),
  start: vi.fn(),
  defaultClient: mockedDefaultClient,
};

const mockedOpenTelemetryLogger = {
  emit: vi.fn(),
};

const mockedOpenTelemetrySpan = {
  addEvent: vi.fn(),
  recordException: vi.fn(),
  setStatus: vi.fn(),
  end: vi.fn(),
};

const mockedOpenTelemetryTracer = {
  startSpan: vi.fn(() => mockedOpenTelemetrySpan),
};

const mockedOpenTelemetryLoggerProvider = {
  getLogger: vi.fn(() => mockedOpenTelemetryLogger),
  forceFlush: vi.fn().mockResolvedValue(undefined),
  shutdown: vi.fn().mockResolvedValue(undefined),
};

const mockedOpenTelemetryTracerProvider = {
  getTracer: vi.fn(() => mockedOpenTelemetryTracer),
  forceFlush: vi.fn().mockResolvedValue(undefined),
  shutdown: vi.fn().mockResolvedValue(undefined),
};

const mockedLoggerProviderCtor = vi.fn(() => mockedOpenTelemetryLoggerProvider);
const mockedBatchLogRecordProcessor = vi.fn((processor) => ({
  processor,
}));
const mockedOtlpLogExporter = vi.fn((config) => ({
  config,
  export: vi.fn((_records, callback) => {
    callback({ code: 0 });
  }),
  shutdown: vi.fn().mockResolvedValue(undefined),
}));
const mockedBasicTracerProviderCtor = vi.fn(
  () => mockedOpenTelemetryTracerProvider,
);
const mockedBatchSpanProcessor = vi.fn((processor) => ({
  processor,
}));
const mockedOtlpTraceExporter = vi.fn((config) => ({
  config,
  export: vi.fn((_spans, callback) => {
    callback({ code: 0 });
  }),
  shutdown: vi.fn().mockResolvedValue(undefined),
}));
const mockedDefaultResource = {
  merge: vi.fn((other) => other ?? mockedDefaultResource),
};
const mockedDefaultResourceFactory = vi.fn(() => mockedDefaultResource);
const mockedResourceFromAttributes = vi.fn((attributes) => ({
  attributes,
  merge: vi.fn((other) => other),
}));

vi.mock("server-only", () => ({}));

vi.mock("applicationinsights", () => ({
  ...mockedApplicationInsights,
  default: mockedApplicationInsights,
}));

vi.mock("@opentelemetry/api", async () => {
  const actualModule = await vi.importActual("@opentelemetry/api");
  return {
    __esModule: true,
    ...actualModule,
    default: actualModule,
  };
});

const mockedOtlpLogsModule = {
  OTLPLogExporter: mockedOtlpLogExporter,
};

const mockedSdkLogsModule = {
  LoggerProvider: mockedLoggerProviderCtor,
  BatchLogRecordProcessor: mockedBatchLogRecordProcessor,
};

vi.mock("@opentelemetry/exporter-logs-otlp-proto", () => ({
  __esModule: true,
  ...mockedOtlpLogsModule,
  default: mockedOtlpLogsModule,
}));

vi.mock("@opentelemetry/sdk-logs", () => ({
  __esModule: true,
  ...mockedSdkLogsModule,
  default: mockedSdkLogsModule,
}));

const mockedOtlpTracesModule = {
  OTLPTraceExporter: mockedOtlpTraceExporter,
};

const mockedSdkTraceBaseModule = {
  BasicTracerProvider: mockedBasicTracerProviderCtor,
  BatchSpanProcessor: mockedBatchSpanProcessor,
};

vi.mock("@opentelemetry/exporter-trace-otlp-proto", () => ({
  __esModule: true,
  ...mockedOtlpTracesModule,
  default: mockedOtlpTracesModule,
}));

vi.mock("@opentelemetry/sdk-trace-base", () => ({
  __esModule: true,
  ...mockedSdkTraceBaseModule,
  default: mockedSdkTraceBaseModule,
}));

const mockedResourcesModule = {
  defaultResource: mockedDefaultResourceFactory,
  resourceFromAttributes: mockedResourceFromAttributes,
};

vi.mock("@opentelemetry/resources", () => ({
  __esModule: true,
  ...mockedResourcesModule,
  default: mockedResourcesModule,
}));

const validConnectionString =
  "InstrumentationKey=11111111-1111-1111-1111-111111111111;IngestionEndpoint=https://westus-0.in.applicationinsights.azure.com/";

let loggerModule;
let mockedAppInsights;
let mockedClient;

function getLoggerState() {
  return globalThis.__systemUptimeTrackerLoggerState;
}

async function getOpenTelemetryLoggerProviderPrototype(): Promise<{
  getLogger: (...args: unknown[]) => unknown;
  forceFlush: () => Promise<unknown>;
}> {
  const actualModule = await vi.importActual<
    typeof import("@opentelemetry/sdk-logs")
  >("@opentelemetry/sdk-logs");
  return (
    actualModule as {
      LoggerProvider: {
        prototype: {
          getLogger: (...args: unknown[]) => unknown;
          forceFlush: () => Promise<unknown>;
        };
      };
    }
  ).LoggerProvider.prototype;
}

async function getOpenTelemetryTracerProviderPrototype(): Promise<{
  getTracer: (...args: unknown[]) => unknown;
}> {
  const actualModule = await vi.importActual<
    typeof import("@opentelemetry/sdk-trace-base")
  >("@opentelemetry/sdk-trace-base");
  return (
    actualModule as {
      BasicTracerProvider: {
        prototype: {
          getTracer: (...args: unknown[]) => unknown;
        };
      };
    }
  ).BasicTracerProvider.prototype;
}

describe("logger-server", () => {
  beforeEach(async () => {
    vi.resetModules();
    vi.clearAllMocks();
    delete globalThis.__systemUptimeTrackerLoggerState;

    env.APP_LOGGING_LEVEL = "Information";
    env.APP_INSIGHTS_ENABLED = "true";
    env.APP_INSIGHTS_KEY = validConnectionString;
    env.NODE_ENV = "development";
    env.APP_OPEN_TELEMETRY_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_SEQ_ENABLED = "false";
    env.APP_OPEN_TELEMETRY_ASPIRE_ENABLED = "false";
    delete env.APP_OPEN_TELEMETRY_SEQ_ENDPOINT;
    delete env.APP_OPEN_TELEMETRY_SEQ_API_KEY;
    delete env.APP_OPEN_TELEMETRY_ASPIRE_ENDPOINT;
    delete env.OTEL_EXPORTER_OTLP_ENDPOINT;
    delete env.OTEL_SERVICE_NAME;
    delete env.OTEL_RESOURCE_ATTRIBUTES;
    env.APP_VERSION = "1.0.0";
    delete env.APP_NAME;
    delete env.APP_INSIGHTS_CLOUD_ROLE;

    logSpy = vi.spyOn(console, "log").mockImplementation(() => {});
    debugSpy = vi.spyOn(console, "debug").mockImplementation(() => {});
    infoSpy = vi.spyOn(console, "info").mockImplementation(() => {});
    warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});
    errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});

    loggerModule = await import("./logger-server");
    ({ default: mockedAppInsights } = await import("applicationinsights"));
    mockedClient = mockedAppInsights.defaultClient;
    mockedClient.context.tags = {};
    mockedClient.flush.mockImplementation(
      ({ callback }: { callback?: () => void } = {}) => {
        callback?.();
      },
    );
    mockedDefaultResource.merge.mockImplementation(
      (other) => other ?? mockedDefaultResource,
    );
    mockedOtlpTraceExporter.mockImplementation((config) => ({
      config,
      export: vi.fn((_spans, callback) => {
        callback({ code: 0 });
      }),
      shutdown: vi.fn().mockResolvedValue(undefined),
    }));
    mockedOpenTelemetryTracer.startSpan.mockReturnValue(
      mockedOpenTelemetrySpan,
    );
    mockedOpenTelemetrySpan.addEvent.mockReturnValue(undefined);
    mockedOpenTelemetrySpan.recordException.mockReturnValue(undefined);
    mockedOpenTelemetrySpan.setStatus.mockReturnValue(undefined);
    mockedOpenTelemetrySpan.end.mockReturnValue(undefined);
  });

  afterEach(() => {
    delete globalThis.__systemUptimeTrackerLoggerState;
    vi.useRealTimers();
    logSpy?.mockRestore();
    debugSpy?.mockRestore();
    infoSpy?.mockRestore();
    warnSpy?.mockRestore();
    errorSpy?.mockRestore();
  });

  it("should create a Logger instance with the expected methods", async () => {
    const logger = await loggerModule.createLogger("TestCategory");

    expect(logger).toBeDefined();
    expect(typeof logger.trace).toBe("function");
    expect(typeof logger.debug).toBe("function");
    expect(typeof logger.info).toBe("function");
    expect(typeof logger.information).toBe("function");
    expect(typeof logger.warn).toBe("function");
    expect(typeof logger.warning).toBe("function");
    expect(typeof logger.error).toBe("function");
    expect(typeof logger.critical).toBe("function");
    expect(typeof logger.trackEvent).toBe("function");
    expect(typeof logger.trackMetric).toBe("function");
    expect(typeof logger.flush).toBe("function");
  });

  it("should skip Application Insights setup when the connection string is invalid", async () => {
    process.env.APP_INSIGHTS_KEY = "not-a-connection-string";

    await loggerModule.initializeLogger();

    expect(mockedAppInsights.setup).not.toHaveBeenCalled();
    expect(console.warn).toHaveBeenCalledWith(
      expect.stringContaining("connection string not configured"),
    );
  });

  it("should treat APP_OPEN_TELEMETRY_ENABLED as the master switch for OpenTelemetry sinks only", async () => {
    process.env.APP_OPEN_TELEMETRY_ENABLED = "false";
    process.env.APP_OPEN_TELEMETRY_SEQ_ENABLED = "true";
    process.env.APP_OPEN_TELEMETRY_ASPIRE_ENABLED = "true";
    process.env.APP_OPEN_TELEMETRY_SEQ_ENDPOINT =
      "http://localhost:10150/ingest/otlp/v1/logs";
    process.env.APP_OPEN_TELEMETRY_SEQ_API_KEY = "seq-api-key";
    process.env.OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4318";

    await loggerModule.initializeLogger();

    expect(mockedAppInsights.setup).toHaveBeenCalledTimes(1);
    expect(mockedOtlpLogExporter).not.toHaveBeenCalled();
    expect(mockedOtlpTraceExporter).not.toHaveBeenCalled();
  });

  it("should allow Application Insights to be disabled independently", async () => {
    process.env.APP_INSIGHTS_ENABLED = "false";
    process.env.APP_OPEN_TELEMETRY_ENABLED = "true";
    process.env.APP_OPEN_TELEMETRY_ASPIRE_ENABLED = "true";
    process.env.OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4318";

    await loggerModule.initializeLogger();

    expect(mockedAppInsights.setup).not.toHaveBeenCalled();
    expect(mockedOtlpLogExporter).toHaveBeenCalledWith({
      url: "http://localhost:4318/v1/logs",
    });
    expect(mockedOtlpTraceExporter).toHaveBeenCalledWith({
      url: "http://localhost:4318/v1/traces",
    });
  });

  it("should retry initialization when configuration changes from invalid to valid", async () => {
    process.env.APP_INSIGHTS_KEY = "not-a-connection-string";

    await loggerModule.initializeLogger();
    expect(mockedAppInsights.setup).not.toHaveBeenCalled();

    process.env.APP_INSIGHTS_KEY = validConnectionString;

    const logger = await loggerModule.createLogger("RetryLogger");
    await logger.info("Telemetry enabled");

    expect(mockedAppInsights.setup).toHaveBeenCalledTimes(1);
    expect(mockedAppInsights.setup).toHaveBeenCalledWith(validConnectionString);
    expect(mockedClient.trackTrace).toHaveBeenCalledWith(
      expect.objectContaining({
        properties: expect.objectContaining({
          surface: "frontend",
          source: "server",
          environment: "development",
          appName: "systemuptimetracker-web",
          cloudRole: "systemuptimetracker-web",
          appVersion: "1.0.0",
          category: "RetryLogger",
          logLevel: "Information",
        }),
      }),
    );
  });

  it("should fingerprint secrets in the initialization signature while still detecting config changes", async () => {
    process.env.APP_OPEN_TELEMETRY_ENABLED = "true";
    process.env.APP_OPEN_TELEMETRY_SEQ_ENABLED = "true";
    process.env.APP_OPEN_TELEMETRY_SEQ_ENDPOINT =
      "http://localhost:10150/ingest/otlp/v1/logs";
    process.env.APP_OPEN_TELEMETRY_SEQ_API_KEY = "seq-api-key";

    await loggerModule.initializeLogger();

    const initialSignature =
      globalThis.__systemUptimeTrackerLoggerState.initializedSignature;

    expect(initialSignature).not.toContain(validConnectionString);
    expect(initialSignature).not.toContain("seq-api-key");
    expect(mockedOtlpLogExporter).toHaveBeenCalledTimes(1);

    process.env.APP_OPEN_TELEMETRY_SEQ_API_KEY = "seq-api-key-rotated";
    delete globalThis.__systemUptimeTrackerLoggerState;

    await loggerModule.initializeLogger();

    const rotatedSignature =
      globalThis.__systemUptimeTrackerLoggerState.initializedSignature;

    expect(rotatedSignature).not.toContain(validConnectionString);
    expect(rotatedSignature).not.toContain("seq-api-key-rotated");
    expect(rotatedSignature).not.toBe(initialSignature);
  });

  // Deferred in issue #197 while the Vitest ESM/OpenTelemetry module interop is stabilized.
  it.skip("[#197] should initialize the Seq OpenTelemetry exporter when frontend flags are enabled", async () => {
    env.APP_OPEN_TELEMETRY_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_SEQ_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_SEQ_ENDPOINT =
      "http://localhost:10150/ingest/otlp/v1/logs";
    env.APP_OPEN_TELEMETRY_SEQ_API_KEY = "seq-api-key";
    env.APP_INSIGHTS_CLOUD_ROLE = "systemuptimetracker-web";
    const loggerProviderPrototype =
      await getOpenTelemetryLoggerProviderPrototype();
    const getLoggerSpy = vi.spyOn(loggerProviderPrototype, "getLogger");

    await loggerModule.initializeLogger();

    expect(mockedOtlpLogExporter).toHaveBeenCalledWith({
      url: "http://localhost:10150/ingest/otlp/v1/logs",
      headers: {
        "X-Seq-ApiKey": "seq-api-key",
      },
    });
    expect(mockedResourceFromAttributes).toHaveBeenCalledWith({
      "service.name": "systemuptimetracker-web",
      "deployment.environment": "development",
      "deployment.version": "1.0.0",
    });
    expect(getLoggerSpy).toHaveBeenCalledWith("systemuptimetracker-web");
    expect(getLoggerState().openTelemetryLogger).toBeDefined();
    getLoggerSpy.mockRestore();
  });

  it.skip("[#197] should initialize Seq and Aspire OpenTelemetry exporters together when both sinks are enabled", async () => {
    env.APP_INSIGHTS_ENABLED = "false";
    env.APP_OPEN_TELEMETRY_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_SEQ_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_SEQ_ENDPOINT =
      "http://localhost:10150/ingest/otlp/v1/logs";
    env.APP_OPEN_TELEMETRY_SEQ_API_KEY = "seq-api-key";
    env.APP_OPEN_TELEMETRY_ASPIRE_ENABLED = "true";
    env.OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4318";
    const tracerProviderPrototype =
      await getOpenTelemetryTracerProviderPrototype();
    const getTracerSpy = vi.spyOn(tracerProviderPrototype, "getTracer");

    await loggerModule.initializeLogger();

    expect(mockedOtlpLogExporter).toHaveBeenNthCalledWith(1, {
      url: "http://localhost:10150/ingest/otlp/v1/logs",
      headers: {
        "X-Seq-ApiKey": "seq-api-key",
      },
    });
    expect(mockedOtlpLogExporter).toHaveBeenNthCalledWith(2, {
      url: "http://localhost:4318/v1/logs",
    });
    expect(mockedOtlpTraceExporter).toHaveBeenNthCalledWith(1, {
      url: "http://localhost:10150/ingest/otlp/v1/traces",
      headers: {
        "X-Seq-ApiKey": "seq-api-key",
      },
    });
    expect(mockedOtlpTraceExporter).toHaveBeenNthCalledWith(2, {
      url: "http://localhost:4318/v1/traces",
    });
    expect(getLoggerState().openTelemetryLoggerProvider).toBeDefined();
    expect(getLoggerState().openTelemetryTracerProvider).toBeDefined();
    expect(getTracerSpy).toHaveBeenCalledTimes(1);
    getTracerSpy.mockRestore();
  });

  it.skip("[#197] should prefer OTEL_SERVICE_NAME for OpenTelemetry resource identity", async () => {
    env.APP_INSIGHTS_ENABLED = "false";
    env.APP_OPEN_TELEMETRY_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_ASPIRE_ENABLED = "true";
    env.OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4318";
    env.OTEL_SERVICE_NAME = "systemuptimetracker-web";
    env.APP_INSIGHTS_CLOUD_ROLE = "systemuptimetracker-web";
    const loggerProviderPrototype =
      await getOpenTelemetryLoggerProviderPrototype();
    const getLoggerSpy = vi.spyOn(loggerProviderPrototype, "getLogger");

    await loggerModule.initializeLogger();

    expect(mockedResourceFromAttributes).toHaveBeenCalledWith({
      "service.name": "systemuptimetracker-web",
      "deployment.environment": "development",
      "deployment.version": "1.0.0",
    });
    expect(getLoggerSpy).toHaveBeenCalledWith("systemuptimetracker-web");
    getLoggerSpy.mockRestore();
  });

  it("should fall back to OTEL_RESOURCE_ATTRIBUTES service.name when OTEL_SERVICE_NAME is absent", async () => {
    process.env.APP_INSIGHTS_ENABLED = "false";
    process.env.APP_OPEN_TELEMETRY_ENABLED = "true";
    process.env.APP_OPEN_TELEMETRY_ASPIRE_ENABLED = "true";
    process.env.OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4318";
    process.env.OTEL_RESOURCE_ATTRIBUTES =
      "service.name=systemuptimetracker-web,service.instance.id=abc123";

    await loggerModule.initializeLogger();

    expect(mockedResourceFromAttributes).toHaveBeenCalledWith({
      "service.name": "systemuptimetracker-web",
      "deployment.environment": "development",
      "deployment.version": "1.0.0",
    });
  });

  it("should reject insecure Seq endpoints outside development", async () => {
    env.NODE_ENV = "production";
    env.APP_OPEN_TELEMETRY_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_SEQ_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_SEQ_ENDPOINT =
      "http://localhost:10150/ingest/otlp/v1/logs";
    env.APP_OPEN_TELEMETRY_SEQ_API_KEY = "seq-api-key";

    await loggerModule.initializeLogger();

    expect(mockedOtlpLogExporter).not.toHaveBeenCalled();
    expect(console.warn).toHaveBeenCalledWith(
      expect.stringContaining("must use HTTPS outside development"),
    );
  });

  it.skip("[#197] should emit Seq OpenTelemetry log records for frontend server logs", async () => {
    env.APP_OPEN_TELEMETRY_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_SEQ_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_SEQ_ENDPOINT =
      "http://localhost:10150/ingest/otlp/v1/logs";
    env.APP_OPEN_TELEMETRY_SEQ_API_KEY = "seq-api-key";

    const logger = await loggerModule.createLogger("SeqLogger");
    const state = getLoggerState();
    const emitSpy = vi
      .spyOn(state.openTelemetryLogger, "emit")
      .mockImplementation(() => {});
    const forceFlushSpy = vi
      .spyOn(state.openTelemetryLoggerProvider, "forceFlush")
      .mockResolvedValue(undefined);
    await logger.error("Seq failure", new Error("boom"), {
      requestId: "req-789",
    });

    const emittedLogRecord = emitSpy.mock.calls.at(-1)?.[0] as {
      severityText: string;
      body: string;
      attributes: Record<string, unknown>;
    };

    expect(emittedLogRecord.severityText).toBe("Error");
    expect(emittedLogRecord.body).toBe("[SeqLogger] Seq failure");
    expect(emittedLogRecord.attributes).toEqual(
      expect.objectContaining({
        category: "SeqLogger",
        logLevel: "Error",
        appVersion: "1.0.0",
        requestId: "req-789",
        "exception.message": "boom",
        "exception.type": "Error",
      }),
    );
    expect(forceFlushSpy).toHaveBeenCalled();
    emitSpy.mockRestore();
    forceFlushSpy.mockRestore();
  });

  it("should include trace ids attached to error objects in telemetry properties", async () => {
    const logger = await loggerModule.createLogger("TraceLogger");
    const error = new Error("boom");
    error.traceId = "fedcba9876543210fedcba9876543210";

    await logger.error("Traceable failure", error);

    expect(mockedClient.trackException).toHaveBeenCalledWith(
      expect.objectContaining({
        properties: expect.objectContaining({
          traceId: "fedcba9876543210fedcba9876543210",
        }),
      }),
    );
  });

  it("should convert server-side error-like objects into exception telemetry", async () => {
    const logger = await loggerModule.createLogger("ErrorLikeLogger");

    await logger.error("Hydration failed", {
      message: "object boom",
      name: "HydrationError",
      stack: "HydrationError: object boom",
      traceId: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    });

    expect(mockedClient.trackException).toHaveBeenCalledWith(
      expect.objectContaining({
        exception: expect.objectContaining({
          message: "object boom",
          name: "HydrationError",
          stack: "HydrationError: object boom",
        }),
        properties: expect.objectContaining({
          traceId: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          surface: "frontend",
          source: "server",
          category: "ErrorLikeLogger",
          logLevel: "Error",
        }),
      }),
    );
  });

  it.skip("[#197] should not block initialization when the OpenTelemetry flush stalls", async () => {
    vi.useFakeTimers();
    env.APP_OPEN_TELEMETRY_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_SEQ_ENABLED = "true";
    env.APP_OPEN_TELEMETRY_SEQ_ENDPOINT =
      "http://localhost:10150/ingest/otlp/v1/logs";
    const loggerProviderPrototype =
      await getOpenTelemetryLoggerProviderPrototype();
    const forceFlushSpy = vi
      .spyOn(loggerProviderPrototype, "forceFlush")
      .mockReturnValue(new Promise<void>(() => {}));

    await expect(loggerModule.initializeLogger()).resolves.toBeUndefined();
    expect(forceFlushSpy).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(1000);
    forceFlushSpy.mockRestore();
  });

  it.skip("[#197] should not block request-path logging when the OpenTelemetry flush stalls", async () => {
    vi.useFakeTimers();
    process.env.APP_INSIGHTS_KEY = "not-a-connection-string";
    process.env.APP_OPEN_TELEMETRY_ENABLED = "true";
    process.env.APP_OPEN_TELEMETRY_SEQ_ENABLED = "true";
    process.env.APP_OPEN_TELEMETRY_SEQ_ENDPOINT =
      "http://localhost:10150/ingest/otlp/v1/logs";

    const logger = await loggerModule.createLogger("SeqLogger");
    const state = getLoggerState();
    const emitSpy = vi
      .spyOn(state.openTelemetryLogger, "emit")
      .mockImplementation(() => {});
    const forceFlushSpy = vi
      .spyOn(state.openTelemetryLoggerProvider, "forceFlush")
      .mockReturnValue(new Promise(() => {}));

    await expect(
      logger.error("Seq failure", new Error("boom")),
    ).resolves.toBeUndefined();

    expect(emitSpy).toHaveBeenCalled();
    expect(forceFlushSpy).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(1000);
    emitSpy.mockRestore();
    forceFlushSpy.mockRestore();
  });

  it("should prefer explicit cloud role overrides for telemetry context", async () => {
    process.env.APP_NAME = "SystemUptimeTracker";
    process.env.APP_INSIGHTS_CLOUD_ROLE = "systemuptimetracker-web";

    await loggerModule.initializeLogger();

    expect(mockedClient.context.tags["ai.cloud.role"]).toBe(
      "systemuptimetracker-web",
    );
  });

  it("should fall back to APP_NAME for cloud role when no override is provided", async () => {
    process.env.APP_NAME = "SystemUptimeTracker";

    await loggerModule.initializeLogger();

    expect(mockedClient.context.tags["ai.cloud.role"]).toBe(
      "SystemUptimeTracker",
    );
  });

  it("should not log the instrumentation key prefix during initialization", async () => {
    await loggerModule.initializeLogger();

    const loggedText = logSpy.mock.calls.flat().join(" ");
    expect(loggedText).not.toContain("Extracted instrumentation key");
  });

  it("should await flush completion for error telemetry", async () => {
    let flushed = false;
    mockedClient.flush.mockImplementationOnce(
      ({ callback }: { callback?: () => void } = {}) => {
        setTimeout(() => {
          flushed = true;
          callback?.();
        }, 0);
      },
    );

    const logger = await loggerModule.createLogger("FlushLogger");
    await logger.error("Something failed", new Error("boom"));

    expect(flushed).toBe(true);
    expect(mockedClient.trackException).toHaveBeenCalled();
  });

  it("should sanitize client log payloads before forwarding them", async () => {
    await loggerModule.clientLog(
      "Information",
      "x".repeat(2500),
      "ClientWidget",
      {
        good: "value",
        nested: {
          secret: "nope",
        },
        list: [1, 2, true],
        timestamp: "2026-03-01T00:00:00.000Z",
        huge: "y".repeat(600),
      },
    );

    const tracePayload = mockedClient.trackTrace.mock.calls.at(-1)[0];

    expect(tracePayload.properties).toEqual(
      expect.objectContaining({
        surface: "frontend",
        source: "client",
        environment: "development",
        appName: "systemuptimetracker-web",
        cloudRole: "systemuptimetracker-web",
        appVersion: "1.0.0",
        clientTimestamp: "2026-03-01T00:00:00.000Z",
        good: "value",
        list: "1, 2, true",
      }),
    );
    expect(tracePayload.properties).not.toHaveProperty("nested");
    expect(tracePayload.properties.huge.length).toBeLessThanOrEqual(512);
    expect(tracePayload.message.length).toBeLessThanOrEqual(2064);
  });

  it("should bound trace messages after prefixing a long category", async () => {
    const logger = await loggerModule.createLogger("C".repeat(400));

    await logger.info("m".repeat(3000));

    const tracePayload = mockedClient.trackTrace.mock.calls.at(-1)[0];

    expect(tracePayload.message.length).toBeLessThanOrEqual(2048);
    expect(tracePayload.properties.category).toHaveLength(128);
  });

  it("should sanitize client page view urls and measurements", async () => {
    await loggerModule.clientTrackPageView(
      "Stories",
      "https://example.com/stories?token=secret#section",
      {
        feature: "roster",
        nested: {
          hidden: true,
        },
      },
      {
        loadTime: 150,
        ignored: "abc",
      },
    );

    const pageViewPayload = mockedClient.trackPageView.mock.calls.at(-1)[0];

    expect(pageViewPayload.uri).toBe("https://example.com/stories");
    expect(pageViewPayload.measurements).toEqual({
      loadTime: 150,
    });
    expect(pageViewPayload.properties).toEqual(
      expect.objectContaining({
        feature: "roster",
        surface: "frontend",
        source: "client",
        environment: "development",
        appName: "systemuptimetracker-web",
        cloudRole: "systemuptimetracker-web",
        appVersion: "1.0.0",
      }),
    );
    expect(pageViewPayload.properties).not.toHaveProperty("nested");
  });

  it.skip("[#197] should emit OpenTelemetry spans for client log messages and flush traces", async () => {
    process.env.APP_INSIGHTS_ENABLED = "false";
    process.env.APP_OPEN_TELEMETRY_ENABLED = "true";
    process.env.APP_OPEN_TELEMETRY_ASPIRE_ENABLED = "true";
    process.env.OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4318";
    process.env.OTEL_SERVICE_NAME = "systemuptimetracker-web";
    await loggerModule.initializeLogger();
    const state = getLoggerState();
    const startSpanSpy = vi
      .spyOn(state.openTelemetryTracer, "startSpan")
      .mockReturnValue(mockedOpenTelemetrySpan);
    const forceFlushSpy = vi
      .spyOn(state.openTelemetryTracerProvider, "forceFlush")
      .mockResolvedValue(undefined);

    await loggerModule.clientLog("Information", "Hydrated route", "NavBar", {
      feature: "navigation",
    });

    expect(startSpanSpy).toHaveBeenCalledWith(
      "ClientLog: [NavBar] Hydrated route",
      {
        attributes: expect.objectContaining({
          category: "NavBar",
          source: "client",
          feature: "navigation",
          appVersion: "1.0.0",
          "telemetry.level": "Information",
          "telemetry.message": "[NavBar] Hydrated route",
        }),
      },
    );
    expect(mockedOpenTelemetrySpan.addEvent).toHaveBeenCalledWith(
      "telemetry.message",
      {
        "log.severity": "Information",
        "log.message": "[NavBar] Hydrated route",
      },
    );
    expect(mockedOpenTelemetrySpan.end).toHaveBeenCalledTimes(1);
    expect(forceFlushSpy).toHaveBeenCalledTimes(1);
    startSpanSpy.mockRestore();
    forceFlushSpy.mockRestore();
  });

  it.skip("[#197] should mark client exception spans as errors", async () => {
    process.env.APP_INSIGHTS_ENABLED = "false";
    process.env.APP_OPEN_TELEMETRY_ENABLED = "true";
    process.env.APP_OPEN_TELEMETRY_ASPIRE_ENABLED = "true";
    process.env.OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4318";
    await loggerModule.initializeLogger();
    const state = getLoggerState();
    const startSpanSpy = vi
      .spyOn(state.openTelemetryTracer, "startSpan")
      .mockReturnValue(mockedOpenTelemetrySpan);
    const forceFlushSpy = vi
      .spyOn(state.openTelemetryTracerProvider, "forceFlush")
      .mockResolvedValue(undefined);

    await loggerModule.clientTrackException(
      "Client failure",
      "Error: Client failure\n    at click (/app/page.js:10:1)",
      3,
      {
        route: "/stories",
      },
    );

    expect(startSpanSpy).toHaveBeenCalledWith(
      "ClientException: Client failure",
      {
        attributes: expect.objectContaining({
          source: "client",
          route: "/stories",
          severityLevel: "3",
          "telemetry.level": "Error",
          "telemetry.message": "Client failure",
        }),
      },
    );
    expect(mockedOpenTelemetrySpan.recordException).toHaveBeenCalledWith(
      expect.objectContaining({
        message: "Client failure",
        stack: "Error: Client failure\n    at click (/app/page.js:10:1)",
      }),
    );
    expect(mockedOpenTelemetrySpan.setStatus).toHaveBeenCalledWith({
      code: 2,
      message: "Client failure",
    });
    expect(forceFlushSpy).toHaveBeenCalledTimes(1);
    startSpanSpy.mockRestore();
    forceFlushSpy.mockRestore();
  });

  it("should clamp client exception severity and truncate stack traces", async () => {
    await loggerModule.clientTrackException(
      "Client failure",
      "x".repeat(9000),
      99,
      {
        flag: true,
        nested: {
          ignored: true,
        },
      },
    );

    const exceptionPayload = mockedClient.trackException.mock.calls.at(-1)[0];

    expect(exceptionPayload.severity).toBe(4);
    expect(exceptionPayload.exception.message).toBe("Client failure");
    expect(exceptionPayload.exception.stack.length).toBeLessThanOrEqual(8192);
    expect(exceptionPayload.properties).toEqual(
      expect.objectContaining({
        flag: "true",
        surface: "frontend",
        source: "client",
        environment: "development",
        appName: "systemuptimetracker-web",
        cloudRole: "systemuptimetracker-web",
        appVersion: "1.0.0",
      }),
    );
    expect(exceptionPayload.properties).not.toHaveProperty("nested");
  });

  it("should report current log level and level enablement after initialization", async () => {
    process.env.APP_LOGGING_LEVEL = "Warning";

    await loggerModule.initializeLogger();

    await expect(loggerModule.getCurrentLogLevel()).resolves.toBe("Warning");
    await expect(loggerModule.isLevelEnabled("Information")).resolves.toBe(
      false,
    );
    await expect(loggerModule.isLevelEnabled("Error")).resolves.toBe(true);
  });

  it("should default to information when APP_LOGGING_LEVEL is not configured", async () => {
    delete process.env.APP_LOGGING_LEVEL;

    await loggerModule.initializeLogger();

    await expect(loggerModule.getCurrentLogLevel()).resolves.toBe(
      "Information",
    );
    expect(mockedClient.trackEvent).toHaveBeenCalledWith(
      expect.objectContaining({
        name: "Application Started",
        properties: expect.objectContaining({
          surface: "frontend",
          source: "server",
          environment: "development",
          appName: "systemuptimetracker-web",
          cloudRole: "systemuptimetracker-web",
          appVersion: "1.0.0",
          category: "Application",
        }),
      }),
    );
  });

  it("should skip client page views when informational logging is disabled", async () => {
    process.env.APP_LOGGING_LEVEL = "Warning";

    await loggerModule.clientTrackPageView(
      "Stories",
      "https://example.com/stories",
    );

    expect(console.info).not.toHaveBeenCalled();
    expect(mockedClient.trackPageView).not.toHaveBeenCalled();
  });

  it("should flush safely even when telemetry is disabled", async () => {
    process.env.APP_LOGGING_LEVEL = "None";

    const logger = await loggerModule.createLogger("DisabledLogger");

    await expect(logger.flush()).resolves.toBeUndefined();
    expect(mockedClient.flush).not.toHaveBeenCalled();
    expect(mockedOpenTelemetryLoggerProvider.forceFlush).not.toHaveBeenCalled();
  });

  it("should reuse initialized telemetry across module reloads", async () => {
    await loggerModule.initializeLogger();

    vi.resetModules();
    loggerModule = await import("./logger-server");

    await loggerModule.initializeLogger();

    expect(mockedAppInsights.setup).toHaveBeenCalledTimes(1);
    expect(mockedAppInsights.start).toHaveBeenCalledTimes(1);
  });
});
