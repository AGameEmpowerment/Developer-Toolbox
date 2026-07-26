import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

describe("frontend health route", () => {
  const originalApiBaseUrl = process.env.API_BASE_URL;

  beforeEach(() => {
    vi.resetModules();
    vi.clearAllMocks();
    process.env.API_BASE_URL = "http://systemuptimetracker-backend:8002/";
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response("{}", { status: 200 })),
    );
  });

  afterEach(() => {
    process.env.API_BASE_URL = originalApiBaseUrl;
    vi.unstubAllGlobals();
  });

  it("returns healthy when the frontend route and backend health endpoint are reachable", async () => {
    const { GET } = await import("./route");
    const response = await GET();
    const payload = await response.json();

    expect(response.status).toBe(200);
    expect(payload.status).toBe("healthy");
    expect(payload.frontend.status).toBe("healthy");
    expect(payload.api).toMatchObject({
      status: "healthy",
      url: "http://systemuptimetracker-backend:8002/_health",
      statusCode: 200,
    });
    expect(fetch).toHaveBeenCalledWith(
      "http://systemuptimetracker-backend:8002/_health",
      expect.objectContaining({
        cache: "no-store",
      }),
    );
  });

  it("returns unhealthy when the backend health endpoint is not reachable", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response("unavailable", { status: 503 })),
    );

    const { GET } = await import("./route");
    const response = await GET();
    const payload = await response.json();

    expect(response.status).toBe(503);
    expect(payload.status).toBe("unhealthy");
    expect(payload.frontend.status).toBe("healthy");
    expect(payload.api).toMatchObject({
      status: "unhealthy",
      url: "http://systemuptimetracker-backend:8002/_health",
      statusCode: 503,
    });
  });

  it("returns unhealthy when API_BASE_URL is missing", async () => {
    delete process.env.API_BASE_URL;

    const { GET } = await import("./route");
    const response = await GET();
    const payload = await response.json();

    expect(response.status).toBe(503);
    expect(payload.status).toBe("unhealthy");
    expect(payload.api).toMatchObject({
      status: "unhealthy",
      error: "API_BASE_URL must be configured.",
    });
  });
});
