import { beforeEach, describe, expect, it, vi } from "vitest";

const serverApiGet = vi.fn();
const redirectToLogin = vi.fn();
const createTraceId = vi.fn(() => "generatedtraceid0000000000000000");
const createTraceableError = vi.fn((message, traceId, properties = {}) => {
  const error = new Error(
    traceId ? `${message} Trace ID: ${traceId}.` : message,
  );
  error.traceId = traceId;
  Object.assign(error, properties);
  return error;
});
const extractTraceId = vi.fn((source) => {
  if (!source) {
    return undefined;
  }

  if (typeof source.get === "function") {
    return source.get("x-trace-id") || undefined;
  }

  return source.traceId;
});
const mockedLogger = {
  warn: vi.fn().mockResolvedValue(undefined),
  error: vi.fn().mockResolvedValue(undefined),
};
const createLogger = vi.fn().mockResolvedValue(mockedLogger);

vi.mock("@/utils/server-api", () => ({
  serverApiGet,
}));

vi.mock("../utils/auth/redirect-to-login", () => ({
  default: redirectToLogin,
}));

vi.mock("@/utils/error-reference", () => ({
  createTraceId,
  createTraceableError,
  extractTraceId,
}));

vi.mock("@/utils/logger-server", () => ({
  createLogger,
}));

describe("serverApiGetAsJson", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("reuses the upstream trace id when json parsing fails", async () => {
    const traceId = "fedcba9876543210fedcba9876543210";
    serverApiGet.mockResolvedValue({
      headers: {
        get: vi.fn((name) => (name === "x-trace-id" ? traceId : undefined)),
      },
      json: vi.fn().mockRejectedValue(new Error("Unexpected token < in JSON")),
    });

    const { serverApiGetAsJson } = await import("./server-api-get");

    await expect(
      serverApiGetAsJson({
        url: "/_health",
      }),
    ).rejects.toMatchObject({
      traceId,
      message: `The server returned an unreadable response. Trace ID: ${traceId}.`,
    });
    expect(createTraceId).not.toHaveBeenCalled();
    expect(mockedLogger.error).toHaveBeenCalledWith(
      "Server API JSON response parsing failed",
      expect.any(Error),
      expect.objectContaining({
        traceId,
      }),
    );
  });

  it("reuses the upstream trace id when schema validation fails", async () => {
    const traceId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    serverApiGet.mockResolvedValue({
      headers: {
        get: vi.fn((name) => (name === "x-trace-id" ? traceId : undefined)),
      },
      json: vi.fn().mockResolvedValue({
        invalid: true,
      }),
    });

    const { serverApiGetAsJson } = await import("./server-api-get");

    await expect(
      serverApiGetAsJson({
        url: "/_health",
        zodSchema: {
          safeParse: vi.fn(() => ({
            success: false,
          })),
        },
      }),
    ).rejects.toMatchObject({
      traceId,
      message: `The server returned an unexpected response. Trace ID: ${traceId}.`,
    });
    expect(createTraceId).not.toHaveBeenCalled();
    expect(mockedLogger.error).toHaveBeenCalledWith(
      "Server API JSON schema validation failed",
      expect.objectContaining({
        traceId,
      }),
    );
  });
});
