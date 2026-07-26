import { beforeEach, describe, expect, it, vi } from "vitest";

const cookies = vi.fn();

vi.mock("next/headers", () => ({
  cookies,
}));

vi.mock("next/server", () => ({
  NextResponse: {
    next: () => new Response(null, { status: 200 }),
    redirect: (url: URL | string) =>
      new Response(null, {
        status: 302,
        headers: {
          location: typeof url === "string" ? url : url.toString(),
        },
      }),
  },
}));

describe("local identity auth pages", () => {
  beforeEach(() => {
    vi.resetModules();
    vi.clearAllMocks();
    process.env.APP_BASE_URL = "https://app.test";
    process.env.API_BASE_URL = "https://api.test/";
    process.env.AUTH_COOKIE_SECRET = "test-cookie-secret";
    global.fetch = vi.fn() as unknown as typeof fetch;
    cookies.mockResolvedValue({
      get: vi.fn().mockReturnValue(undefined),
    });
  });

  it("redirects login requests to first-time setup when no administrator exists", async () => {
    vi.mocked(global.fetch).mockResolvedValue(
      new Response(
        JSON.stringify({
          hasUsers: true,
          hasAdministrators: false,
          isFirstTimeSetup: true,
          canCreateFirstUser: true,
        }),
        {
          status: 200,
          headers: { "content-type": "application/json" },
        },
      ),
    );

    const { auth } = await import("./auth");
    const response = await auth().login(
      new Request("https://app.test/auth/login?returnTo=/admin/users"),
    );

    expect(response.status).toBe(302);
    expect(response.headers.get("location")).toBe(
      "https://app.test/auth/create-user?firstSetup=true&returnTo=%2Fadmin%2Fusers",
    );
  });

  it("renders the sign-in page when setup status reports an active administrator", async () => {
    vi.mocked(global.fetch).mockResolvedValue(
      new Response(
        JSON.stringify({
          hasUsers: true,
          hasAdministrators: true,
          isFirstTimeSetup: false,
          canCreateFirstUser: false,
        }),
        {
          status: 200,
          headers: { "content-type": "application/json" },
        },
      ),
    );

    const { auth } = await import("./auth");
    const response = await auth().login(
      new Request("https://app.test/auth/login?returnTo=/"),
    );
    const html = await response.text();

    expect(response.status).toBe(200);
    expect(html).toContain('<h1 id="auth-login-page-title"');
    expect(html).toContain("Sign in");
    expect(html).not.toContain("First Time Setup");
  });

  it("renders first-time setup copy without a bootstrap token field", async () => {
    vi.mocked(global.fetch).mockResolvedValue(
      new Response(
        JSON.stringify({
          hasUsers: true,
          hasAdministrators: false,
          isFirstTimeSetup: true,
          canCreateFirstUser: true,
        }),
        {
          status: 200,
          headers: { "content-type": "application/json" },
        },
      ),
    );

    const { auth } = await import("./auth");
    const response = await auth().createUser(
      new Request("https://app.test/auth/create-user?returnTo=/admin/users"),
    );
    const html = await response.text();

    expect(response.status).toBe(200);
    expect(html).toContain("First Time Setup");
    expect(html).toContain(
      "Create the first administrator account for this System Uptime Tracker database.",
    );
    expect(html).not.toContain("Bootstrap token");
    expect(html).toContain('name="firstTimeSetup" value="true"');
  });

  it("renders the standard create-user page when setup is complete", async () => {
    vi.mocked(global.fetch).mockResolvedValue(
      new Response(
        JSON.stringify({
          hasUsers: true,
          hasAdministrators: true,
          isFirstTimeSetup: false,
          canCreateFirstUser: false,
        }),
        {
          status: 200,
          headers: { "content-type": "application/json" },
        },
      ),
    );

    const { auth } = await import("./auth");
    const response = await auth().createUser(
      new Request("https://app.test/auth/create-user"),
    );
    const html = await response.text();

    expect(response.status).toBe(200);
    expect(html).toContain("Create User");
    expect(html).toContain(
      "Create a local System Uptime Tracker identity account.",
    );
    expect(html).not.toContain('name="firstTimeSetup" value="true"');
  });
});
