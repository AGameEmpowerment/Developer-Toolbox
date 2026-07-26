import type { ReactNode } from "react";
import { act } from "react";

import { beforeEach, describe, expect, it, vi } from "vitest";

import { getTestContext } from "@/utils/testHelper";

const usePathname = vi.fn(() => "/");
const useEnvironment = vi.fn(() => ({
  environment: "test",
  appVersion: "2.3.4",
}));
const initializeLogger = vi.fn();
const trackPageView = vi.fn().mockResolvedValue(undefined);
const createClientLogger = vi.fn();

const mockedLogger = {
  debug: vi.fn().mockResolvedValue(undefined),
  info: vi.fn().mockResolvedValue(undefined),
};

vi.mock("next/navigation", () => ({
  usePathname,
}));

vi.mock("@/utils/env/env-provider", () => ({
  useEnvironment,
}));

vi.mock("@/utils/logger-client", () => ({
  createClientLogger,
  initializeLogger,
  trackPageView,
}));

const { default: LoggerClientWrapper } = await import("./LoggerClientWrapper");

const context = getTestContext();

function renderWrapper(
  user?: { sub?: string } | null,
  children: ReactNode = <div>Child</div>,
) {
  return act(async () => {
    context.root?.render(
      <LoggerClientWrapper user={user}>{children}</LoggerClientWrapper>,
    );
  });
}

describe("LoggerClientWrapper", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    usePathname.mockReturnValue("/");
    useEnvironment.mockReturnValue({
      environment: "test",
      appVersion: "2.3.4",
    });
    createClientLogger.mockReturnValue(mockedLogger);
    mockedLogger.debug.mockResolvedValue(undefined);
    mockedLogger.info.mockResolvedValue(undefined);
    trackPageView.mockResolvedValue(undefined);
    window.history.replaceState({}, "System Uptime Tracker", "/");
    document.title = "System Uptime Tracker";
  });

  it("logs a debug page landing once when the wrapper renders", async () => {
    await renderWrapper({ sub: "user-123" });

    expect(initializeLogger).toHaveBeenCalledWith("test", "2.3.4");
    expect(createClientLogger).toHaveBeenCalledWith("PageNavigation");
    expect(mockedLogger.debug).toHaveBeenCalledWith("Page landed", {
      pageName: "System Uptime Tracker",
      pathname: "/",
      isAuthenticated: true,
      isInitialLoad: true,
    });
    expect(trackPageView).toHaveBeenCalledWith(
      "System Uptime Tracker",
      window.location.href,
      {
        pathname: "/",
        isAuthenticated: true,
        isInitialLoad: true,
      },
    );
  });

  it("tracks another page view when the route changes", async () => {
    await renderWrapper();

    window.history.replaceState(
      {},
      "Admin Users | System Uptime Tracker",
      "/admin/users",
    );
    document.title = "Admin Users | System Uptime Tracker";
    usePathname.mockReturnValue("/admin/users");

    await renderWrapper();

    expect(mockedLogger.debug).toHaveBeenNthCalledWith(2, "Page landed", {
      pageName: "Admin Users | System Uptime Tracker",
      pathname: "/admin/users",
      isAuthenticated: false,
      isInitialLoad: false,
    });
    expect(trackPageView).toHaveBeenNthCalledWith(
      2,
      "Admin Users | System Uptime Tracker",
      window.location.href,
      {
        pathname: "/admin/users",
        isAuthenticated: false,
        isInitialLoad: false,
      },
    );
  });

  it("tracks a page again when navigation returns to it", async () => {
    await renderWrapper();

    window.history.replaceState(
      {},
      "Admin Users | System Uptime Tracker",
      "/admin/users",
    );
    document.title = "Admin Users | System Uptime Tracker";
    usePathname.mockReturnValue("/admin/users");

    await renderWrapper();

    window.history.replaceState({}, "System Uptime Tracker", "/");
    document.title = "System Uptime Tracker";
    usePathname.mockReturnValue("/");

    await renderWrapper();

    expect(trackPageView).toHaveBeenNthCalledWith(
      3,
      "System Uptime Tracker",
      window.location.href,
      {
        pathname: "/",
        isAuthenticated: false,
        isInitialLoad: false,
      },
    );
  });

  it("does not log the same page again when only auth state changes", async () => {
    await renderWrapper();

    await renderWrapper({ sub: "user-123" });

    expect(mockedLogger.debug).toHaveBeenCalledTimes(1);
    expect(mockedLogger.debug).toHaveBeenCalledWith("Page landed", {
      pageName: "System Uptime Tracker",
      pathname: "/",
      isAuthenticated: false,
      isInitialLoad: true,
    });
    expect(trackPageView).toHaveBeenCalledTimes(1);
  });
});
