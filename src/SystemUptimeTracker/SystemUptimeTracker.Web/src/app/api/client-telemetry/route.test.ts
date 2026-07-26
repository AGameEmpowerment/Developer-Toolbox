import { beforeEach, describe, expect, it, vi } from "vitest";

const warning = vi.fn().mockResolvedValue(undefined);
const error = vi.fn().mockResolvedValue(undefined);
const createLogger = vi.fn(async () => ({
  warning,
  error,
}));
const clientLog = vi.fn().mockResolvedValue(undefined);
const clientTrackEvent = vi.fn().mockResolvedValue(undefined);
const clientTrackException = vi.fn().mockResolvedValue(undefined);
const clientTrackPageView = vi.fn().mockResolvedValue(undefined);

vi.mock("@/utils/logger-server", () => ({
  createLogger,
  clientLog,
  clientTrackEvent,
  clientTrackException,
  clientTrackPageView,
}));

const { POST } = await import("./route");

describe("client telemetry route", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("should reject invalid JSON payloads and log a warning", async () => {
    const response = await POST({
      method: "POST",
      json: vi.fn().mockRejectedValue(new Error("Invalid JSON")),
    } as unknown as Request);

    expect(response.status).toBe(400);
    await expect(response.json()).resolves.toEqual({
      error: "Telemetry payload must be valid JSON.",
    });
    expect(createLogger).toHaveBeenCalledWith("ClientTelemetryRoute");
    expect(warning).toHaveBeenCalledWith(
      "Client telemetry request contained invalid JSON.",
      expect.objectContaining({
        requestMethod: "POST",
        route: "/api/client-telemetry",
      }),
    );
  });

  it("should reject unsupported telemetry actions and log a warning", async () => {
    const response = await POST({
      method: "POST",
      json: vi.fn().mockResolvedValue({
        actionName: "missingAction",
        args: ["one", "two"],
      }),
    } as unknown as Request);

    expect(response.status).toBe(400);
    await expect(response.json()).resolves.toEqual({
      error: "Telemetry action is not supported.",
    });
    expect(warning).toHaveBeenCalledWith(
      "Client telemetry request referenced an unsupported action.",
      expect.objectContaining({
        actionName: "missingAction",
        argumentCount: 2,
        route: "/api/client-telemetry",
      }),
    );
  });

  it("should log handler failures before returning a generic server error", async () => {
    clientTrackEvent.mockRejectedValueOnce(new Error("sink unavailable"));

    const response = await POST({
      method: "POST",
      json: vi.fn().mockResolvedValue({
        actionName: "clientTrackEvent",
        args: ["Clicked", { feature: "toolbar" }, { durationMs: 10 }],
      }),
    } as unknown as Request);

    expect(response.status).toBe(500);
    await expect(response.json()).resolves.toEqual({
      error: "Telemetry dispatch failed.",
    });
    expect(error).toHaveBeenCalledWith(
      "Client telemetry dispatch failed.",
      expect.any(Error),
      expect.objectContaining({
        actionName: "clientTrackEvent",
        argumentCount: 3,
        route: "/api/client-telemetry",
      }),
    );
  });
});
