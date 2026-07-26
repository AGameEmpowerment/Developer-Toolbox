import { describe, expect, it, vi } from "vitest";

const getAccessToken = vi.fn();
const fetchWithLoopbackTlsBypass = vi.fn();

vi.mock("server-only", () => ({}));
vi.mock("@/utils/auth/auth", () => ({
  auth: () => ({ getAccessToken }),
  fetchWithLoopbackTlsBypass,
  getApiBaseUrl: () => "https://api.test/",
}));

const { getAuthorizationPolicies } = await import("./authorization-policies");

describe("getAuthorizationPolicies", () => {
  it("rejects partial policy payloads", async () => {
    getAccessToken.mockResolvedValue("token");
    fetchWithLoopbackTlsBypass.mockResolvedValue(
      new Response(JSON.stringify({}), {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
    );

    await expect(getAuthorizationPolicies()).rejects.toThrow(
      "The authorization policy response was invalid.",
    );
  });
});
