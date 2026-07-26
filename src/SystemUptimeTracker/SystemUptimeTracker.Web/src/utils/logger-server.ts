/**
 * Server-side logging utility that wraps optional telemetry sinks.
 *
 * This module provides ASP.NET Core-style logging levels and can send telemetry
 * to Azure Application Insights plus optional OpenTelemetry Seq and Aspire sinks
 * while keeping secrets server-side.
 *
 * SECURITY: All telemetry secrets are kept server-side. Client-side logging
 * routes through server actions defined in this module, ensuring no secrets
 * are exposed to the browser.
 *
 * Log Levels (matching ASP.NET Core):
 * - Trace (0): Very detailed logs, only for development
 * - Debug (1): Debugging information
 * - Information (2): General operational entries about application progress
 * - Warning (3): Indications of possible issues
 * - Error (4): Errors and exceptions that cannot be handled
 * - Critical (5): Fatal errors causing premature termination
 * - None (6): Disable logging
 *
 * Configuration via environment variables:
 * - APP_LOGGING_LEVEL: Current logging level (default: "Information")
 * - APP_INSIGHTS_ENABLED: Enables Azure Application Insights logging
 * - APP_INSIGHTS_KEY: Azure Application Insights connection string
 * - APP_OPEN_TELEMETRY_ENABLED: Master switch for OpenTelemetry server-side sinks
 * - APP_OPEN_TELEMETRY_SEQ_ENABLED: Enables the Seq OpenTelemetry sink
 * - APP_OPEN_TELEMETRY_SEQ_ENDPOINT: OTLP HTTP/protobuf endpoint used for Seq logs
 * - APP_OPEN_TELEMETRY_SEQ_API_KEY: Optional Seq API key sent in X-Seq-ApiKey
 * - APP_OPEN_TELEMETRY_ASPIRE_ENABLED: Enables the Aspire OpenTelemetry sink
 * - APP_OPEN_TELEMETRY_ASPIRE_ENDPOINT: Optional explicit OTLP HTTP/protobuf endpoint override for Aspire logs
 * - OTEL_EXPORTER_OTLP_ENDPOINT: Standard OTLP endpoint injected by orchestration when available
 * - APP_VERSION: Application version for telemetry context
 * - APP_NAME: Optional application name used for shared telemetry defaults
 * - APP_INSIGHTS_CLOUD_ROLE: Optional explicit cloud role override
 *
 * @module utils/logger-server
 */

import "server-only";

import { extractTraceId } from "@/utils/error-reference";

type TelemetryProperties = Record<string, string>;
type TelemetryMeasurements = Record<string, number>;
type OpenTelemetryAttributes = Record<string, string | number>;
type LoggerErrorLike =
  | Error
  | {
      message?: string;
      stack?: string;
      [key: string]: unknown;
    };

/**
 * Log level enumeration matching ASP.NET Core LogLevel
 * @readonly
 * @enum {number}
 */
const LogLevel = Object.freeze({
  Trace: 0,
  Debug: 1,
  Information: 2,
  Warning: 3,
  Error: 4,
  Critical: 5,
  None: 6,
});

// Canonical log level names aligned with APP_LOGGING_LEVEL in .env.example
export const LogLevelName = Object.freeze({
  TRACE: "Trace",
  DEBUG: "Debug",
  INFORMATION: "Information",
  WARNING: "Warning",
  ERROR: "Error",
  CRITICAL: "Critical",
  NONE: "None",
});

/**
 * Severity level mapping for Application Insights
 * @readonly
 * @enum {number}
 */
const AppInsightsSeverity = Object.freeze({
  Verbose: 0,
  Information: 1,
  Warning: 2,
  Error: 3,
  Critical: 4,
});

/**
 * Severity level mapping for OpenTelemetry logs
 * @readonly
 * @enum {number}
 */
const OpenTelemetrySeverityNumber = Object.freeze({
  Trace: 1,
  Debug: 5,
  Information: 9,
  Warning: 13,
  Error: 17,
  Critical: 21,
});

/**
 * Maps our LogLevel to Application Insights severity
 * @param {number} logLevel - Our internal log level
 * @returns {number} Application Insights severity level
 */
function mapToAppInsightsSeverity(logLevel) {
  switch (logLevel) {
    case LogLevel.Trace:
    case LogLevel.Debug:
      return AppInsightsSeverity.Verbose;
    case LogLevel.Information:
      return AppInsightsSeverity.Information;
    case LogLevel.Warning:
      return AppInsightsSeverity.Warning;
    case LogLevel.Error:
      return AppInsightsSeverity.Error;
    case LogLevel.Critical:
      return AppInsightsSeverity.Critical;
    default:
      return AppInsightsSeverity.Information;
  }
}

/**
 * Maps our LogLevel to OpenTelemetry severity numbers
 * @param {number} logLevel - Our internal log level
 * @returns {number} OpenTelemetry severity number
 */
function mapToOpenTelemetrySeverityNumber(logLevel) {
  switch (logLevel) {
    case LogLevel.Trace:
      return OpenTelemetrySeverityNumber.Trace;
    case LogLevel.Debug:
      return OpenTelemetrySeverityNumber.Debug;
    case LogLevel.Warning:
      return OpenTelemetrySeverityNumber.Warning;
    case LogLevel.Error:
      return OpenTelemetrySeverityNumber.Error;
    case LogLevel.Critical:
      return OpenTelemetrySeverityNumber.Critical;
    case LogLevel.Information:
    default:
      return OpenTelemetrySeverityNumber.Information;
  }
}

function mapClientSeverityToLogLevel(severityLevel) {
  if (severityLevel >= AppInsightsSeverity.Critical) {
    return LogLevel.Critical;
  }

  if (severityLevel >= AppInsightsSeverity.Error) {
    return LogLevel.Error;
  }

  if (severityLevel >= AppInsightsSeverity.Warning) {
    return LogLevel.Warning;
  }

  return LogLevel.Information;
}

/**
 * Parse log level string to LogLevel enum value
 * @param {string} levelStr - Log level string (case-insensitive)
 * @returns {number} LogLevel enum value
 */
function parseLogLevel(levelStr) {
  if (!levelStr || typeof levelStr !== "string") {
    return LogLevel.Information;
  }

  const normalized = levelStr.toLowerCase().trim();

  switch (normalized) {
    case "trace":
      return LogLevel.Trace;
    case "debug":
      return LogLevel.Debug;
    case "information":
    case "info":
      return LogLevel.Information;
    case "warning":
    case "warn":
      return LogLevel.Warning;
    case "error":
      return LogLevel.Error;
    case "critical":
    case "fatal":
      return LogLevel.Critical;
    case "none":
    case "off":
      return LogLevel.None;
    default:
      return LogLevel.Information;
  }
}

/**
 * Get log level name for display
 * @param {number} level - LogLevel enum value
 * @returns {string} Log level name
 */
function getLogLevelName(level) {
  switch (level) {
    case LogLevel.Trace:
      return "Trace";
    case LogLevel.Debug:
      return "Debug";
    case LogLevel.Information:
      return "Information";
    case LogLevel.Warning:
      return "Warning";
    case LogLevel.Error:
      return "Error";
    case LogLevel.Critical:
      return "Critical";
    case LogLevel.None:
      return "None";
    default:
      return "Unknown";
  }
}

function parseBooleanEnv(value, fallback = false) {
  if (typeof value === "boolean") {
    return value;
  }

  if (typeof value !== "string") {
    return fallback;
  }

  switch (value.trim().toLowerCase()) {
    case "1":
    case "true":
    case "yes":
    case "on":
      return true;
    case "0":
    case "false":
    case "no":
    case "off":
      return false;
    default:
      return fallback;
  }
}

const MAX_TELEMETRY_TEXT_LENGTH = 2048;
const MAX_TELEMETRY_CATEGORY_LENGTH = 128;
const MAX_TELEMETRY_STACK_LENGTH = 8192;
const MAX_TELEMETRY_PROPERTIES = 25;
const MAX_TELEMETRY_PROPERTY_KEY_LENGTH = 100;
const MAX_TELEMETRY_PROPERTY_VALUE_LENGTH = 512;
const MAX_TELEMETRY_MEASUREMENTS = 20;
const INSTRUMENTATION_KEY_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DEFAULT_CLOUD_ROLE = "systemuptimetracker-web";
const FRONTEND_SURFACE = "frontend";
const SERVER_TELEMETRY_SOURCE = "server";
const CLIENT_TELEMETRY_SOURCE = "client";
const APPLICATION_STARTED_EVENT_NAME = "Application Started";
const LOGGER_STATE_KEY = "__systemUptimeTrackerLoggerState";
const OPEN_TELEMETRY_FLUSH_TIMEOUT_MS = 1000;
const OPEN_TELEMETRY_TRACE_SCOPE_NAME = "systemuptimetracker.client.telemetry";
const MAX_TELEMETRY_SPAN_NAME_LENGTH = 160;

function getLoggerState() {
  if (!globalThis[LOGGER_STATE_KEY]) {
    globalThis[LOGGER_STATE_KEY] = {
      telemetryClient: undefined,
      isInitialized: false,
      hasLoggedStartup: false,
      currentLogLevel: LogLevel.Information,
      appVersion: "unknown",
      environment: "development",
      appName: DEFAULT_CLOUD_ROLE,
      cloudRole: DEFAULT_CLOUD_ROLE,
      lastFailedInitializationSignature: "",
      initializedSignature: "",
      initializationPromise: undefined,
      applicationInsightsModule: undefined,
      openTelemetryApiModule: undefined,
      openTelemetrySdkLogsModule: undefined,
      openTelemetryExporterModule: undefined,
      openTelemetrySdkTraceBaseModule: undefined,
      openTelemetryTraceExporterModule: undefined,
      openTelemetryResourcesModule: undefined,
      openTelemetryLoggerProvider: undefined,
      openTelemetryLogger: undefined,
      openTelemetryTracerProvider: undefined,
      openTelemetryTracer: undefined,
      pendingOpenTelemetryFlush: undefined,
      pendingOpenTelemetryTraceFlush: undefined,
    };
  }

  return globalThis[LOGGER_STATE_KEY];
}

function normalizeText(value, maxLength, fallback = "") {
  const rawValue =
    typeof value === "string"
      ? value
      : value === null || typeof value === "undefined"
        ? fallback
        : String(value);
  const normalizedValue = rawValue.trim();

  if (!normalizedValue) {
    return fallback;
  }

  return normalizedValue.length > maxLength
    ? normalizedValue.slice(0, maxLength)
    : normalizedValue;
}

function buildSecretFingerprint(value) {
  const normalizedValue = normalizeText(value, MAX_TELEMETRY_TEXT_LENGTH, "");

  if (!normalizedValue) {
    return "";
  }

  let hash = 2166136261;
  for (let index = 0; index < normalizedValue.length; index += 1) {
    hash ^= normalizedValue.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }

  return `hash:${(hash >>> 0).toString(16)}`;
}

function buildInitializationSignature(config) {
  return [
    config.logLevel,
    config.applicationInsightsEnabled,
    buildSecretFingerprint(config.connectionString),
    config.appVersion,
    config.environment,
    config.appName,
    config.cloudRole,
    config.openTelemetryEnabled,
    config.openTelemetrySeqEnabled,
    config.openTelemetrySeqEndpoint,
    buildSecretFingerprint(config.openTelemetrySeqApiKey),
    config.openTelemetryAspireEnabled,
    config.openTelemetryAspireEndpoint,
    config.openTelemetryServiceName,
  ].join("|");
}

function extractInstrumentationKey(connectionString) {
  const normalizedConnectionString = normalizeText(
    connectionString,
    MAX_TELEMETRY_TEXT_LENGTH,
    "",
  );

  if (!normalizedConnectionString) {
    return "";
  }

  if (INSTRUMENTATION_KEY_PATTERN.test(normalizedConnectionString)) {
    return normalizedConnectionString;
  }

  const match = normalizedConnectionString.match(
    /(?:^|;)InstrumentationKey=([0-9a-f-]{36})(?:;|$)/i,
  );

  return match?.[1] && INSTRUMENTATION_KEY_PATTERN.test(match[1])
    ? match[1]
    : "";
}

function hasValidConnectionString(connectionString) {
  return Boolean(extractInstrumentationKey(connectionString));
}

function hasValidOpenTelemetryEndpoint(
  endpoint,
  environment = process.env.NODE_ENV || "development",
) {
  const normalizedEndpoint = normalizeText(
    endpoint,
    MAX_TELEMETRY_TEXT_LENGTH,
    "",
  );

  if (!normalizedEndpoint) {
    return false;
  }

  try {
    const parsedEndpoint = new URL(normalizedEndpoint);
    if (parsedEndpoint.protocol === "https:") {
      return true;
    }

    return parsedEndpoint.protocol === "http:" && environment === "development";
  } catch {
    return false;
  }
}

function buildOpenTelemetrySignalEndpoint(endpoint, signalName) {
  const normalizedEndpoint = normalizeText(
    endpoint,
    MAX_TELEMETRY_TEXT_LENGTH,
    "",
  );
  const normalizedSignalName = normalizeText(
    signalName,
    MAX_TELEMETRY_PROPERTY_KEY_LENGTH,
    "",
  );

  if (!normalizedEndpoint || !normalizedSignalName) {
    return "";
  }

  const normalizedSignalPath = `/v1/${normalizedSignalName}`;

  try {
    const parsedEndpoint = new URL(normalizedEndpoint);
    const normalizedPath = parsedEndpoint.pathname.replace(/\/+$/, "");

    if (!normalizedPath) {
      parsedEndpoint.pathname = normalizedSignalPath;
    } else if (/\/v1\/(?:logs|traces|metrics)$/.test(normalizedPath)) {
      parsedEndpoint.pathname = normalizedPath.replace(
        /\/v1\/(?:logs|traces|metrics)$/,
        normalizedSignalPath,
      );
    } else if (normalizedPath.endsWith(normalizedSignalPath)) {
      parsedEndpoint.pathname = normalizedPath;
    } else {
      parsedEndpoint.pathname = `${normalizedPath}${normalizedSignalPath}`;
    }

    return parsedEndpoint.toString();
  } catch {
    return normalizedEndpoint;
  }
}

function buildOpenTelemetryEndpointWarningMessage(
  sinkName,
  endpoint,
  environment = process.env.NODE_ENV || "development",
) {
  const normalizedEndpoint = normalizeText(
    endpoint,
    MAX_TELEMETRY_TEXT_LENGTH,
    "",
  );

  if (
    normalizedEndpoint.startsWith("http://") &&
    environment !== "development"
  ) {
    return `[Logger-Server] OpenTelemetry ${sinkName} endpoint must use HTTPS outside development. ${sinkName} export will be skipped.`;
  }

  return normalizedEndpoint
    ? `[Logger-Server] OpenTelemetry ${sinkName} endpoint is invalid. ${sinkName} export will be skipped.`
    : `[Logger-Server] OpenTelemetry ${sinkName} endpoint not configured. ${sinkName} export will be skipped.`;
}

function getOpenTelemetryResourceAttribute(attributes, attributeName) {
  const normalizedAttributes = normalizeText(
    attributes,
    MAX_TELEMETRY_TEXT_LENGTH,
    "",
  );
  const normalizedAttributeName = normalizeText(
    attributeName,
    MAX_TELEMETRY_PROPERTY_KEY_LENGTH,
    "",
  );

  if (!normalizedAttributes || !normalizedAttributeName) {
    return "";
  }

  for (const pair of normalizedAttributes.split(",")) {
    const separatorIndex = pair.indexOf("=");

    if (separatorIndex <= 0) {
      continue;
    }

    const key = pair.slice(0, separatorIndex).trim();
    if (key !== normalizedAttributeName) {
      continue;
    }

    return normalizeText(
      pair.slice(separatorIndex + 1),
      MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
      "",
    );
  }

  return "";
}

function normalizeCategory(category = "") {
  return normalizeText(category, MAX_TELEMETRY_CATEGORY_LENGTH, "");
}

function buildTelemetryMessage(message, category = "") {
  const normalizedMessage = normalizeText(
    message,
    MAX_TELEMETRY_TEXT_LENGTH,
    "[message omitted]",
  );
  const categoryPrefix = category ? `[${category}] ` : "";

  return normalizeText(
    `${categoryPrefix}${normalizedMessage}`,
    MAX_TELEMETRY_TEXT_LENGTH,
    normalizedMessage,
  );
}

function buildOpenTelemetrySpanName(prefix, subject = "") {
  const normalizedPrefix = normalizeText(
    prefix,
    MAX_TELEMETRY_CATEGORY_LENGTH,
    "Telemetry",
  );
  const normalizedSubject = normalizeText(
    subject,
    MAX_TELEMETRY_TEXT_LENGTH,
    "",
  );

  if (!normalizedSubject) {
    return normalizeText(
      normalizedPrefix,
      MAX_TELEMETRY_SPAN_NAME_LENGTH,
      "Telemetry",
    );
  }

  return normalizeText(
    `${normalizedPrefix}: ${normalizedSubject}`,
    MAX_TELEMETRY_SPAN_NAME_LENGTH,
    normalizedPrefix,
  );
}

function resolveCloudRoleName(config) {
  const configuredCloudRole = normalizeText(
    config.cloudRole,
    MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
    "",
  );

  if (configuredCloudRole) {
    return configuredCloudRole;
  }

  return normalizeText(
    config.appName,
    MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
    DEFAULT_CLOUD_ROLE,
  );
}

function resolveApplicationName(config) {
  return normalizeText(
    config.appName,
    MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
    resolveCloudRoleName(config),
  );
}

function resolveOpenTelemetryServiceName(config) {
  const configuredServiceName = normalizeText(
    config.openTelemetryServiceName,
    MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
    "",
  );

  if (configuredServiceName) {
    return configuredServiceName;
  }

  return resolveCloudRoleName(config);
}

function buildOpenTelemetryHeaders(
  apiKey = "",
): Record<string, string> | undefined {
  const normalizedApiKey = normalizeText(
    apiKey,
    MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
    "",
  );

  return normalizedApiKey
    ? {
        "X-Seq-ApiKey": normalizedApiKey,
      }
    : undefined;
}

function buildSyntheticClientError(message, stack = "") {
  const syntheticError = new Error(message);

  if (stack) {
    syntheticError.stack = stack;
  }

  return syntheticError;
}

function toLoggerErrorInstance(
  errorLike: unknown,
  fallbackMessage = "Unhandled error",
) {
  if (errorLike instanceof Error) {
    return errorLike;
  }

  if (!errorLike || typeof errorLike !== "object") {
    return null;
  }

  const message = normalizeText(
    "message" in errorLike ? errorLike.message : "",
    MAX_TELEMETRY_TEXT_LENGTH,
    "",
  );
  const stack = normalizeText(
    "stack" in errorLike ? errorLike.stack : "",
    MAX_TELEMETRY_STACK_LENGTH,
    "",
  );
  const name = normalizeText(
    "name" in errorLike ? errorLike.name : "",
    MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
    "",
  );

  if (!message && !stack) {
    return null;
  }

  const syntheticError = buildSyntheticClientError(
    message || fallbackMessage,
    stack,
  );

  if (name) {
    syntheticError.name = name;
  }

  const traceId = extractTraceId(errorLike);
  if (traceId) {
    (syntheticError as Error & { traceId?: string }).traceId = traceId;
  }

  return syntheticError;
}

function normalizePropertyValue(value: unknown) {
  if (typeof value === "string") {
    return normalizeText(value, MAX_TELEMETRY_PROPERTY_VALUE_LENGTH, "");
  }

  if (typeof value === "number") {
    return Number.isFinite(value) ? String(value) : undefined;
  }

  if (typeof value === "boolean" || typeof value === "bigint") {
    return String(value);
  }

  if (value instanceof Date) {
    return value.toISOString();
  }

  if (Array.isArray(value)) {
    const normalizedItems = value
      .map((item) => normalizePropertyValue(item))
      .filter((item) => typeof item === "string" && item.length > 0);

    if (normalizedItems.length === 0) {
      return undefined;
    }

    return normalizeText(
      normalizedItems.join(", "),
      MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
      "",
    );
  }

  return undefined;
}

function normalizeProperties(properties: unknown = {}): TelemetryProperties {
  if (
    !properties ||
    typeof properties !== "object" ||
    Array.isArray(properties)
  ) {
    return {};
  }

  const normalizedProperties: TelemetryProperties = {};
  let propertyCount = 0;

  for (const [key, value] of Object.entries(properties)) {
    if (propertyCount >= MAX_TELEMETRY_PROPERTIES) {
      break;
    }

    const normalizedKey = normalizeText(
      key,
      MAX_TELEMETRY_PROPERTY_KEY_LENGTH,
      "",
    );

    if (!normalizedKey) {
      continue;
    }

    const normalizedValue = normalizePropertyValue(value);

    if (typeof normalizedValue === "undefined") {
      continue;
    }

    normalizedProperties[normalizedKey] = normalizedValue;
    propertyCount += 1;
  }

  return normalizedProperties;
}

function normalizeMeasurements(
  measurements: unknown = {},
): TelemetryMeasurements {
  if (
    !measurements ||
    typeof measurements !== "object" ||
    Array.isArray(measurements)
  ) {
    return {};
  }

  const normalizedMeasurements: TelemetryMeasurements = {};
  let measurementCount = 0;

  for (const [key, value] of Object.entries(measurements)) {
    if (measurementCount >= MAX_TELEMETRY_MEASUREMENTS) {
      break;
    }

    const normalizedKey = normalizeText(
      key,
      MAX_TELEMETRY_PROPERTY_KEY_LENGTH,
      "",
    );
    const normalizedValue =
      typeof value === "number" ? value : Number.parseFloat(String(value));

    if (!normalizedKey || !Number.isFinite(normalizedValue)) {
      continue;
    }

    normalizedMeasurements[normalizedKey] = normalizedValue;
    measurementCount += 1;
  }

  return normalizedMeasurements;
}

function buildOpenTelemetryAttributes(
  properties: unknown = {},
  measurements: unknown = {},
): OpenTelemetryAttributes {
  const attributes: OpenTelemetryAttributes = {
    ...normalizeProperties(properties),
  };

  for (const [key, value] of Object.entries(
    normalizeMeasurements(measurements),
  )) {
    attributes[`measurement.${key}`] = value;
  }

  return attributes;
}

function sanitizePageUrl(pageUrl = "") {
  const normalizedUrl = normalizeText(pageUrl, MAX_TELEMETRY_TEXT_LENGTH, "");

  if (!normalizedUrl) {
    return "";
  }

  try {
    if (normalizedUrl.startsWith("/")) {
      return normalizedUrl.split(/[?#]/, 1)[0];
    }

    const parsedUrl = new URL(normalizedUrl);
    return `${parsedUrl.origin}${parsedUrl.pathname}`;
  } catch {
    return normalizedUrl.split(/[?#]/, 1)[0];
  }
}

async function flushApplicationInsightsTelemetry() {
  const { telemetryClient } = getLoggerState();

  if (!telemetryClient) {
    return undefined;
  }

  return new Promise<void>((resolve) => {
    telemetryClient.flush({
      callback: () => resolve(),
    });
  });
}

function withTimeout(promise, timeoutMs, timeoutMessage) {
  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) {
    return promise;
  }

  let timeoutId;
  const timeoutPromise = new Promise((_, reject) => {
    timeoutId = setTimeout(() => {
      reject(new Error(timeoutMessage));
    }, timeoutMs);
  });

  return Promise.race([promise, timeoutPromise]).finally(() => {
    clearTimeout(timeoutId);
  });
}

function flushOpenTelemetryTelemetry(
  timeoutMs = OPEN_TELEMETRY_FLUSH_TIMEOUT_MS,
) {
  const state = getLoggerState();

  if (state.pendingOpenTelemetryFlush) {
    return state.pendingOpenTelemetryFlush;
  }

  if (!state.openTelemetryLoggerProvider) {
    return undefined;
  }

  return withTimeout(
    Promise.resolve(state.openTelemetryLoggerProvider.forceFlush()),
    timeoutMs,
    `[Logger-Server] OpenTelemetry flush timed out after ${timeoutMs}ms.`,
  );
}

function queueOpenTelemetryFlush(reason = "background telemetry") {
  const state = getLoggerState();

  if (!state.openTelemetryLoggerProvider) {
    return undefined;
  }

  if (state.pendingOpenTelemetryFlush) {
    return state.pendingOpenTelemetryFlush;
  }

  const pendingFlush = Promise.resolve(
    flushOpenTelemetryTelemetry(OPEN_TELEMETRY_FLUSH_TIMEOUT_MS),
  )
    .catch((error) => {
      console.warn(
        `[Logger-Server] OpenTelemetry flush did not complete during ${reason}:`,
        error,
      );
    })
    .finally(() => {
      if (state.pendingOpenTelemetryFlush === pendingFlush) {
        state.pendingOpenTelemetryFlush = undefined;
      }
    });

  state.pendingOpenTelemetryFlush = pendingFlush;
  return pendingFlush;
}

function flushOpenTelemetryTraceTelemetry(
  timeoutMs = OPEN_TELEMETRY_FLUSH_TIMEOUT_MS,
) {
  const state = getLoggerState();

  if (state.pendingOpenTelemetryTraceFlush) {
    return state.pendingOpenTelemetryTraceFlush;
  }

  if (!state.openTelemetryTracerProvider) {
    return undefined;
  }

  return withTimeout(
    Promise.resolve(state.openTelemetryTracerProvider.forceFlush()),
    timeoutMs,
    `[Logger-Server] OpenTelemetry trace flush timed out after ${timeoutMs}ms.`,
  );
}

function queueOpenTelemetryTraceFlush(reason = "background trace telemetry") {
  const state = getLoggerState();

  if (!state.openTelemetryTracerProvider) {
    return undefined;
  }

  if (state.pendingOpenTelemetryTraceFlush) {
    return state.pendingOpenTelemetryTraceFlush;
  }

  const pendingFlush = Promise.resolve(
    flushOpenTelemetryTraceTelemetry(OPEN_TELEMETRY_FLUSH_TIMEOUT_MS),
  )
    .catch((error) => {
      console.warn(
        `[Logger-Server] OpenTelemetry trace flush did not complete during ${reason}:`,
        error,
      );
    })
    .finally(() => {
      if (state.pendingOpenTelemetryTraceFlush === pendingFlush) {
        state.pendingOpenTelemetryTraceFlush = undefined;
      }
    });

  state.pendingOpenTelemetryTraceFlush = pendingFlush;
  return pendingFlush;
}

async function flushTelemetry() {
  const flushOperations = [
    flushApplicationInsightsTelemetry(),
    flushOpenTelemetryTelemetry(),
    flushOpenTelemetryTraceTelemetry(),
  ];

  const activeFlushOperations = flushOperations.filter(Boolean);

  if (activeFlushOperations.length === 0) {
    return;
  }

  const results = await Promise.allSettled(activeFlushOperations);

  for (const result of results) {
    if (result.status === "rejected") {
      console.error(
        "[Logger-Server] Failed to flush telemetry:",
        result.reason,
      );
    }
  }
}

/**
 * Get the current configuration from environment variables
 * @returns {Object} Configuration object
 */
function getConfig() {
  return {
    logLevel: process.env.APP_LOGGING_LEVEL || LogLevelName.INFORMATION,
    applicationInsightsEnabled: parseBooleanEnv(
      process.env.APP_INSIGHTS_ENABLED,
      false,
    ),
    connectionString: process.env.APP_INSIGHTS_KEY || "",
    openTelemetryEnabled: parseBooleanEnv(
      process.env.APP_OPEN_TELEMETRY_ENABLED,
      false,
    ),
    openTelemetrySeqEnabled: parseBooleanEnv(
      process.env.APP_OPEN_TELEMETRY_SEQ_ENABLED,
      false,
    ),
    openTelemetrySeqEndpoint: process.env.APP_OPEN_TELEMETRY_SEQ_ENDPOINT || "",
    openTelemetrySeqApiKey: process.env.APP_OPEN_TELEMETRY_SEQ_API_KEY || "",
    openTelemetryAspireEnabled: parseBooleanEnv(
      process.env.APP_OPEN_TELEMETRY_ASPIRE_ENABLED,
      false,
    ),
    openTelemetryAspireEndpoint:
      process.env.APP_OPEN_TELEMETRY_ASPIRE_ENDPOINT ||
      process.env.OTEL_EXPORTER_OTLP_ENDPOINT ||
      "",
    openTelemetryServiceName:
      process.env.OTEL_SERVICE_NAME ||
      getOpenTelemetryResourceAttribute(
        process.env.OTEL_RESOURCE_ATTRIBUTES,
        "service.name",
      ) ||
      "",
    appVersion: process.env.APP_VERSION || "0.0.0",
    environment: process.env.NODE_ENV || "development",
    appName: process.env.APP_NAME || "",
    cloudRole: process.env.APP_INSIGHTS_CLOUD_ROLE || "",
  };
}

function getTelemetryContext() {
  const state = getLoggerState();
  const config = getConfig();

  return {
    appName: normalizeText(
      state.appName,
      MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
      resolveApplicationName(config),
    ),
    appVersion: normalizeText(
      state.appVersion,
      MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
      config.appVersion || "0.0.0",
    ),
    cloudRole: normalizeText(
      state.cloudRole,
      MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
      resolveCloudRoleName(config),
    ),
    environment: normalizeText(
      state.environment,
      MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
      config.environment || "development",
    ),
  };
}

function buildStandardTelemetryProperties(
  source = SERVER_TELEMETRY_SOURCE,
  properties: unknown = {},
): TelemetryProperties {
  const normalizedProperties = normalizeProperties(properties);
  const normalizedSource = normalizeText(
    source,
    MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
    SERVER_TELEMETRY_SOURCE,
  );
  const telemetryContext = getTelemetryContext();

  return {
    ...normalizedProperties,
    surface: FRONTEND_SURFACE,
    source: normalizedSource,
    environment: telemetryContext.environment,
    appName: telemetryContext.appName,
    cloudRole: telemetryContext.cloudRole,
    appVersion: telemetryContext.appVersion,
  };
}

function buildClientTelemetryProperties(properties: unknown = {}) {
  const normalizedProperties = normalizeProperties(properties);

  return {
    ...buildStandardTelemetryProperties(
      CLIENT_TELEMETRY_SOURCE,
      normalizedProperties,
    ),
    clientTimestamp: normalizedProperties.timestamp || new Date().toISOString(),
  };
}

async function loadApplicationInsightsModule() {
  const state = getLoggerState();

  if (state.applicationInsightsModule) {
    return state.applicationInsightsModule;
  }

  const importedModule = await import("applicationinsights");
  state.applicationInsightsModule = importedModule.default ?? importedModule;
  return state.applicationInsightsModule;
}

function resolveModuleExports(importedModule) {
  return importedModule?.default ?? importedModule;
}

async function loadOpenTelemetryResourcesModule() {
  const state = getLoggerState();

  if (state.openTelemetryResourcesModule) {
    return state.openTelemetryResourcesModule;
  }

  const resourcesModule = resolveModuleExports(
    await import("@opentelemetry/resources"),
  );
  state.openTelemetryResourcesModule = resourcesModule;
  return resourcesModule;
}

async function loadOpenTelemetryModules() {
  const state = getLoggerState();

  if (
    state.openTelemetrySdkLogsModule &&
    state.openTelemetryExporterModule &&
    state.openTelemetryResourcesModule
  ) {
    return {
      sdkLogsModule: state.openTelemetrySdkLogsModule,
      exporterModule: state.openTelemetryExporterModule,
      resourcesModule: state.openTelemetryResourcesModule,
    };
  }

  const [sdkLogsModule, exporterModule, resourcesModule] = await Promise.all([
    import("@opentelemetry/sdk-logs"),
    import("@opentelemetry/exporter-logs-otlp-proto"),
    loadOpenTelemetryResourcesModule(),
  ]);

  state.openTelemetrySdkLogsModule = resolveModuleExports(sdkLogsModule);
  state.openTelemetryExporterModule = resolveModuleExports(exporterModule);
  state.openTelemetryResourcesModule = resourcesModule;

  return {
    sdkLogsModule: state.openTelemetrySdkLogsModule,
    exporterModule: state.openTelemetryExporterModule,
    resourcesModule,
  };
}

async function loadOpenTelemetryTraceModules() {
  const state = getLoggerState();

  if (
    state.openTelemetryApiModule &&
    state.openTelemetrySdkTraceBaseModule &&
    state.openTelemetryTraceExporterModule &&
    state.openTelemetryResourcesModule
  ) {
    return {
      apiModule: state.openTelemetryApiModule,
      sdkTraceBaseModule: state.openTelemetrySdkTraceBaseModule,
      traceExporterModule: state.openTelemetryTraceExporterModule,
      resourcesModule: state.openTelemetryResourcesModule,
    };
  }

  const [apiModule, sdkTraceBaseModule, traceExporterModule, resourcesModule] =
    await Promise.all([
      import("@opentelemetry/api"),
      import("@opentelemetry/sdk-trace-base"),
      import("@opentelemetry/exporter-trace-otlp-proto"),
      loadOpenTelemetryResourcesModule(),
    ]);

  state.openTelemetryApiModule = resolveModuleExports(apiModule);
  state.openTelemetrySdkTraceBaseModule =
    resolveModuleExports(sdkTraceBaseModule);
  state.openTelemetryTraceExporterModule =
    resolveModuleExports(traceExporterModule);
  state.openTelemetryResourcesModule = resourcesModule;

  return {
    apiModule: state.openTelemetryApiModule,
    sdkTraceBaseModule: state.openTelemetrySdkTraceBaseModule,
    traceExporterModule: state.openTelemetryTraceExporterModule,
    resourcesModule,
  };
}

function hasTelemetrySink() {
  const { telemetryClient, openTelemetryLogger, openTelemetryTracer } =
    getLoggerState();
  return Boolean(telemetryClient || openTelemetryLogger || openTelemetryTracer);
}

async function shutdownOpenTelemetryLoggerProvider() {
  const state = getLoggerState();

  if (state.openTelemetryLoggerProvider) {
    try {
      await state.openTelemetryLoggerProvider.shutdown();
    } catch (error) {
      console.error(
        "[Logger-Server] Failed to shut down OpenTelemetry logger provider:",
        error,
      );
    }
  }

  state.openTelemetryLoggerProvider = undefined;
  state.openTelemetryLogger = undefined;
  state.pendingOpenTelemetryFlush = undefined;
}

async function shutdownOpenTelemetryTracerProvider() {
  const state = getLoggerState();

  if (state.openTelemetryTracerProvider) {
    try {
      await state.openTelemetryTracerProvider.shutdown();
    } catch (error) {
      console.error(
        "[Logger-Server] Failed to shut down OpenTelemetry tracer provider:",
        error,
      );
    }
  }

  state.openTelemetryTracerProvider = undefined;
  state.openTelemetryTracer = undefined;
  state.pendingOpenTelemetryTraceFlush = undefined;
}

async function shutdownOpenTelemetryProviders() {
  await Promise.all([
    shutdownOpenTelemetryLoggerProvider(),
    shutdownOpenTelemetryTracerProvider(),
  ]);
}

function collectOpenTelemetryExporterConfigs(config, signalName) {
  if (!config.openTelemetryEnabled) {
    return [];
  }

  const exporterConfigs = [];

  if (config.openTelemetrySeqEnabled) {
    const normalizedSeqEndpoint = buildOpenTelemetrySignalEndpoint(
      config.openTelemetrySeqEndpoint,
      signalName,
    );

    if (
      hasValidOpenTelemetryEndpoint(normalizedSeqEndpoint, config.environment)
    ) {
      const seqExporterConfig: {
        url: string;
        headers?: Record<string, string>;
      } = {
        url: normalizedSeqEndpoint,
      };
      const seqHeaders = buildOpenTelemetryHeaders(
        config.openTelemetrySeqApiKey,
      );
      if (seqHeaders) {
        seqExporterConfig.headers = seqHeaders;
      }

      exporterConfigs.push(seqExporterConfig);
    } else {
      console.warn(
        buildOpenTelemetryEndpointWarningMessage(
          "Seq",
          normalizedSeqEndpoint,
          config.environment,
        ),
      );
    }
  }

  if (config.openTelemetryAspireEnabled) {
    const normalizedAspireEndpoint = buildOpenTelemetrySignalEndpoint(
      config.openTelemetryAspireEndpoint,
      signalName,
    );

    if (
      hasValidOpenTelemetryEndpoint(
        normalizedAspireEndpoint,
        config.environment,
      )
    ) {
      exporterConfigs.push({
        url: normalizedAspireEndpoint,
      });
    } else {
      console.warn(
        buildOpenTelemetryEndpointWarningMessage(
          "Aspire",
          normalizedAspireEndpoint,
          config.environment,
        ),
      );
    }
  }

  return exporterConfigs;
}

function buildOpenTelemetryResource(config, resourcesModule) {
  const { defaultResource, resourceFromAttributes } = resourcesModule;

  return defaultResource().merge(
    resourceFromAttributes({
      "service.name": resolveOpenTelemetryServiceName(config),
      "deployment.environment": config.environment,
      "deployment.version": config.appVersion,
    }),
  );
}

async function initializeOpenTelemetryLogger(config) {
  const state = getLoggerState();
  const exporterConfigs = collectOpenTelemetryExporterConfigs(config, "logs");

  if (exporterConfigs.length === 0) {
    await shutdownOpenTelemetryLoggerProvider();
    return false;
  }

  try {
    const { sdkLogsModule, exporterModule, resourcesModule } =
      await loadOpenTelemetryModules();
    const { LoggerProvider, BatchLogRecordProcessor } = sdkLogsModule;
    const { OTLPLogExporter } = exporterModule;

    await shutdownOpenTelemetryLoggerProvider();

    const openTelemetryServiceName = resolveOpenTelemetryServiceName(config);

    const provider = new LoggerProvider({
      resource: buildOpenTelemetryResource(config, resourcesModule),
      processors: exporterConfigs.map(
        (exporterConfig) =>
          new BatchLogRecordProcessor(new OTLPLogExporter(exporterConfig)),
      ),
    });

    state.openTelemetryLoggerProvider = provider;
    state.openTelemetryLogger = provider.getLogger(openTelemetryServiceName);
    return true;
  } catch (error) {
    await shutdownOpenTelemetryLoggerProvider();
    console.error(
      "[Logger-Server] Failed to initialize OpenTelemetry exporters:",
      error,
    );
    return false;
  }
}

async function initializeOpenTelemetryTracer(config) {
  const state = getLoggerState();
  const exporterConfigs = collectOpenTelemetryExporterConfigs(config, "traces");

  if (exporterConfigs.length === 0) {
    await shutdownOpenTelemetryTracerProvider();
    return false;
  }

  try {
    const {
      apiModule,
      sdkTraceBaseModule,
      traceExporterModule,
      resourcesModule,
    } = await loadOpenTelemetryTraceModules();
    const { BasicTracerProvider, BatchSpanProcessor } = sdkTraceBaseModule;
    const { OTLPTraceExporter } = traceExporterModule;

    await shutdownOpenTelemetryTracerProvider();

    const provider = new BasicTracerProvider({
      resource: buildOpenTelemetryResource(config, resourcesModule),
      spanProcessors: exporterConfigs.map(
        (exporterConfig) =>
          new BatchSpanProcessor(new OTLPTraceExporter(exporterConfig)),
      ),
    });

    state.openTelemetryApiModule = apiModule;
    state.openTelemetryTracerProvider = provider;
    state.openTelemetryTracer = provider.getTracer(
      OPEN_TELEMETRY_TRACE_SCOPE_NAME,
      config.appVersion,
    );
    return true;
  } catch (error) {
    await shutdownOpenTelemetryTracerProvider();
    console.error(
      "[Logger-Server] Failed to initialize OpenTelemetry trace exporters:",
      error,
    );
    return false;
  }
}

function emitOpenTelemetryRecord(
  eventName: string,
  level: number,
  body: string,
  properties: unknown = {},
  measurements: unknown = {},
  error: LoggerErrorLike | null = null,
) {
  const { openTelemetryLogger } = getLoggerState();
  if (!openTelemetryLogger) {
    return false;
  }

  const normalizedEventName = normalizeText(
    eventName,
    MAX_TELEMETRY_TEXT_LENGTH,
    "",
  );
  const attributes = buildOpenTelemetryAttributes(properties, measurements);

  if (normalizedEventName) {
    attributes.eventName = normalizedEventName;
  }

  if (error instanceof Error) {
    attributes["exception.type"] = normalizeText(
      error.name,
      MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
      "Error",
    );
    attributes["exception.message"] = normalizeText(
      error.message,
      MAX_TELEMETRY_TEXT_LENGTH,
      "Unhandled error",
    );

    if (error.stack) {
      attributes["exception.stacktrace"] = normalizeText(
        error.stack,
        MAX_TELEMETRY_STACK_LENGTH,
        "",
      );
    }
  }

  const logRecord: {
    severityNumber: number;
    severityText: string;
    body: string;
    attributes: OpenTelemetryAttributes;
    timestamp: number;
    observedTimestamp: number;
    eventName?: string;
  } = {
    severityNumber: mapToOpenTelemetrySeverityNumber(level),
    severityText: getLogLevelName(level),
    body: normalizeText(body, MAX_TELEMETRY_TEXT_LENGTH, "[message omitted]"),
    attributes,
    timestamp: Date.now(),
    observedTimestamp: Date.now(),
  };

  if (normalizedEventName) {
    logRecord.eventName = normalizedEventName;
  }

  openTelemetryLogger.emit(logRecord);

  return true;
}

function emitOpenTelemetrySpan(
  spanName,
  level,
  body,
  properties = {},
  measurements = {},
  error = null,
) {
  const { openTelemetryTracer, openTelemetryApiModule } = getLoggerState();
  if (!openTelemetryTracer || !openTelemetryApiModule) {
    return false;
  }

  const normalizedBody = normalizeText(body, MAX_TELEMETRY_TEXT_LENGTH, "");
  const attributes = {
    ...buildOpenTelemetryAttributes(properties, measurements),
    "telemetry.level": getLogLevelName(level),
  };

  if (normalizedBody) {
    attributes["telemetry.message"] = normalizedBody;
  }

  try {
    const span = openTelemetryTracer.startSpan(
      normalizeText(spanName, MAX_TELEMETRY_SPAN_NAME_LENGTH, "Telemetry"),
      {
        attributes,
      },
    );

    try {
      if (normalizedBody) {
        span.addEvent("telemetry.message", {
          "log.severity": getLogLevelName(level),
          "log.message": normalizedBody,
        });
      }

      if (error instanceof Error) {
        span.recordException(error);
        span.setStatus({
          code: openTelemetryApiModule.SpanStatusCode.ERROR,
          message: normalizeText(
            error.message,
            MAX_TELEMETRY_TEXT_LENGTH,
            "Unhandled error",
          ),
        });
      } else if (level >= LogLevel.Error) {
        span.setStatus({
          code: openTelemetryApiModule.SpanStatusCode.ERROR,
          message: normalizeText(
            normalizedBody,
            MAX_TELEMETRY_TEXT_LENGTH,
            "Telemetry error",
          ),
        });
      }
    } finally {
      span.end();
    }

    return true;
  } catch (spanError) {
    console.warn(
      "[Logger-Server] Failed to emit OpenTelemetry span:",
      spanError,
    );
    return false;
  }
}

async function emitStartupTelemetry() {
  const startupProperties = {
    ...buildStandardTelemetryProperties(SERVER_TELEMETRY_SOURCE, {
      timestamp: new Date().toISOString(),
    }),
    category: "Application",
  };

  if (getLoggerState().telemetryClient) {
    getLoggerState().telemetryClient.trackEvent({
      name: APPLICATION_STARTED_EVENT_NAME,
      properties: startupProperties,
    });
  }

  emitOpenTelemetryRecord(
    APPLICATION_STARTED_EVENT_NAME,
    LogLevel.Information,
    APPLICATION_STARTED_EVENT_NAME,
    startupProperties,
  );
  queueOpenTelemetryFlush("startup telemetry");
}

/**
 * Check if a message should be logged based on current log level
 * @param {number} messageLevel - The level of the message to log
 * @returns {boolean} True if the message should be logged
 */
function shouldLog(messageLevel) {
  const { currentLogLevel } = getLoggerState();
  return messageLevel >= currentLogLevel && currentLogLevel !== LogLevel.None;
}

/**
 * Initialize the Application Insights telemetry client.
 * This is called automatically on first log call.
 *
 * @returns {Promise<boolean>} True if initialization succeeded
 */
async function initializeTelemetry() {
  const state = getLoggerState();

  if (state.isInitialized) {
    return hasTelemetrySink();
  }
  if (state.initializationPromise) {
    return state.initializationPromise;
  }

  // The initialization core is defined below so the outer function can guard concurrent callers.
  // eslint-disable-next-line no-use-before-define
  state.initializationPromise = initializeTelemetryCore();

  try {
    return await state.initializationPromise;
  } finally {
    state.initializationPromise = undefined;
  }
}

async function initializeTelemetryCore() {
  const state = getLoggerState();
  const config = getConfig();
  const initializationSignature = buildInitializationSignature(config);
  const telemetryFeaturesEnabled =
    config.applicationInsightsEnabled || config.openTelemetryEnabled;

  state.currentLogLevel = parseLogLevel(config.logLevel);
  state.appVersion = config.appVersion;
  state.environment = config.environment;
  state.appName = resolveApplicationName(config);
  state.cloudRole = resolveCloudRoleName(config);

  if (
    state.initializedSignature &&
    state.initializedSignature !== initializationSignature
  ) {
    state.telemetryClient = undefined;
    state.isInitialized = false;
    state.initializedSignature = "";
    await shutdownOpenTelemetryProviders();
  }

  if (
    state.isInitialized &&
    state.initializedSignature === initializationSignature
  ) {
    return hasTelemetrySink();
  }

  if (state.lastFailedInitializationSignature === initializationSignature) {
    return false;
  }

  if (state.currentLogLevel === LogLevel.None) {
    state.telemetryClient = undefined;
    await shutdownOpenTelemetryProviders();
    state.lastFailedInitializationSignature = initializationSignature;
    return false;
  }

  if (!telemetryFeaturesEnabled) {
    state.telemetryClient = undefined;
    await shutdownOpenTelemetryProviders();
    state.lastFailedInitializationSignature = initializationSignature;
    return false;
  }

  let applicationInsightsInitialized = false;
  let openTelemetryLoggerInitialized = false;
  let openTelemetryTracerInitialized = false;
  const connectionString = config.connectionString.trim();

  if (!config.applicationInsightsEnabled) {
    state.telemetryClient = undefined;
  } else if (!hasValidConnectionString(connectionString)) {
    state.telemetryClient = undefined;
    console.warn(
      "[Logger-Server] Application Insights connection string not configured. Telemetry will be logged to console only.",
    );
  } else {
    try {
      const appInsights = await loadApplicationInsightsModule();

      appInsights
        .setup(connectionString)
        .setAutoCollectRequests(false)
        .setAutoCollectPerformance(false)
        .setAutoCollectExceptions(true)
        .setAutoCollectDependencies(false)
        .setAutoCollectConsole(false)
        .setUseDiskRetryCaching(true)
        .start();

      state.telemetryClient = appInsights.defaultClient;

      if (!state.telemetryClient) {
        console.error(
          "[Logger-Server] Application Insights default client is unavailable after setup.",
        );
      } else {
        state.telemetryClient.context.tags[
          state.telemetryClient.context.keys.applicationVersion
        ] = state.appVersion;
        state.telemetryClient.context.tags[
          state.telemetryClient.context.keys.cloudRole
        ] = state.cloudRole;
        applicationInsightsInitialized = true;
      }
    } catch (error) {
      console.error(
        "[Logger-Server] Failed to initialize Application Insights:",
        error,
      );
      state.telemetryClient = undefined;
    }
  }

  openTelemetryLoggerInitialized = await initializeOpenTelemetryLogger(config);
  openTelemetryTracerInitialized = await initializeOpenTelemetryTracer(config);

  const startupTelemetryEnabled =
    applicationInsightsInitialized || openTelemetryLoggerInitialized;
  const shouldEmitStartupTelemetry =
    !state.hasLoggedStartup &&
    startupTelemetryEnabled &&
    shouldLog(LogLevel.Information);

  if (shouldEmitStartupTelemetry) {
    state.hasLoggedStartup = true;
    await emitStartupTelemetry();
  }

  if (
    applicationInsightsInitialized ||
    openTelemetryLoggerInitialized ||
    openTelemetryTracerInitialized
  ) {
    state.lastFailedInitializationSignature = "";
    state.initializedSignature = initializationSignature;
    state.isInitialized = true;
    return true;
  }

  state.lastFailedInitializationSignature = initializationSignature;
  return false;
}

/**
 * Format the log message with timestamp and level
 * @param {number} level - Log level
 * @param {string} message - Log message
 * @param {string} category - Optional category/source
 * @returns {string} Formatted message
 */
function formatMessage(level, message, category = "") {
  const timestamp = new Date().toISOString();
  const levelName = getLogLevelName(level);
  const telemetryMessage = buildTelemetryMessage(
    message,
    normalizeCategory(category),
  );
  return `[${timestamp}] [${levelName}] ${telemetryMessage}`;
}

/**
 * Internal logging function that sends to the console and configured telemetry sinks
 * @param {number} level - Log level
 * @param {string} message - Log message
 * @param {string} category - Optional category/source
 * @param {Object} properties - Additional properties
 * @param {Error} error - Optional error object for exceptions
 */
async function logInternal(
  level,
  message,
  category = "",
  properties = {},
  error = null,
) {
  // Ensure initialization
  const state = getLoggerState();

  if (!state.isInitialized) {
    await initializeTelemetry();
  }

  // Check if we should log at this level
  if (!shouldLog(level)) {
    return;
  }

  const normalizedCategory = normalizeCategory(category);
  const formattedMessage = formatMessage(level, message, normalizedCategory);
  const { telemetryClient } = getLoggerState();
  const normalizedProperties = normalizeProperties(properties);
  const telemetrySource = normalizeText(
    normalizedProperties.source,
    MAX_TELEMETRY_PROPERTY_VALUE_LENGTH,
    SERVER_TELEMETRY_SOURCE,
  );
  const normalizedError = toLoggerErrorInstance(error, message);
  const traceId = extractTraceId(normalizedError) || extractTraceId(properties);
  const telemetryProperties = {
    ...buildStandardTelemetryProperties(telemetrySource, normalizedProperties),
    ...(traceId ? { traceId } : {}),
    category: normalizedCategory,
    logLevel: getLogLevelName(level),
  };
  let shouldFlushTelemetry = false;

  // Console output based on level
  switch (level) {
    case LogLevel.Trace:
    case LogLevel.Debug:
      console.debug(formattedMessage, telemetryProperties);
      break;
    case LogLevel.Information:
      console.info(formattedMessage, telemetryProperties);
      break;
    case LogLevel.Warning:
      console.warn(formattedMessage, telemetryProperties);
      break;
    case LogLevel.Error:
    case LogLevel.Critical:
      console.error(
        formattedMessage,
        telemetryProperties,
        normalizedError || "",
      );
      break;
    default:
      console.log(formattedMessage, telemetryProperties);
  }

  if (
    emitOpenTelemetryRecord(
      "",
      level,
      buildTelemetryMessage(message, normalizedCategory),
      telemetryProperties,
      {},
      normalizedError,
    )
  ) {
    shouldFlushTelemetry =
      shouldFlushTelemetry ||
      level === LogLevel.Critical ||
      level === LogLevel.Error ||
      normalizedCategory.includes("Application");
  }

  // Send to Application Insights if available
  if (telemetryClient) {
    if (
      normalizedError &&
      (level === LogLevel.Error || level === LogLevel.Critical)
    ) {
      // Track as exception
      telemetryClient.trackException({
        exception: normalizedError,
        severity: mapToAppInsightsSeverity(level),
        properties: telemetryProperties,
      });
    } else {
      // Track as trace
      telemetryClient.trackTrace({
        message: buildTelemetryMessage(message, normalizedCategory),
        severity: mapToAppInsightsSeverity(level),
        properties: telemetryProperties,
      });
    }

    // Flush telemetry to ensure it's sent (especially for important events)
    if (
      level === LogLevel.Critical ||
      level === LogLevel.Error ||
      normalizedCategory.includes("Application")
    ) {
      shouldFlushTelemetry = true;
    }
  }

  if (shouldFlushTelemetry) {
    if (telemetryClient) {
      await flushApplicationInsightsTelemetry();
    }

    queueOpenTelemetryFlush(`${getLogLevelName(level)} log dispatch`);
  }
}

/**
 * Logger class providing ASP.NET Core-style logging methods.
 * Each method corresponds to a log level.
 */
class Logger {
  category: string;

  /**
   * Create a new Logger instance
   * @param {string} category - The category/source name for log messages
   */
  constructor(category = "") {
    this.category = category;
  }

  /**
   * Log a trace-level message (most detailed)
   * @param {string} message - Log message
   * @param {Object} properties - Additional properties
   */
  async trace(message: string, properties: Record<string, unknown> = {}) {
    await logInternal(LogLevel.Trace, message, this.category, properties);
  }

  /**
   * Log a debug-level message
   * @param {string} message - Log message
   * @param {Object} properties - Additional properties
   */
  async debug(message: string, properties: Record<string, unknown> = {}) {
    await logInternal(LogLevel.Debug, message, this.category, properties);
  }

  /**
   * Log an information-level message
   * @param {string} message - Log message
   * @param {Object} properties - Additional properties
   */
  async info(message: string, properties: Record<string, unknown> = {}) {
    await logInternal(LogLevel.Information, message, this.category, properties);
  }

  /**
   * Log an information-level message (alias for info)
   * @param {string} message - Log message
   * @param {Object} properties - Additional properties
   */
  async information(message: string, properties: Record<string, unknown> = {}) {
    await logInternal(LogLevel.Information, message, this.category, properties);
  }

  /**
   * Log a warning-level message
   * @param {string} message - Log message
   * @param {Object} properties - Additional properties
   */
  async warn(message: string, properties: Record<string, unknown> = {}) {
    await logInternal(LogLevel.Warning, message, this.category, properties);
  }

  /**
   * Log a warning-level message (alias for warn)
   * @param {string} message - Log message
   * @param {Object} properties - Additional properties
   */
  async warning(message: string, properties: Record<string, unknown> = {}) {
    await logInternal(LogLevel.Warning, message, this.category, properties);
  }

  /**
   * Log an error-level message
   * @param {string} message - Log message
   * @param {Error|Object} errorOrProperties - Error object or additional properties
   * @param {Object} properties - Additional properties (if first param is Error)
   */
  async error(
    message: string,
    errorOrProperties: LoggerErrorLike | Record<string, unknown> = {},
    properties: Record<string, unknown> = {},
  ) {
    const error = toLoggerErrorInstance(errorOrProperties, message);
    const props = error ? properties : errorOrProperties;

    await logInternal(LogLevel.Error, message, this.category, props, error);
  }

  /**
   * Log a critical-level message
   * @param {string} message - Log message
   * @param {Error|Object} errorOrProperties - Error object or additional properties
   * @param {Object} properties - Additional properties (if first param is Error)
   */
  async critical(
    message: string,
    errorOrProperties: LoggerErrorLike | Record<string, unknown> = {},
    properties: Record<string, unknown> = {},
  ) {
    const error = toLoggerErrorInstance(errorOrProperties, message);
    const props = error ? properties : errorOrProperties;

    await logInternal(LogLevel.Critical, message, this.category, props, error);
  }

  /**
   * Track a custom event
   * @param {string} eventName - Name of the event
   * @param {Object} properties - Event properties
   * @param {Object} measurements - Numeric measurements
   */
  async trackEvent(eventName, properties = {}, measurements = {}) {
    const state = getLoggerState();

    if (!state.isInitialized) {
      await initializeTelemetry();
    }

    const telemetryProperties = {
      ...buildStandardTelemetryProperties(SERVER_TELEMETRY_SOURCE, properties),
      category: this.category,
    };
    const normalizedMeasurements = normalizeMeasurements(measurements);

    if (!shouldLog(LogLevel.Information)) {
      return;
    }

    if (state.telemetryClient) {
      state.telemetryClient.trackEvent({
        name: eventName,
        properties: telemetryProperties,
        measurements: normalizedMeasurements,
      });
    }

    emitOpenTelemetryRecord(
      eventName,
      LogLevel.Information,
      `Event: ${eventName}`,
      telemetryProperties,
      normalizedMeasurements,
    );
  }

  /**
   * Track a metric value
   * @param {string} name - Metric name
   * @param {number} value - Metric value
   * @param {Object} properties - Additional properties
   */
  async trackMetric(name, value, properties = {}) {
    const state = getLoggerState();

    if (!state.isInitialized) {
      await initializeTelemetry();
    }

    const telemetryProperties = {
      ...buildStandardTelemetryProperties(SERVER_TELEMETRY_SOURCE, properties),
      category: this.category,
    };

    if (state.telemetryClient) {
      state.telemetryClient.trackMetric({
        name,
        value,
        properties: telemetryProperties,
      });
    }

    emitOpenTelemetryRecord(
      name,
      LogLevel.Information,
      `Metric: ${name}`,
      telemetryProperties,
      {
        value,
      },
    );
  }

  /**
   * Flush all pending telemetry
   */
  async flush() {
    await flushTelemetry();
  }
}

/**
 * Create a new logger instance with the specified category.
 * This is the primary way to create loggers in application code.
 *
 * @param {string} category - The category/source name (e.g., component name, module name)
 * @returns {Logger} A new Logger instance
 *
 * @example
 * import { createLogger } from '@/utils/logger-server';
 *
 * const log = await createLogger('ExampleService');
 *
 * export async function getStories() {
 *   await log.info('Fetching stories');
 *   // ...
 * }
 */
export async function createLogger(category = "") {
  if (!getLoggerState().isInitialized) {
    await initializeTelemetry();
  }
  return new Logger(category);
}

/**
 * Get a pre-initialized default logger.
 *
 * @returns {Logger} Default logger instance
 */
export async function getLogger() {
  return createLogger("App");
}

/**
 * Server action to log messages from server components.
 *
 * @param {string} level - Log level name
 * @param {string} message - Log message
 * @param {string} category - Optional category
 * @param {Object} properties - Additional properties
 */
export async function serverLog(
  level,
  message,
  category = "",
  properties = {},
) {
  const logLevel = parseLogLevel(level);
  await logInternal(logLevel, message, category, properties);
}

// ============================================================================
// CLIENT LOGGING SERVER ACTIONS
// These server actions are called by the client-side logger (logger-client.js)
// to route all telemetry through the server, keeping secrets secure.
// ============================================================================

/**
 * Server action for client-side logging.
 * Called by logger-client.js to log messages from the browser.
 *
 * @param {string} level - Log level name (trace, debug, info, warn, error, critical)
 * @param {string} message - Log message
 * @param {string} category - Optional category/source
 * @param {Object} properties - Additional properties
 */
export async function clientLog(
  level,
  message,
  category = "",
  properties = {},
) {
  const state = getLoggerState();

  if (!state.isInitialized) {
    await initializeTelemetry();
  }

  const logLevel = parseLogLevel(level);
  if (!shouldLog(logLevel)) {
    return;
  }

  const normalizedProperties = normalizeProperties(properties);
  const normalizedCategory = normalizeText(
    category,
    MAX_TELEMETRY_CATEGORY_LENGTH,
    "",
  );
  const normalizedMessage = normalizeText(
    message,
    MAX_TELEMETRY_TEXT_LENGTH,
    "[client message omitted]",
  );
  const enrichedProperties =
    buildClientTelemetryProperties(normalizedProperties);
  await logInternal(
    logLevel,
    normalizedMessage,
    normalizedCategory,
    enrichedProperties,
  );

  if (
    emitOpenTelemetrySpan(
      buildOpenTelemetrySpanName(
        "ClientLog",
        buildTelemetryMessage(normalizedMessage, normalizedCategory),
      ),
      logLevel,
      buildTelemetryMessage(normalizedMessage, normalizedCategory),
      {
        ...enrichedProperties,
        category: normalizedCategory,
      },
    )
  ) {
    queueOpenTelemetryTraceFlush("client log dispatch");
  }
}

/**
 * Server action for client-side event tracking.
 * Called by logger-client.js to track custom events from the browser.
 *
 * @param {string} eventName - Name of the event
 * @param {Object} properties - Event properties
 * @param {Object} measurements - Numeric measurements
 */
export async function clientTrackEvent(
  eventName,
  properties = {},
  measurements = {},
) {
  const state = getLoggerState();

  if (!state.isInitialized) {
    await initializeTelemetry();
  }

  if (!shouldLog(LogLevel.Information)) {
    return;
  }

  const normalizedProperties = normalizeProperties(properties);
  const normalizedMeasurements = normalizeMeasurements(measurements);
  const normalizedEventName = normalizeText(
    eventName,
    MAX_TELEMETRY_TEXT_LENGTH,
    "ClientEvent",
  );
  const enrichedProperties =
    buildClientTelemetryProperties(normalizedProperties);

  const telemetryProperties = {
    ...enrichedProperties,
  };

  if (state.telemetryClient) {
    state.telemetryClient.trackEvent({
      name: normalizedEventName,
      properties: telemetryProperties,
      measurements: normalizedMeasurements,
    });
  }

  emitOpenTelemetryRecord(
    normalizedEventName,
    LogLevel.Information,
    `ClientEvent: ${normalizedEventName}`,
    telemetryProperties,
    normalizedMeasurements,
  );

  if (
    emitOpenTelemetrySpan(
      buildOpenTelemetrySpanName("ClientEvent", normalizedEventName),
      LogLevel.Information,
      `ClientEvent: ${normalizedEventName}`,
      telemetryProperties,
      normalizedMeasurements,
    )
  ) {
    queueOpenTelemetryTraceFlush("client event dispatch");
  }
}

/**
 * Server action for client-side exception tracking.
 * Called by logger-client.js to track exceptions from the browser.
 *
 * @param {string} errorMessage - Error message
 * @param {string} errorStack - Error stack trace (optional)
 * @param {number} severityLevel - Severity level (0-4)
 * @param {Object} properties - Additional properties
 */
export async function clientTrackException(
  errorMessage,
  errorStack = "",
  severityLevel = 3,
  properties = {},
) {
  const state = getLoggerState();

  if (!state.isInitialized) {
    await initializeTelemetry();
  }

  const normalizedProperties = normalizeProperties(properties);
  const normalizedMessage = normalizeText(
    errorMessage,
    MAX_TELEMETRY_TEXT_LENGTH,
    "Client exception",
  );
  const normalizedStack = normalizeText(
    errorStack,
    MAX_TELEMETRY_STACK_LENGTH,
    "",
  );
  const normalizedSeverityLevel = Number.isFinite(Number(severityLevel))
    ? Math.max(0, Math.min(4, Number(severityLevel)))
    : 3;
  const telemetryLogLevel = mapClientSeverityToLogLevel(
    normalizedSeverityLevel,
  );
  const traceId =
    extractTraceId(normalizedProperties) || extractTraceId(normalizedMessage);
  const enrichedProperties = buildClientTelemetryProperties({
    ...normalizedProperties,
    ...(traceId ? { traceId } : {}),
  });

  const telemetryProperties = {
    ...enrichedProperties,
    severityLevel: normalizedSeverityLevel,
  };

  if (state.telemetryClient) {
    const syntheticError = buildSyntheticClientError(
      normalizedMessage,
      normalizedStack,
    );

    state.telemetryClient.trackException({
      exception: syntheticError,
      severity: normalizedSeverityLevel,
      properties: telemetryProperties,
    });
  }

  const syntheticError = buildSyntheticClientError(
    normalizedMessage,
    normalizedStack,
  );

  emitOpenTelemetryRecord(
    "ClientException",
    telemetryLogLevel,
    normalizedMessage,
    telemetryProperties,
    {},
    syntheticError,
  );

  if (
    emitOpenTelemetrySpan(
      buildOpenTelemetrySpanName("ClientException", normalizedMessage),
      telemetryLogLevel,
      normalizedMessage,
      telemetryProperties,
      {},
      syntheticError,
    )
  ) {
    queueOpenTelemetryTraceFlush("client exception dispatch");
  }
}

/**
 * Server action for client-side page view tracking.
 * Called by logger-client.js to track page views from the browser.
 *
 * @param {string} pageName - Name of the page
 * @param {string} pageUrl - URL of the page
 * @param {Object} properties - Additional properties
 * @param {Object} measurements - Numeric measurements associated with the page view
 */
export async function clientTrackPageView(
  pageName,
  pageUrl = "",
  properties = {},
  measurements = {},
) {
  const state = getLoggerState();

  if (!state.isInitialized) {
    await initializeTelemetry();
  }

  if (!shouldLog(LogLevel.Information)) {
    return;
  }

  const normalizedProperties = normalizeProperties(properties);
  const normalizedMeasurements = normalizeMeasurements(measurements);
  const normalizedPageName = normalizeText(
    pageName,
    MAX_TELEMETRY_TEXT_LENGTH,
    "PageView",
  );
  const sanitizedPageUrl = sanitizePageUrl(pageUrl);
  const enrichedProperties =
    buildClientTelemetryProperties(normalizedProperties);

  const telemetryProperties = {
    ...enrichedProperties,
    uri: sanitizedPageUrl,
  };

  if (state.telemetryClient) {
    state.telemetryClient.trackPageView({
      name: normalizedPageName,
      uri: sanitizedPageUrl,
      properties: telemetryProperties,
      measurements: normalizedMeasurements,
    });
  }

  emitOpenTelemetryRecord(
    normalizedPageName,
    LogLevel.Information,
    `PageView: ${normalizedPageName}`,
    telemetryProperties,
    normalizedMeasurements,
  );

  if (
    emitOpenTelemetrySpan(
      buildOpenTelemetrySpanName("PageView", normalizedPageName),
      LogLevel.Information,
      `PageView: ${normalizedPageName}`,
      telemetryProperties,
      normalizedMeasurements,
    )
  ) {
    queueOpenTelemetryTraceFlush("client page view dispatch");
  }
}

/**
 * Initialize the logger (call early in application lifecycle).
 *
 * @returns {Promise<void>}
 */
export async function initializeLogger() {
  await initializeTelemetry();
}

/**
 * Get current log level
 * @returns {Promise<string>} Current log level name
 */
export async function getCurrentLogLevel() {
  return getLogLevelName(getLoggerState().currentLogLevel);
}

/**
 * Check if a specific log level is enabled
 * @param {string} level - Log level name to check
 * @returns {Promise<boolean>} True if the level is enabled
 */
export async function isLevelEnabled(level) {
  const checkLevel = parseLogLevel(level);
  return shouldLog(checkLevel);
}
