import type { ReactNode } from "react";
import { act } from "react";

import { describe, expect, it, vi } from "vitest";

import { genericTests, getTestContext } from "@/utils/testHelper";

vi.mock("next/link", () => ({
  default: ({
    href,
    children,
    ...rest
  }: {
    href: string;
    children: ReactNode;
    [key: string]: unknown;
  }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

vi.mock("./AppNavLinks", () => ({
  default: () => <div data-testid="app-nav-links">App nav links</div>,
}));

const { default: AppHeader } = await import("./AppHeader");

const context = getTestContext();

describe("AppHeader", () => {
  genericTests(context, AppHeader);

  it("renders a skip link that targets the main content region", async () => {
    await act(async () => {
      context.root?.render(<AppHeader />);
    });

    const skipLink = context.container?.querySelector(
      "a[href='#main-content']",
    );

    expect(skipLink).toBeTruthy();
    expect(skipLink?.textContent).toContain("Skip to main content");
  });

  it("renders the branded home link and primary navigation landmark", async () => {
    await act(async () => {
      context.root?.render(<AppHeader name="System Uptime Tracker" />);
    });

    const brandLink = context.container?.querySelector("a[href='/']");
    const nav = context.container?.querySelector(
      "nav[aria-label='Primary navigation']",
    );

    expect(brandLink).toBeTruthy();
    expect(brandLink?.textContent).toContain("System Uptime Tracker");
    expect(nav).toBeTruthy();
  });

  it("includes the application navigation links region", async () => {
    await act(async () => {
      context.root?.render(<AppHeader />);
    });

    expect(
      context.container?.querySelector("[data-testid='app-nav-links']"),
    ).toBeTruthy();
  });
});
