import { act } from "react";

import { beforeEach, describe, expect, it, vi } from "vitest";

import { getTestContext } from "@/utils/testHelper";

const requireSignOn = vi.fn();
const getAuthorizationPolicies = vi.fn();
const serverApiGetAsJson = vi.fn();
const getSession = vi.fn();
const notFound = vi.fn(() => {
  throw new Error("NEXT_NOT_FOUND");
});

vi.mock("@/utils/auth/require-sign-on", () => ({
  default: requireSignOn,
}));

vi.mock("@/utils/auth/authorization-policies", () => ({
  getAuthorizationPolicies,
}));

vi.mock("@/utils/server-api-get", () => ({
  serverApiGetAsJson,
}));

vi.mock("@/features/users/server/user-management-actions", () => ({
  assignUserRoles: vi.fn(),
  setUserActivation: vi.fn(),
}));

vi.mock("@/features/users/user-roles", () => ({
  assignableRoles: ["Admin", "Manager", "Contributor", "Read"],
}));

vi.mock("next/navigation", () => ({
  notFound,
}));

vi.mock("@/utils/auth/auth", () => ({
  auth: () => ({ getSession }),
}));

const { default: AdminUsersPage } = await import("./page");

const context = getTestContext();

async function renderAdminUsersPage() {
  const page = await AdminUsersPage();

  await act(async () => {
    context.root?.render(page);
  });
}

describe("AdminUsersPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getSession.mockResolvedValue(null);
  });

  it("renders the user management table when the signed-in user can manage users", async () => {
    getAuthorizationPolicies.mockResolvedValue({ canManageUsers: true });
    serverApiGetAsJson.mockResolvedValue([
      {
        userId: "user-001",
        email: "pending@example.test",
        displayName: "Pending User",
        roles: [],
        isActive: true,
        createdAtUtc: "2026-05-12T00:00:00Z",
        lastLoginAtUtc: null,
      },
    ]);

    const result = await AdminUsersPage();

    expect(requireSignOn).toHaveBeenCalled();
    expect(serverApiGetAsJson).toHaveBeenCalledWith({
      url: "api/users",
      defaultData: [],
    });
    expect(result).toBeTruthy();
  });

  it("renders role-editing and activation controls for each user row", async () => {
    getAuthorizationPolicies.mockResolvedValue({ canManageUsers: true });
    serverApiGetAsJson.mockResolvedValue([
      {
        userId: "user-001",
        email: "active@example.test",
        displayName: "Active User",
        roles: ["Contributor"],
        isActive: true,
        createdAtUtc: "2026-05-12T00:00:00Z",
        lastLoginAtUtc: "2026-05-28T10:00:00Z",
      },
      {
        userId: "user-002",
        email: "inactive@example.test",
        displayName: "Inactive User",
        roles: [],
        isActive: false,
        createdAtUtc: "2026-05-13T00:00:00Z",
        lastLoginAtUtc: null,
      },
    ]);

    await renderAdminUsersPage();

    expect(
      context.container?.querySelector(
        "[data-testid='admin-users-page-table']",
      ),
    ).toBeTruthy();
    expect(context.container?.textContent).toContain("Save roles");
    expect(context.container?.textContent).toContain("Deactivate");
    expect(context.container?.textContent).toContain("Activate");
    expect(context.container?.textContent).toContain("1 pending");
    expect(context.container?.textContent).toContain("Last seen");
    expect(context.container?.textContent).toContain("never");

    const contributorCheckbox =
      context.container?.querySelector<HTMLInputElement>(
        "#user-001-Contributor",
      );
    const adminCheckbox =
      context.container?.querySelector<HTMLInputElement>("#user-001-Admin");
    const pendingUserReadCheckbox =
      context.container?.querySelector<HTMLInputElement>("#user-002-Read");

    expect(contributorCheckbox?.checked).toBe(true);
    expect(adminCheckbox?.checked).toBe(false);
    expect(pendingUserReadCheckbox?.checked).toBe(false);

    const activeUserRoleButton = context.container?.querySelector(
      "button[aria-label='Save roles for Active User']",
    );
    const deactivateButton = context.container?.querySelector(
      "button[aria-label='Deactivate Active User']",
    );
    const activateButton = context.container?.querySelector(
      "button[aria-label='Activate Inactive User']",
    );

    expect(activeUserRoleButton).toBeTruthy();
    expect(deactivateButton).toBeTruthy();
    expect(activateButton).toBeTruthy();
  });

  it("disables the activation button and shows a you-badge for the current signed-in user", async () => {
    getAuthorizationPolicies.mockResolvedValue({ canManageUsers: true });
    getSession.mockResolvedValue({
      user: {
        email: "admin@example.test",
        sub: "admin@example.test",
        name: "Admin",
      },
    });
    serverApiGetAsJson.mockResolvedValue([
      {
        userId: "user-001",
        email: "admin@example.test",
        displayName: "Admin User",
        roles: ["Admin"],
        isActive: true,
        createdAtUtc: "2026-05-12T00:00:00Z",
        lastLoginAtUtc: "2026-05-30T09:00:00Z",
      },
      {
        userId: "user-002",
        email: "other@example.test",
        displayName: "Other User",
        roles: ["Contributor"],
        isActive: true,
        createdAtUtc: "2026-05-13T00:00:00Z",
        lastLoginAtUtc: null,
      },
    ]);

    await renderAdminUsersPage();

    const youBadge = context.container?.querySelector(
      "[data-testid='admin-user-user-001-you-badge']",
    );
    expect(youBadge).toBeTruthy();

    const selfDeactivateButton =
      context.container?.querySelector<HTMLButtonElement>(
        "button[aria-label='Deactivate Admin User']",
      );
    const otherDeactivateButton =
      context.container?.querySelector<HTMLButtonElement>(
        "button[aria-label='Deactivate Other User']",
      );

    expect(selfDeactivateButton?.disabled).toBe(true);
    expect(otherDeactivateButton?.disabled).toBe(false);
  });

  it("returns not found when the signed-in user cannot manage users", async () => {
    getAuthorizationPolicies.mockResolvedValue({ canManageUsers: false });

    await expect(AdminUsersPage()).rejects.toThrow("NEXT_NOT_FOUND");
  });
});
