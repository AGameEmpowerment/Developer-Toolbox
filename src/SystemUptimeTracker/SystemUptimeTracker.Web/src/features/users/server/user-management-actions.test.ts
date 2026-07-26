import { beforeEach, describe, expect, it, vi } from "vitest";

const revalidatePath = vi.fn();
const serverApiPut = vi.fn();
const createLogger = vi.fn();

vi.mock("next/cache", () => ({
  revalidatePath,
}));

vi.mock("@/utils/server-api", () => ({
  serverApiPut,
}));

vi.mock("@/utils/logger-server", () => ({
  createLogger,
}));

const mockLogger = {
  warn: vi.fn().mockResolvedValue(undefined),
};

const { assignUserRoles, setUserActivation } =
  await import("./user-management-actions");

describe("user-management-actions", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    createLogger.mockResolvedValue(mockLogger);
    mockLogger.warn.mockResolvedValue(undefined);
  });

  it("updates user roles and revalidates the admin users page on success", async () => {
    serverApiPut.mockResolvedValue(new Response(null, { status: 200 }));

    const formData = new FormData();
    formData.set("userId", "user-001");
    formData.append("roles", "Contributor");
    formData.append("roles", "Read");

    await assignUserRoles(formData);

    expect(serverApiPut).toHaveBeenCalledWith({
      url: "api/users/user-001/roles",
      body: {
        roles: ["Contributor", "Read"],
      },
    });
    expect(revalidatePath).toHaveBeenCalledWith("/admin/users");
  });

  it("updates user activation and revalidates the admin users page on success", async () => {
    serverApiPut.mockResolvedValue(new Response(null, { status: 200 }));

    const formData = new FormData();
    formData.set("userId", "user-001");
    formData.set("isActive", "false");

    await setUserActivation(formData);

    expect(serverApiPut).toHaveBeenCalledWith({
      url: "api/users/user-001/activation",
      body: {
        isActive: false,
      },
    });
    expect(revalidatePath).toHaveBeenCalledWith("/admin/users");
  });
});
