import { act } from "react";

import { describe, expect, it, vi } from "vitest";

import { getTestContext } from "@/utils/testHelper";

const requireSignOn = vi.fn().mockResolvedValue(undefined);
const getSession = vi.fn().mockResolvedValue({
  user: { name: "Test User", email: "test-user@example.test" },
});

vi.mock("@/utils/auth/require-sign-on", () => ({
  default: requireSignOn,
}));

vi.mock("@/utils/auth/auth", () => ({
  auth: () => ({ getSession }),
}));

const { default: Home } = await import("./page");

const context = getTestContext();

async function renderHome() {
  const page = await Home();

  await act(async () => {
    context.root?.render(page);
  });
}

describe("Home page", () => {
  it("renders the operational landing page at the root route", async () => {
    await renderHome();

    expect(requireSignOn).toHaveBeenCalled();
    expect(
      context.container?.querySelector("[data-testid='home-page-title']")
        ?.textContent,
    ).toContain("Test User");
    expect(
      context.container?.querySelector(
        "[data-testid='home-page-overview-copy']",
      )?.textContent,
    ).toContain("operational landing page");
    expect(
      context.container?.querySelector("[data-testid='home-page-account']"),
    ).not.toBeNull();
  });

  it("passes baseline accessibility checks", async () => {
    await renderHome();

    const results = await global.axe(context.container);

    expect(results).toHaveNoViolations();
  });
});
