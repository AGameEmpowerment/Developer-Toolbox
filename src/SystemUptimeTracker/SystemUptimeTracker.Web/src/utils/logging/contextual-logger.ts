import { extractTraceId } from "@/utils/error-reference";

type LogProperties = Record<string, unknown>;
type ServerPageLogger = {
  info: (message: string, properties?: LogProperties) => Promise<void>;
  debug: (message: string, properties?: LogProperties) => Promise<void>;
  warn: (message: string, properties?: LogProperties) => Promise<void>;
  error: (
    message: string,
    error: unknown,
    properties?: LogProperties,
  ) => Promise<void>;
  trackEvent: (
    eventName: string,
    properties?: LogProperties,
    measurements?: Record<string, number>,
  ) => Promise<void>;
};

type ServerPageLoggingOptions<T> = {
  category: string;
  context: LogProperties;
  render: (log: ServerPageLogger, context: LogProperties) => Promise<T>;
};

const noopServerPageLogger: ServerPageLogger = {
  info: async () => {},
  debug: async () => {},
  warn: async () => {},
  error: async () => {},
  trackEvent: async () => {},
};

const getErrorDigest = (error: unknown): string | undefined => {
  if (!error || typeof error !== "object" || !("digest" in error)) {
    return undefined;
  }

  const digest = (error as { digest?: unknown }).digest;
  return typeof digest === "string" ? digest : undefined;
};

const isExpectedNextControlFlowError = (error: unknown): boolean => {
  const digest = getErrorDigest(error);

  return (
    digest === "DYNAMIC_SERVER_USAGE" ||
    digest === "NEXT_NOT_FOUND" ||
    digest === "NEXT_HTTP_ERROR_FALLBACK;404" ||
    digest?.startsWith("NEXT_REDIRECT") === true
  );
};

async function getServerPageLogger(
  category: string,
): Promise<ServerPageLogger> {
  if (typeof window !== "undefined") {
    return noopServerPageLogger;
  }

  const { createLogger } = await import("@/utils/logger-server");
  return createLogger(category);
}

function createLazyServerPageLogger(category: string): ServerPageLogger {
  const loggerPromise = getServerPageLogger(category);

  return {
    info: (message, properties) =>
      loggerPromise.then((log) => log.info(message, properties)),
    debug: (message, properties) =>
      loggerPromise.then((log) => log.debug(message, properties)),
    warn: (message, properties) =>
      loggerPromise.then((log) => log.warn(message, properties)),
    error: (message, error, properties) =>
      loggerPromise.then((log) => log.error(message, error, properties)),
    trackEvent: (eventName, properties, measurements) =>
      loggerPromise.then((log) =>
        log.trackEvent(eventName, properties, measurements),
      ),
  };
}

export async function withServerPageLogging<T>({
  category,
  context,
  render,
}: ServerPageLoggingOptions<T>): Promise<T> {
  const log = createLazyServerPageLogger(category);
  const baseContext = {
    area: "frontend-page",
    ...context,
  };

  void log.info("Rendering frontend page", baseContext);

  try {
    const result = await render(log, baseContext);
    void log.trackEvent("FrontendPageRendered", baseContext);
    return result;
  } catch (error) {
    if (isExpectedNextControlFlowError(error)) {
      await log.debug("Frontend page render returned Next.js control flow", {
        ...baseContext,
        digest: getErrorDigest(error),
      });

      throw error;
    }

    const traceId =
      (extractTraceId(error) as string | null | undefined) ?? undefined;

    await log.error("Frontend page render failed", error, {
      ...baseContext,
      ...(traceId ? { traceId } : {}),
    });

    throw error;
  }
}
