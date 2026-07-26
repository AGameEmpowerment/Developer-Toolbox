"use client";

/**
 * Client-side logging utility that routes all telemetry through the server.
 *
 * SECURITY: This module does NOT directly communicate with Azure Application Insights.
 * All logging is routed through a server route handler, ensuring that telemetry
 * secrets (connection strings, instrumentation keys) are never exposed to the
 * browser.
 *
 * Architecture:
 * - Client components call functions in this module
 * - This module posts telemetry requests to /api/client-telemetry
 * - The route handler delegates to logger-server.js
 * - logger-server.js sends telemetry to Azure Application Insights
 *
 * LogLevel values mirror the `APP_LOGGING_LEVEL` options defined in `.env.example`
 * (Trace, Debug, Information, Warning, Error, Critical, None) so usage is consistent
 * between client and server modules and matches the documented configuration surface.
 *
 * @module utils/logger-client
 */

import { extractTraceId } from "@/utils/error-reference";

let isDevelopment = false;
let clientEnvironment = "development";
let clientAppVersion = "0.0.0";

const FRONTEND_SURFACE = "frontend";
const CLIENT_TELEMETRY_SOURCE = "client";
const MAX_TELEMETRY_ERROR_TEXT_LENGTH = 256;

type LoggerProperties = Record<string, unknown> & {
  url?: string;
  category?: string;
  timestamp?: string;
  userAgent?: string;
  surface?: string;
  source?: string;
  environment?: string;
  appVersion?: string;
};

type LoggerErrorLike = {
  message?: string;
  stack?: string;
  [key: string]: unknown;
};

async function readErrorResponseText(response: Response) {
  try {
    const responseText = await response.text();
    return responseText.trim().slice(0, MAX_TELEMETRY_ERROR_TEXT_LENGTH);
  } catch {
    return "";
  }
}

async function invokeServerAction(actionName: string, ...args: unknown[]) {
  let response: Response;

  try {
    response = await fetch("/api/client-telemetry", {
      method: "POST",
      headers: {
        "content-type": "application/json",
      },
      body: JSON.stringify({
        actionName,
        args,
      }),
      keepalive: true,
    });
  } catch (error) {
    const transportMessage =
      error instanceof Error ? error.message : String(error);
    throw new Error(
      `Telemetry request for ${actionName} failed before reaching the server: ${transportMessage}`,
    );
  }

  if (!response.ok) {
    const responseText = await readErrorResponseText(response);
    const responseSuffix = responseText ? `: ${responseText}` : "";
    throw new Error(
      `Telemetry request for ${actionName} failed with status ${response.status}${responseSuffix}`,
    );
  }
}

/**
 * Initialize the logger with environment configuration.
 * This must be called once during application startup, typically from LoggerClientWrapper.
 *
 * @param {string} environment - The environment string (e.g., "development", "production")
 * @param {string} appVersion - Safe application version string exposed by the server
 */
export function initializeLogger(environment: string, appVersion = "0.0.0") {
  clientEnvironment = environment || "development";
  clientAppVersion = appVersion || "0.0.0";
  isDevelopment = clientEnvironment === "development";
}

/**
 * Log levels for client-side logging.
 * Values align with `.env.example` (Trace, Debug, Information, Warning, Error, Critical, None)
 * so callers can use the same strings on both client and server. Legacy keys remain for
 * backwards compatibility, but all values normalize to the canonical casing.
 * @readonly
 */
export const LogLevel = Object.freeze({
  TRACE: "Trace",
  DEBUG: "Debug",
  INFO: "Information",
  INFORMATION: "Information",
  WARN: "Warning",
  WARNING: "Warning",
  ERROR: "Error",
  CRITICAL: "Critical",
  NONE: "None",
});

function getBrowserLocation() {
  return globalThis.window?.location ?? null;
}

function getUserAgent() {
  return globalThis.navigator?.userAgent ?? "unknown";
}

function getTelemetryUrl(url = "") {
  const rawUrl =
    typeof url === "string" && url.trim().length > 0
      ? url.trim()
      : (getBrowserLocation()?.href ?? "");

  if (!rawUrl) {
    return "";
  }

  try {
    const baseOrigin = getBrowserLocation()?.origin;
    const parsedUrl = baseOrigin
      ? new URL(rawUrl, baseOrigin)
      : new URL(rawUrl);
    return `${parsedUrl.origin}${parsedUrl.pathname}`;
  } catch {
    return rawUrl.split(/[?#]/, 1)[0];
  }
}

function toLoggerError(
  errorLike: LoggerErrorLike | Error | null | undefined,
  fallbackMessage = "Client exception",
) {
  if (errorLike instanceof Error) {
    return errorLike;
  }

  if (!errorLike || typeof errorLike !== "object") {
    return null;
  }

  const message =
    typeof errorLike.message === "string" && errorLike.message.trim().length > 0
      ? errorLike.message.trim()
      : "";
  const stack =
    typeof errorLike.stack === "string" && errorLike.stack.trim().length > 0
      ? errorLike.stack.trim()
      : "";
  const name =
    typeof errorLike.name === "string" && errorLike.name.trim().length > 0
      ? errorLike.name.trim()
      : "";

  if (!message && !stack) {
    return null;
  }

  const normalizedError = new Error(message || fallbackMessage);

  if (stack) {
    normalizedError.stack = stack;
  }

  if (name) {
    normalizedError.name = name;
  }

  const traceId = extractTraceId(errorLike);
  if (traceId) {
    (normalizedError as Error & { traceId?: string }).traceId = traceId;
  }

  return normalizedError;
}

function getEnrichedProperties(
  properties: unknown = {},
  category = "",
  includeUrl = false,
): LoggerProperties {
  const baseProperties: LoggerProperties =
    properties && typeof properties === "object" && !Array.isArray(properties)
      ? ({ ...properties } as LoggerProperties)
      : {};

  if (Object.hasOwn(baseProperties, "url")) {
    baseProperties.url = getTelemetryUrl(baseProperties.url) || "unknown";
  }

  const enrichedProperties = {
    ...baseProperties,
    surface: FRONTEND_SURFACE,
    source: CLIENT_TELEMETRY_SOURCE,
    environment: clientEnvironment,
    appVersion: clientAppVersion,
    timestamp: new Date().toISOString(),
    userAgent: getUserAgent(),
  };

  if (category) {
    enrichedProperties.category = category;
  }

  if (includeUrl && !enrichedProperties.url) {
    enrichedProperties.url = getTelemetryUrl() || "unknown";
  }

  return enrichedProperties;
}

function normalizeLevel(level) {
  const raw = (level || "").toString().trim();
  if (LogLevel[raw]) {
    return LogLevel[raw];
  }

  switch (raw.toLowerCase()) {
    case "trace":
      return LogLevel.TRACE;
    case "debug":
      return LogLevel.DEBUG;
    case "information":
    case "info":
      return LogLevel.INFORMATION;
    case "warning":
    case "warn":
      return LogLevel.WARNING;
    case "error":
      return LogLevel.ERROR;
    case "critical":
    case "fatal":
      return LogLevel.CRITICAL;
    case "none":
    case "off":
      return LogLevel.NONE;
    default:
      return LogLevel.INFORMATION;
  }
}

/**
 * Client-side logger class that routes logging to the server.
 */
class ClientLogger {
  category: string;

  /**
   * Create a new ClientLogger instance
   * @param {string} category - The category/source name for log messages
   */
  constructor(category = "") {
    this.category = category;
  }

  /**
   * Log a trace-level message
   * @param {string} message - Log message
   * @param {Object} properties - Additional properties
   */
  async trace(message: string, properties: LoggerProperties = {}) {
    await this._log(LogLevel.TRACE, message, properties);
  }

  /**
   * Log a debug-level message
   * @param {string} message - Log message
   * @param {Object} properties - Additional properties
   */
  async debug(message: string, properties: LoggerProperties = {}) {
    await this._log(LogLevel.DEBUG, message, properties);
  }

  /**
   * Log an information-level message
   * @param {string} message - Log message
   * @param {Object} properties - Additional properties
   */
  async info(message: string, properties: LoggerProperties = {}) {
    await this._log(LogLevel.INFO, message, properties);
  }

  /**
   * Log an information-level message (alias for info)
   * @param {string} message - Log message
   * @param {Object} properties - Additional properties
   */
  async information(message: string, properties: LoggerProperties = {}) {
    await this._log(LogLevel.INFO, message, properties);
  }

  /**
   * Log a warning-level message
   * @param {string} message - Log message
   * @param {Object} properties - Additional properties
   */
  async warn(message: string, properties: LoggerProperties = {}) {
    await this._log(LogLevel.WARN, message, properties);
  }

  /**
   * Log a warning-level message (alias for warn)
   * @param {string} message - Log message
   * @param {Object} properties - Additional properties
   */
  async warning(message: string, properties: LoggerProperties = {}) {
    await this._log(LogLevel.WARN, message, properties);
  }

  /**
   * Log an error-level message
   * @param {string} message - Log message
   * @param {Error|Object} errorOrProperties - Error object or additional properties
   * @param {Object} properties - Additional properties (if first param is Error)
   */
  async error(
    message: string,
    errorOrProperties: LoggerErrorLike | Error | LoggerProperties = {},
    properties: LoggerProperties = {},
  ) {
    const normalizedError = toLoggerError(errorOrProperties, message);
    const resolvedProperties = normalizedError
      ? properties
      : (errorOrProperties as LoggerProperties);

    if (normalizedError) {
      await this._logError(message, normalizedError, properties);
    } else {
      await this._log(LogLevel.ERROR, message, resolvedProperties);
    }
  }

  /**
   * Log a critical-level message
   * @param {string} message - Log message
   * @param {Error|Object} errorOrProperties - Error object or additional properties
   * @param {Object} properties - Additional properties (if first param is Error)
   */
  async critical(
    message: string,
    errorOrProperties: LoggerErrorLike | Error | LoggerProperties = {},
    properties: LoggerProperties = {},
  ) {
    const normalizedError = toLoggerError(errorOrProperties, message);
    const resolvedProperties = normalizedError
      ? properties
      : (errorOrProperties as LoggerProperties);

    if (normalizedError) {
      await this._logError(message, normalizedError, properties, 4);
    } else {
      await this._log(LogLevel.CRITICAL, message, resolvedProperties);
    }
  }

  /**
   * Track a custom event
   * @param {string} eventName - Name of the event
   * @param {Object} properties - Event properties
   * @param {Object} measurements - Numeric measurements
   */
  async trackEvent(
    eventName: string,
    properties: LoggerProperties = {},
    measurements: Record<string, number> = {},
  ) {
    try {
      const enrichedProperties = getEnrichedProperties(
        properties,
        this.category,
      );

      if (isDevelopment) {
        console.log(`[Client Logger] Event: ${eventName}`, enrichedProperties);
      }

      await invokeServerAction(
        "clientTrackEvent",
        eventName,
        enrichedProperties,
        measurements,
      );
    } catch (error) {
      console.error("[Client Logger] Failed to track event:", error);
    }
  }

  /**
   * Track a page view
   * @param {string} pageName - Name of the page
   * @param {string} pageUrl - URL of the page (defaults to current origin + pathname)
   * @param {Object} properties - Additional properties
   */
  async trackPageView(
    pageName: string,
    pageUrl = "",
    properties: LoggerProperties = {},
  ) {
    try {
      const url = getTelemetryUrl(pageUrl);
      const enrichedProperties = getEnrichedProperties(
        properties,
        this.category,
      );

      if (isDevelopment) {
        console.log(`[Client Logger] PageView: ${pageName}`, url);
      }

      await invokeServerAction(
        "clientTrackPageView",
        pageName,
        url,
        enrichedProperties,
      );
    } catch (error) {
      console.error("[Client Logger] Failed to track page view:", error);
    }
  }

  /**
   * Internal method to log messages
   * @private
   */
  async _log(
    level: unknown,
    message: string,
    properties: LoggerProperties = {},
  ) {
    try {
      const normalizedLevel = normalizeLevel(level);

      if (normalizedLevel === LogLevel.NONE) {
        return;
      }

      const enrichedProperties = getEnrichedProperties(properties, "", true);

      if (isDevelopment) {
        console.log(
          `[Client Logger] [${normalizedLevel}] ${message}`,
          enrichedProperties,
        );
      }

      await invokeServerAction(
        "clientLog",
        normalizedLevel,
        message,
        this.category,
        enrichedProperties,
      );
    } catch (error) {
      console.error("[Client Logger] Failed to log message:", error);
    }
  }

  /**
   * Internal method to log errors with stack traces
   * @private
   */
  async _logError(
    message: string,
    error: LoggerErrorLike | Error,
    properties: LoggerProperties = {},
    severityLevel = 3,
  ) {
    try {
      const normalizedError =
        toLoggerError(error, message) ?? new Error(message);
      const traceId = extractTraceId(normalizedError);
      const enrichedProperties = getEnrichedProperties(
        {
          ...properties,
          ...(traceId ? { traceId } : {}),
        },
        this.category,
        true,
      );

      if (isDevelopment) {
        console.error(
          `[Client Logger] ${message}`,
          normalizedError,
          enrichedProperties,
        );
      }

      await invokeServerAction(
        "clientTrackException",
        `${message}: ${normalizedError.message || String(normalizedError)}`,
        normalizedError.stack || "",
        severityLevel,
        enrichedProperties,
      );
    } catch (trackError) {
      console.error("[Client Logger] Failed to log error:", trackError);
    }
  }
}

/**
 * Create a new client logger instance with the specified category.
 *
 * @param {string} category - The category/source name
 * @returns {ClientLogger} A new ClientLogger instance
 *
 * @example
 * import { createClientLogger } from '@/utils/logger-client';
 *
 * const log = createClientLogger('DashboardPage');
 *
 * // Log messages
 * await log.info('Page loaded');
 *
 * // Track events
 * await log.trackEvent('ButtonClicked', { buttonName: 'Submit' });
 */
export function createClientLogger(category = "") {
  return new ClientLogger(category);
}

// ============================================================================
// STANDALONE FUNCTIONS
// Convenience functions for one-off logging without creating a logger instance
// ============================================================================

/**
 * Log an information message from the client
 * @param {string} message - Log message
 * @param {Object} properties - Additional properties
 */
export async function logInfo(
  message: string,
  properties: LoggerProperties = {},
) {
  const logger = createClientLogger();
  await logger.info(message, properties);
}

/**
 * Log a warning message from the client
 * @param {string} message - Log message
 * @param {Object} properties - Additional properties
 */
export async function logWarn(
  message: string,
  properties: LoggerProperties = {},
) {
  const logger = createClientLogger();
  await logger.warn(message, properties);
}

/**
 * Log an error message from the client
 * @param {string} message - Log message
 * @param {Error|Object} errorOrProperties - Error object or properties
 * @param {Object} properties - Additional properties
 */
export async function logError(
  message: string,
  errorOrProperties: LoggerErrorLike | Error | LoggerProperties = {},
  properties: LoggerProperties = {},
) {
  const logger = createClientLogger();
  await logger.error(message, errorOrProperties, properties);
}

/**
 * Track a custom event from the client
 * @param {string} eventName - Event name
 * @param {Object} properties - Event properties
 * @param {Object} measurements - Numeric measurements
 */
export async function trackEvent(
  eventName: string,
  properties: LoggerProperties = {},
  measurements = {},
) {
  const logger = createClientLogger();
  await logger.trackEvent(eventName, properties, measurements);
}

/**
 * Track a page view from the client
 * @param {string} pageName - Page name
 * @param {string} pageUrl - Page URL
 * @param {Object} properties - Additional properties
 */
export async function trackPageView(
  pageName: string,
  pageUrl = "",
  properties: LoggerProperties = {},
) {
  const logger = createClientLogger();
  await logger.trackPageView(pageName, pageUrl, properties);
}

/**
 * Track an exception from the client
 * @param {Error} error - The error to track
 * @param {number} severityLevel - Severity level (0=Verbose, 1=Information, 2=Warning, 3=Error, 4=Critical)
 * @param {Object} properties - Additional properties
 */
export async function trackException(
  error: LoggerErrorLike | Error,
  severityLevel = 3,
  properties: LoggerProperties = {},
) {
  try {
    const normalizedError =
      toLoggerError(error) ?? new Error("Unhandled client exception");
    const traceId = extractTraceId(normalizedError);
    const enrichedProperties = getEnrichedProperties(
      {
        ...properties,
        ...(traceId ? { traceId } : {}),
      },
      "",
      true,
    );

    if (isDevelopment) {
      console.error(
        "[Client Logger] Exception:",
        normalizedError,
        enrichedProperties,
      );
    }

    await invokeServerAction(
      "clientTrackException",
      normalizedError.message || String(normalizedError),
      normalizedError.stack || "",
      severityLevel,
      enrichedProperties,
    );
  } catch (trackError) {
    console.error("[Client Logger] Failed to track exception:", trackError);
  }
}

export default ClientLogger;
