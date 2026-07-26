"use client";

import { createClientLogger } from "@/utils/logger-client";

type LogProperties = Record<string, unknown>;

type ClientContextualLogger = {
  trace: (message: string, properties?: LogProperties) => Promise<void>;
  debug: (message: string, properties?: LogProperties) => Promise<void>;
  info: (message: string, properties?: LogProperties) => Promise<void>;
  warn: (message: string, properties?: LogProperties) => Promise<void>;
  error: (
    message: string,
    errorOrProperties?: Error | LogProperties,
    properties?: LogProperties,
  ) => Promise<void>;
  critical: (
    message: string,
    errorOrProperties?: Error | LogProperties,
    properties?: LogProperties,
  ) => Promise<void>;
  trackEvent: (
    eventName: string,
    properties?: LogProperties,
    measurements?: Record<string, number>,
  ) => Promise<void>;
  trackPageView: (
    pageName: string,
    pageUrl?: string,
    properties?: LogProperties,
  ) => Promise<void>;
};

const noopClientContextualLogger: ClientContextualLogger = {
  trace: async () => {},
  debug: async () => {},
  info: async () => {},
  warn: async () => {},
  error: async () => {},
  critical: async () => {},
  trackEvent: async () => {},
  trackPageView: async () => {},
};

const mergeLogProperties = (
  baseProperties: LogProperties,
  additionalProperties: LogProperties = {},
) => ({
  ...baseProperties,
  ...additionalProperties,
});

export function createContextualClientLogger(
  category: string,
  baseProperties: LogProperties = {},
): ClientContextualLogger {
  if (process.env.NODE_ENV === "test") {
    return noopClientContextualLogger;
  }

  const log = createClientLogger(category);
  const context = {
    area: "frontend-module",
    ...baseProperties,
  };

  return {
    trace(message, properties = {}) {
      return log.trace(message, mergeLogProperties(context, properties));
    },
    debug(message, properties = {}) {
      return log.debug(message, mergeLogProperties(context, properties));
    },
    info(message, properties = {}) {
      return log.info(message, mergeLogProperties(context, properties));
    },
    warn(message, properties = {}) {
      return log.warn(message, mergeLogProperties(context, properties));
    },
    error(message, errorOrProperties = {}, properties = {}) {
      if (errorOrProperties instanceof Error) {
        return log.error(
          message,
          errorOrProperties,
          mergeLogProperties(context, properties),
        );
      }

      return log.error(message, mergeLogProperties(context, errorOrProperties));
    },
    critical(message, errorOrProperties = {}, properties = {}) {
      if (errorOrProperties instanceof Error) {
        return log.critical(
          message,
          errorOrProperties,
          mergeLogProperties(context, properties),
        );
      }

      return log.critical(
        message,
        mergeLogProperties(context, errorOrProperties),
      );
    },
    trackEvent(eventName, properties = {}, measurements = {}) {
      return log.trackEvent(
        eventName,
        mergeLogProperties(context, properties),
        measurements,
      );
    },
    trackPageView(pageName, pageUrl = "", properties = {}) {
      return log.trackPageView(
        pageName,
        pageUrl,
        mergeLogProperties(context, properties),
      );
    },
  };
}
