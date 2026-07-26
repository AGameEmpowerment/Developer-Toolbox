// @vitest-environment node

import { beforeEach, describe, expect, it, vi } from "vitest";

const info = vi.fn();
const debug = vi.fn();
const warn = vi.fn();
const error = vi.fn();
const trackEvent = vi.fn();
const createLogger = vi.fn(() => ({
  info,
  debug,
  warn,
  error,
  trackEvent,
}));

vi.mock("@/utils/logger-server", () => ({
  createLogger,
}));

describe("withServerPageLogging", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("tracks a successful render", async () => {
    const { withServerPageLogging } = await import("./contextual-logger");

    const result = await withServerPageLogging({
      category: "HomePage",
      context: { route: "/", page: "Home" },
      render: async () => "ok",
    });

    expect(result).toBe("ok");
    await vi.waitFor(() => {
      expect(info).toHaveBeenCalledWith("Rendering frontend page", {
        area: "frontend-page",
        route: "/",
        page: "Home",
      });
    });
    await vi.waitFor(() => {
      expect(trackEvent).toHaveBeenCalledWith(
        "FrontendPageRendered",
        {
          area: "frontend-page",
          route: "/",
          page: "Home",
        },
        undefined,
      );
    });
    expect(error).not.toHaveBeenCalled();
  });

  it("does not log expected Next.js control flow as an error", async () => {
    const { withServerPageLogging } = await import("./contextual-logger");
    const expectedError = {
      digest: "DYNAMIC_SERVER_USAGE",
      message: "Dynamic server usage",
    };

    await expect(
      withServerPageLogging({
        category: "HomePage",
        context: { route: "/", page: "Home" },
        render: async () => {
          throw expectedError;
        },
      }),
    ).rejects.toBe(expectedError);

    expect(debug).toHaveBeenCalledWith(
      "Frontend page render returned Next.js control flow",
      {
        area: "frontend-page",
        route: "/",
        page: "Home",
        digest: "DYNAMIC_SERVER_USAGE",
      },
    );
    expect(error).not.toHaveBeenCalled();
  });
});
