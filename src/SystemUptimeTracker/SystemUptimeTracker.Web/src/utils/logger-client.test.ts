import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

let loggerClientModule;
let fetchMock;
let logSpy;
let errorSpy;

function stubBrowserGlobals(
  href = "https://example.com/stories?token=secret#section",
) {
  vi.stubGlobal("navigator", {
    userAgent: "VitestAgent/1.0",
  });
  vi.stubGlobal("window", {
    location: new URL(href),
  });
}

function getTelemetryPayload(callIndex = 0) {
  return JSON.parse(fetchMock.mock.calls[callIndex][1].body);
}

describe("logger-client", () => {
  beforeEach(async () => {
    vi.resetModules();
    vi.clearAllMocks();
    vi.unstubAllGlobals();

    fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
    });
    vi.stubGlobal("fetch", fetchMock);

    loggerClientModule = await import("./logger-client");
    loggerClientModule.initializeLogger("test", "2.3.4");

    logSpy = vi.spyOn(console, "log").mockImplementation(() => {});
    errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    logSpy?.mockRestore();
    errorSpy?.mockRestore();
    vi.unstubAllGlobals();
  });

  it("should have all log levels defined", () => {
    expect(loggerClientModule.LogLevel.TRACE).toBe("Trace");
    expect(loggerClientModule.LogLevel.DEBUG).toBe("Debug");
    expect(loggerClientModule.LogLevel.INFO).toBe("Information");
    expect(loggerClientModule.LogLevel.INFORMATION).toBe("Information");
    expect(loggerClientModule.LogLevel.WARN).toBe("Warning");
    expect(loggerClientModule.LogLevel.WARNING).toBe("Warning");
    expect(loggerClientModule.LogLevel.ERROR).toBe("Error");
    expect(loggerClientModule.LogLevel.CRITICAL).toBe("Critical");
    expect(loggerClientModule.LogLevel.NONE).toBe("None");
    expect(Object.isFrozen(loggerClientModule.LogLevel)).toBe(true);
  });

  it("should create a ClientLogger instance with the expected methods", () => {
    const logger = loggerClientModule.createClientLogger("TestCategory");

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
    expect(typeof logger.trackPageView).toBe("function");
  });

  it("should route info logs to clientLog with normalized browser metadata", async () => {
    stubBrowserGlobals();
    const logger = loggerClientModule.createClientLogger("TestInfo");

    await logger.info("Info message", {
      requestId: "req-123",
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/client-telemetry",
      expect.objectContaining({
        method: "POST",
        headers: {
          "content-type": "application/json",
        },
        keepalive: true,
      }),
    );
    const payload = getTelemetryPayload();
    expect(payload.actionName).toBe("clientLog");
    expect(payload.args).toEqual([
      "Information",
      "Info message",
      "TestInfo",
      expect.objectContaining({
        requestId: "req-123",
        surface: "frontend",
        source: "client",
        environment: "test",
        appVersion: "2.3.4",
        userAgent: "VitestAgent/1.0",
        url: "https://example.com/stories",
      }),
    ]);
    expect(payload.args[3].timestamp).toEqual(expect.any(String));
  });

  it("should not send telemetry when _log is called with LogLevel.NONE", async () => {
    const logger = loggerClientModule.createClientLogger("TestNone");

    await logger._log(loggerClientModule.LogLevel.NONE, "Skip this message");

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("should track page views using only origin and pathname", async () => {
    stubBrowserGlobals();
    const logger = loggerClientModule.createClientLogger("TestPageView");

    await logger.trackPageView("Stories");

    const payload = getTelemetryPayload();
    expect(payload.actionName).toBe("clientTrackPageView");
    expect(payload.args).toEqual([
      "Stories",
      "https://example.com/stories",
      expect.objectContaining({
        category: "TestPageView",
        surface: "frontend",
        source: "client",
        environment: "test",
        appVersion: "2.3.4",
        timestamp: expect.any(String),
      }),
    ]);
  });

  it("should strip query strings and fragments from explicit page URLs", async () => {
    const logger = loggerClientModule.createClientLogger("TestPageView");

    await logger.trackPageView(
      "Stories",
      "https://example.com/stories?token=secret#section",
    );

    const payload = getTelemetryPayload();
    expect(payload.actionName).toBe("clientTrackPageView");
    expect(payload.args).toEqual([
      "Stories",
      "https://example.com/stories",
      expect.any(Object),
    ]);
  });

  it("should fall back to unknown browser metadata outside the browser", async () => {
    vi.stubGlobal("navigator", undefined);
    vi.stubGlobal("window", undefined);
    const logger = loggerClientModule.createClientLogger("NoBrowser");

    await logger.info("Server render");

    const payload = getTelemetryPayload();
    expect(payload.actionName).toBe("clientLog");
    expect(payload.args).toEqual([
      "Information",
      "Server render",
      "NoBrowser",
      expect.objectContaining({
        surface: "frontend",
        source: "client",
        environment: "test",
        appVersion: "2.3.4",
        userAgent: "unknown",
        url: "unknown",
      }),
    ]);
  });

  it("should route critical errors through clientTrackException", async () => {
    stubBrowserGlobals();
    const logger = loggerClientModule.createClientLogger("CriticalLogger");
    const error = new Error("boom");

    await logger.critical("System failure", error, {
      requestId: "req-456",
    });

    const payload = getTelemetryPayload();
    expect(payload.actionName).toBe("clientTrackException");
    expect(payload.args).toEqual([
      "System failure: boom",
      expect.any(String),
      4,
      expect.objectContaining({
        category: "CriticalLogger",
        requestId: "req-456",
        surface: "frontend",
        source: "client",
        environment: "test",
        appVersion: "2.3.4",
        url: "https://example.com/stories",
        userAgent: "VitestAgent/1.0",
      }),
    ]);
  });

  it("should forward trace ids attached to error objects", async () => {
    stubBrowserGlobals();
    const error = new Error("boom");
    error.traceId = "0123456789abcdef0123456789abcdef";

    await loggerClientModule.trackException(error);

    const payload = getTelemetryPayload();
    expect(payload.actionName).toBe("clientTrackException");
    expect(payload.args).toEqual([
      "boom",
      expect.any(String),
      3,
      expect.objectContaining({
        traceId: "0123456789abcdef0123456789abcdef",
        surface: "frontend",
        source: "client",
        environment: "test",
        appVersion: "2.3.4",
        url: "https://example.com/stories",
      }),
    ]);
  });

  it("should handle telemetry endpoint failures without throwing", async () => {
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 500,
      text: vi.fn().mockResolvedValue("Telemetry dispatch failed."),
    });
    const logger = loggerClientModule.createClientLogger("FailingLogger");

    await expect(logger.info("Retry later")).resolves.toBeUndefined();

    expect(console.error).toHaveBeenCalledWith(
      "[Client Logger] Failed to log message:",
      expect.any(Error),
    );
    expect(errorSpy.mock.calls[0][1].message).toContain(
      "Telemetry request for clientLog failed with status 500",
    );
  });

  it("should route error-like objects without stack traces through clientTrackException", async () => {
    stubBrowserGlobals();
    const logger = loggerClientModule.createClientLogger("ErrorLikeLogger");

    await logger.error(
      "Submission failed",
      { message: "boom" },
      {
        requestId: "req-789",
      },
    );

    const payload = getTelemetryPayload();
    expect(payload.actionName).toBe("clientTrackException");
    expect(payload.args).toEqual([
      "Submission failed: boom",
      expect.any(String),
      3,
      expect.objectContaining({
        category: "ErrorLikeLogger",
        requestId: "req-789",
        surface: "frontend",
        source: "client",
        environment: "test",
        appVersion: "2.3.4",
        url: "https://example.com/stories",
      }),
    ]);
  });

  it("should treat properties-only error calls as regular error logs", async () => {
    stubBrowserGlobals();
    const logger = loggerClientModule.createClientLogger("PropertyLogger");

    await logger.error("Submission failed", {
      requestId: "req-901",
      validationState: "missing-title",
    });

    const payload = getTelemetryPayload();
    expect(payload.actionName).toBe("clientLog");
    expect(payload.args).toEqual([
      "Error",
      "Submission failed",
      "PropertyLogger",
      expect.objectContaining({
        requestId: "req-901",
        validationState: "missing-title",
        surface: "frontend",
        source: "client",
        environment: "test",
        appVersion: "2.3.4",
        url: "https://example.com/stories",
      }),
    ]);
  });

  it("should expose standalone helpers that reuse the logger implementation", async () => {
    stubBrowserGlobals();

    await loggerClientModule.trackEvent(
      "ButtonClicked",
      {
        buttonName: "Submit",
      },
      {
        durationMs: 25,
      },
    );
    await loggerClientModule.trackException(new Error("Unhandled error"));

    const eventPayload = getTelemetryPayload(0);
    expect(eventPayload.actionName).toBe("clientTrackEvent");
    expect(eventPayload.args).toEqual([
      "ButtonClicked",
      expect.objectContaining({
        buttonName: "Submit",
        surface: "frontend",
        source: "client",
        environment: "test",
        appVersion: "2.3.4",
        userAgent: "VitestAgent/1.0",
      }),
      {
        durationMs: 25,
      },
    ]);
    const exceptionPayload = getTelemetryPayload(1);
    expect(exceptionPayload.actionName).toBe("clientTrackException");
    expect(exceptionPayload.args).toEqual([
      "Unhandled error",
      expect.any(String),
      3,
      expect.objectContaining({
        surface: "frontend",
        source: "client",
        environment: "test",
        appVersion: "2.3.4",
        url: "https://example.com/stories",
        userAgent: "VitestAgent/1.0",
      }),
    ]);
  });
});
