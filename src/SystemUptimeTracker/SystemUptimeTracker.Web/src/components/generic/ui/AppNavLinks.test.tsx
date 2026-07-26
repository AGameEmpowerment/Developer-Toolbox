import { act } from "react";

import { describe, expect, it, vi } from "vitest";

import { genericTests, getTestContext } from "@/utils/testHelper";

// ── Module mocks ──────────────────────────────────────────────────────────────

const usePathname = vi.fn(() => "/");

vi.mock("next/navigation", () => ({
  usePathname,
}));

vi.mock("next/link", () => ({
  // Render a plain anchor so jsdom can resolve href checks
  default: ({
    href,
    children,
    ...rest
  }: {
    href: string;
    children: React.ReactNode;
    [key: string]: unknown;
  }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

vi.mock("@/utils/auth/session-user-provider", () => ({
  useSessionUser: vi.fn(() => null),
}));

// ── Dynamic imports after mocks ───────────────────────────────────────────────

const { default: AppNavLinks } = await import("./AppNavLinks");
const { useSessionUser } = await import("@/utils/auth/session-user-provider");

const adminPermissions = {
  canManageUsers: true,
};

// ── Tests ─────────────────────────────────────────────────────────────────────

const context = getTestContext();

describe("AppNavLinks", () => {
  genericTests(context, AppNavLinks);

  describe("navigation links", () => {
    it("renders the Admin dropdown toggle button", async () => {
      await act(async () => {
        context.root.render(<AppNavLinks permissions={adminPermissions} />);
      });

      const toggle = context.container?.querySelector(
        "button[aria-controls='admin-nav-dropdown-menu']",
      );
      expect(toggle).toBeTruthy();
      expect(toggle?.textContent).toContain("Admin");
    });

    it("renders the admin Users sub-link", async () => {
      await act(async () => {
        context.root.render(<AppNavLinks permissions={adminPermissions} />);
      });

      const usersLink = context.container?.querySelector(
        "a[href='/admin/users']",
      );
      expect(usersLink).toBeTruthy();
      expect(usersLink?.textContent).toContain("Users");
    });

    it("hides the Admin dropdown when no admin permissions are present", async () => {
      await act(async () => {
        context.root.render(<AppNavLinks permissions={undefined} />);
      });

      const toggle = context.container?.querySelector(
        "button[aria-controls='admin-nav-dropdown-menu']",
      );
      expect(toggle).toBeNull();
    });

    it("shows a Sign In link when no user is authenticated", async () => {
      await act(async () => {
        context.root.render(<AppNavLinks permissions={adminPermissions} />);
      });

      const signIn = context.container?.querySelector("a[href='/auth/login']");
      expect(signIn).toBeTruthy();
      expect(signIn?.textContent).toContain("Sign In");
    });
  });

  describe("when a user is authenticated", () => {
    it("shows the user name instead of the Sign In link", async () => {
      vi.mocked(useSessionUser).mockReturnValueOnce({
        name: "Alice",
        email: "alice@example.com",
      });

      await act(async () => {
        context.root.render(<AppNavLinks permissions={adminPermissions} />);
      });

      expect(context.container?.textContent).toContain("Alice");
      expect(
        context.container?.querySelector("a[href='/auth/login']"),
      ).toBeNull();
    });
  });

  describe("Admin dropdown toggle", () => {
    it("starts collapsed (aria-expanded=false)", async () => {
      await act(async () => {
        context.root.render(<AppNavLinks permissions={adminPermissions} />);
      });

      const toggle = context.container?.querySelector(
        "button[aria-controls='admin-nav-dropdown-menu']",
      );
      expect(toggle?.getAttribute("aria-expanded")).toBe("false");
    });

    it("expands when the toggle is clicked", async () => {
      await act(async () => {
        context.root.render(<AppNavLinks permissions={adminPermissions} />);
      });

      const toggle = context.container?.querySelector(
        "button[aria-controls='admin-nav-dropdown-menu']",
      );
      await act(async () => {
        toggle?.dispatchEvent(new MouseEvent("click", { bubbles: true }));
      });

      expect(toggle?.getAttribute("aria-expanded")).toBe("true");

      const menu = context.container?.querySelector("#admin-nav-dropdown-menu");
      expect(menu?.className).toContain("show");
    });

    it("closes when a pointer interaction happens outside the dropdown", async () => {
      await act(async () => {
        context.root.render(<AppNavLinks permissions={adminPermissions} />);
      });

      const toggle = context.container?.querySelector(
        "button[aria-controls='admin-nav-dropdown-menu']",
      );

      await act(async () => {
        toggle?.dispatchEvent(new MouseEvent("click", { bubbles: true }));
      });

      expect(toggle?.getAttribute("aria-expanded")).toBe("true");

      await act(async () => {
        document.dispatchEvent(
          new PointerEvent("pointerdown", { bubbles: true }),
        );
      });

      expect(toggle?.getAttribute("aria-expanded")).toBe("false");
    });
  });

  describe("Mobile navigation", () => {
    it("closes the expanded mobile nav after the route changes", async () => {
      usePathname.mockReturnValue("/");

      await act(async () => {
        context.root.render(<AppNavLinks />);
      });

      const navToggle = context.container?.querySelector(
        "button[aria-controls='app-nav-collapse']",
      );

      await act(async () => {
        navToggle?.dispatchEvent(new MouseEvent("click", { bubbles: true }));
      });

      const collapse = context.container?.querySelector("#app-nav-collapse");
      expect(collapse?.className).toContain("show");

      usePathname.mockReturnValue("/admin/users");

      await act(async () => {
        context.root.render(<AppNavLinks />);
      });

      expect(collapse?.className).not.toContain("show");
      expect(navToggle?.getAttribute("aria-expanded")).toBe("false");
    });
  });
});
