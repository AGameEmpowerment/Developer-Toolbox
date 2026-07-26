import { beforeEach, describe, expect, it, vi } from "vitest";

const cookies = vi.fn();
const getAccessToken = vi.fn();
const auth = vi.fn(() => ({
  getAccessToken,
}));
const decrypt = vi.fn();
const getZodDefinitionForRequest = vi.fn();
const createLogger = vi.fn();

const mockedLogger = {
  warn: vi.fn().mockResolvedValue(undefined),
  error: vi.fn().mockResolvedValue(undefined),
};

vi.mock("next/headers", () => ({
  cookies,
}));

vi.mock("@/utils/auth/auth", () => ({
  auth,
}));

vi.mock("@/utils/encryption", () => ({
  decrypt,
}));

vi.mock("@/utils/logger-server", () => ({
  createLogger,
}));

vi.mock("../get-zod-definition-for-request", () => ({
  default: getZodDefinitionForRequest,
}));

describe("catch-all api route", () => {
  beforeEach(() => {
    vi.resetModules();
    vi.clearAllMocks();
    global.fetch = vi.fn() as unknown as typeof fetch;
    process.env.API_BASE_URL = "https://example.test/";
    process.env.IMPERSONATING_COOKIE = "impersonating";
    cookies.mockResolvedValue({
      get: vi.fn().mockReturnValue(undefined),
    });
    auth.mockReturnValue({
      getAccessToken,
    });
    getAccessToken.mockResolvedValue(undefined);
    createLogger.mockResolvedValue(mockedLogger);
    getZodDefinitionForRequest.mockReturnValue(undefined);
  });

  it("returns a generic request-body detail while logging the specific parser error with the same trace id", async () => {
    const { POST } = await import("./route");
    const request = {
      method: "POST",
      url: "https://systemuptimetracker.test/api/example",
      headers: new Headers({
        "content-type": "application/json",
      }),
      json: vi.fn().mockRejectedValue(new Error("Unexpected token < in JSON")),
    };

    const response = await POST(request as unknown as Request, {
      params: Promise.resolve({
        routeparts: ["example"],
      }),
    });
    const payload = await response.json();

    expect(response.status).toBe(400);
    expect(payload.error).toBe("Invalid request body.");
    expect(payload.detail).toContain("Trace ID:");
    expect(payload.traceId).toMatch(/^[0-9a-f]{32}$/i);
    expect(response.headers.get("x-trace-id")).toBe(payload.traceId);
    expect(mockedLogger.warn).toHaveBeenCalledWith(
      "API proxy request body validation failed",
      expect.objectContaining({
        method: "POST",
        route: "example",
        errorMessage: "Unexpected token < in JSON",
        traceId: payload.traceId,
      }),
    );
  });

  it("scrubs upstream error details while preserving the backend trace id", async () => {
    const upstreamTraceId = "0123456789abcdef0123456789abcdef";
    vi.mocked(global.fetch).mockResolvedValue({
      ok: false,
      status: 500,
      statusText: "Internal Server Error",
      headers: new Headers({
        "content-type": "application/json",
        "x-trace-id": upstreamTraceId,
      }),
      json: vi.fn().mockResolvedValue({
        title: "Unhandled exception",
        detail: "stack trace should not be exposed",
        traceId: upstreamTraceId,
      }),
    } as unknown as Response);

    const { GET } = await import("./route");
    const request = {
      method: "GET",
      url: "https://systemuptimetracker.test/api/example",
      headers: new Headers(),
    };

    const response = await GET(request as unknown as Request, {
      params: Promise.resolve({
        routeparts: ["example"],
      }),
    });
    const payload = await response.json();

    expect(response.status).toBe(500);
    expect(payload).toEqual({
      error: "Something went wrong.",
      detail: `The request could not be completed. Trace ID: ${upstreamTraceId}.`,
      traceId: upstreamTraceId,
    });
    expect(response.headers.get("x-trace-id")).toBe(upstreamTraceId);
    expect(mockedLogger.warn).toHaveBeenCalledWith(
      "Upstream API request returned non-success status",
      expect.objectContaining({
        route: "example",
        statusCode: 500,
        traceId: upstreamTraceId,
      }),
    );
  });

  it("normalizes API base URLs without requiring a trailing slash", async () => {
    process.env.API_BASE_URL = "https://example.test";
    vi.mocked(global.fetch).mockResolvedValue({
      ok: true,
      status: 200,
      statusText: "OK",
      headers: new Headers(),
    } as unknown as Response);

    const { serverApiPost, serverApiGet } = await import("@/utils/server-api");

    await serverApiPost({ url: "api/users/user-001/activation" });
    await serverApiGet({ url: "api/users?view=active" });

    expect(global.fetch).toHaveBeenNthCalledWith(
      1,
      "https://example.test/api/users/user-001/activation",
      expect.any(Object),
    );
    expect(global.fetch).toHaveBeenNthCalledWith(
      2,
      "https://example.test/api/users?view=active",
      expect.any(Object),
    );
  });

  it("returns expected 404 responses without logging them as server API errors", async () => {
    vi.mocked(global.fetch).mockResolvedValue({
      ok: false,
      status: 404,
      statusText: "Not Found",
      headers: new Headers({
        "content-type": "application/json",
        "x-trace-id": "trace-not-found-001",
      }),
      json: vi.fn().mockResolvedValue({
        title: "Not Found",
        traceId: "trace-not-found-001",
      }),
    } as unknown as Response);

    const { serverApiGet } = await import("@/utils/server-api");

    const response = await serverApiGet({
      url: "api/users/user-001",
      expectedStatusCodes: [404],
    });

    expect(response).toMatchObject({
      status: 404,
      statusText: "Not Found",
    });
    expect(mockedLogger.error).not.toHaveBeenCalledWith(
      "Server API GET request failed",
      expect.anything(),
    );
  });
});
