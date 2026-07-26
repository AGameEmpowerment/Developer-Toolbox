import crypto from "node:crypto";
import http from "node:http";
import https from "node:https";

import { cookies } from "next/headers";
import { NextResponse } from "next/server";

import {
  appendTraceId,
  createTraceId,
  extractTraceId,
} from "@/utils/error-reference";

const sessionCookieName = "systemuptimetracker.auth.session";
const defaultSessionLifetimeSeconds = 60 * 60;

type SessionUser = {
  sub: string;
  name: string;
  email: string;
};

type StoredSession = {
  accessToken?: string;
  expiresAt: string;
  user: SessionUser;
};

type LoginPayload = {
  detail?: string;
  expiresIn?: number | string;
  accessToken?: string;
  refreshToken?: string;
  traceId?: string;
};

type BootstrapPayload = {
  errors?: Record<string, string[]>;
  message?: string;
  detail?: string;
  traceId?: string;
};

type SetupStatusPayload = {
  hasUsers?: boolean;
  hasAdministrators?: boolean;
  isFirstTimeSetup?: boolean;
  canCreateFirstUser?: boolean;
};

type NodeFetchOptions = RequestInit & {
  headers?: Record<string, string>;
};

let authInstance: LocalIdentityAuthClient | null = null;

const getAppBaseUrl = () =>
  process.env.APP_BASE_URL || "https://localhost:3001";

export const getApiBaseUrl = () =>
  process.env.API_BASE_URL || "https://localhost:7060/";

const getCookieSecret = () => {
  const configuredSecret = process.env.AUTH_COOKIE_SECRET;
  if (configuredSecret) {
    return configuredSecret;
  }

  if (process.env.NODE_ENV === "production") {
    throw new Error("AUTH_COOKIE_SECRET must be configured in production.");
  }

  return "development-only-local-auth-cookie-secret";
};

const getEncryptionKey = () =>
  crypto.createHash("sha256").update(getCookieSecret()).digest();

const encrypt = (payload: StoredSession) => {
  const iv = crypto.randomBytes(12);
  const cipher = crypto.createCipheriv("aes-256-gcm", getEncryptionKey(), iv);
  const encrypted = Buffer.concat([
    cipher.update(JSON.stringify(payload), "utf8"),
    cipher.final(),
  ]);
  const tag = cipher.getAuthTag();

  return Buffer.concat([iv, tag, encrypted]).toString("base64url");
};

const decrypt = (value: string): StoredSession => {
  const buffer = Buffer.from(value, "base64url");
  const iv = buffer.subarray(0, 12);
  const tag = buffer.subarray(12, 28);
  const encrypted = buffer.subarray(28);
  const decipher = crypto.createDecipheriv(
    "aes-256-gcm",
    getEncryptionKey(),
    iv,
  );
  decipher.setAuthTag(tag);

  return JSON.parse(
    Buffer.concat([decipher.update(encrypted), decipher.final()]).toString(
      "utf8",
    ),
  );
};

const createCookieOptions = (expires: Date) => ({
  expires,
  httpOnly: true,
  sameSite: "lax" as const,
  secure: true,
  path: "/",
});

const isLoopbackHost = (hostname) =>
  hostname === "localhost" || hostname === "127.0.0.1" || hostname === "::1";

const shouldUseLoopbackTlsBypass = (url) => {
  try {
    const parsedUrl = new URL(url);
    return (
      parsedUrl.protocol === "https:" && isLoopbackHost(parsedUrl.hostname)
    );
  } catch {
    return false;
  }
};

const createHeadersFromNodeResponse = (
  responseHeaders: http.IncomingHttpHeaders,
) => {
  const headers = new Headers();

  for (const [name, value] of Object.entries(responseHeaders)) {
    if (Array.isArray(value)) {
      for (const item of value) {
        headers.append(name, item);
      }
      continue;
    }

    if (typeof value === "string") {
      headers.set(name, value);
    }
  }

  return headers;
};

const requestWithLoopbackTlsBypass = (
  url: string,
  fetchOptions: NodeFetchOptions,
) =>
  new Promise<Response>((resolve, reject) => {
    const parsedUrl = new URL(url);
    const requestFactory = parsedUrl.protocol === "https:" ? https : http;
    const request = requestFactory.request(
      {
        hostname: parsedUrl.hostname,
        port: parsedUrl.port || `${parsedUrl.protocol === "https:" ? 443 : 80}`,
        path: `${parsedUrl.pathname}${parsedUrl.search}`,
        method: fetchOptions.method,
        headers: fetchOptions.headers,
        rejectUnauthorized: false,
      },
      (response) => {
        const chunks = [];

        response.on("data", (chunk) => {
          chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
        });

        response.on("end", () => {
          resolve(
            new Response(Buffer.concat(chunks), {
              status: response.statusCode || 500,
              statusText: response.statusMessage || "",
              headers: createHeadersFromNodeResponse(response.headers),
            }),
          );
        });

        response.on("error", reject);
      },
    );

    request.on("error", reject);

    if (fetchOptions.body) {
      request.write(fetchOptions.body);
    }

    request.end();
  });

export const fetchWithLoopbackTlsBypass = async (
  url: string,
  fetchOptions: NodeFetchOptions,
) => {
  try {
    return await fetch(url, fetchOptions);
  } catch (error) {
    if (!shouldUseLoopbackTlsBypass(url)) {
      throw error;
    }

    return await requestWithLoopbackTlsBypass(url, fetchOptions);
  }
};

const escapeHtml = (value = "") =>
  value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");

const normalizeReturnTo = (value) =>
  typeof value === "string" && value.startsWith("/") ? value : "/";

const getReturnTo = (request: Request) => {
  const url = new URL(request.url);
  return normalizeReturnTo(url.searchParams.get("returnTo"));
};

const getLoginPageHtml = ({
  errorMessage = "",
  email = "",
  returnTo = "/",
}: {
  errorMessage?: string;
  email?: string;
  returnTo?: string;
}) => `<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Sign in</title>
    <style>
      body { font-family: Segoe UI, sans-serif; margin: 0; background: #f7f4ee; color: #1f2937; }
      main { max-width: 28rem; margin: 4rem auto; padding: 2rem; background: #ffffff; border: 1px solid #d6d3d1; border-radius: 1rem; box-shadow: 0 10px 30px rgba(0,0,0,.08); }
      h1 { margin-top: 0; font-size: 1.875rem; }
      p { line-height: 1.5; }
      a { color: #0f766e; font-weight: 600; }
      label { display: block; margin-top: 1rem; font-weight: 600; }
      input { width: 100%; box-sizing: border-box; padding: .75rem; margin-top: .5rem; border: 1px solid #78716c; border-radius: .5rem; font: inherit; }
      button { margin-top: 1.5rem; width: 100%; padding: .85rem 1rem; border: 0; border-radius: .5rem; background: #0f766e; color: #fff; font: inherit; font-weight: 600; cursor: pointer; }
      button:focus, input:focus, a:focus { outline: 3px solid #f59e0b; outline-offset: 2px; }
      .error { margin-top: 1rem; padding: .75rem 1rem; border-radius: .5rem; background: #fef2f2; color: #991b1b; }
      .secondary-action { margin-top: 1.5rem; padding-top: 1.25rem; border-top: 1px solid #d6d3d1; }
    </style>
  </head>
  <body>
    <main id="auth-login-page" data-testid="auth-login-page" aria-labelledby="auth-login-page-title">
      <h1 id="auth-login-page-title" data-testid="auth-login-page-title">Sign in</h1>
      <p>Use your local System Uptime Tracker identity account.</p>
      ${errorMessage ? `<div id="auth-login-error" data-testid="auth-login-error" class="error" role="alert">${escapeHtml(errorMessage)}</div>` : ""}
      <form id="auth-login-form" data-testid="auth-login-form" method="post" action="/auth/login">
        <input type="hidden" name="returnTo" value="${escapeHtml(returnTo)}" />
        <label for="auth-login-email">Email</label>
        <input id="auth-login-email" data-testid="auth-login-email" name="email" type="email" autocomplete="username" required aria-required="true" value="${escapeHtml(email)}" />
        <label for="auth-login-password">Password</label>
        <input id="auth-login-password" data-testid="auth-login-password" name="password" type="password" autocomplete="current-password" required aria-required="true" />
        <button id="auth-login-submit" data-testid="auth-login-submit" type="submit">Sign in</button>
      </form>
      <p class="secondary-action"><a id="auth-create-user-link" data-testid="auth-create-user-link" href="/auth/create-user">Create User</a></p>
    </main>
  </body>
</html>`;

const getCreateUserPageHtml = ({
  errorMessage = "",
  email = "",
  displayName = "",
  returnTo = "/",
  isFirstTimeSetup = false,
}: {
  errorMessage?: string;
  email?: string;
  displayName?: string;
  returnTo?: string;
  isFirstTimeSetup?: boolean;
}) => `<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Create User</title>
    <style>
      body { font-family: Segoe UI, sans-serif; margin: 0; background: #f7f4ee; color: #1f2937; }
      main { max-width: 28rem; margin: 4rem auto; padding: 2rem; background: #ffffff; border: 1px solid #d6d3d1; border-radius: 1rem; box-shadow: 0 10px 30px rgba(0,0,0,.08); }
      h1 { margin-top: 0; font-size: 1.875rem; }
      p { line-height: 1.5; }
      a { color: #0f766e; font-weight: 600; }
      label { display: block; margin-top: 1rem; font-weight: 600; }
      input { width: 100%; box-sizing: border-box; padding: .75rem; margin-top: .5rem; border: 1px solid #78716c; border-radius: .5rem; font: inherit; }
      button { margin-top: 1.5rem; width: 100%; padding: .85rem 1rem; border: 0; border-radius: .5rem; background: #0f766e; color: #fff; font: inherit; font-weight: 600; cursor: pointer; }
      button:focus, input:focus, a:focus { outline: 3px solid #f59e0b; outline-offset: 2px; }
      .error { margin-top: 1rem; padding: .75rem 1rem; border-radius: .5rem; background: #fef2f2; color: #991b1b; }
      .secondary-action { margin-top: 1.5rem; padding-top: 1.25rem; border-top: 1px solid #d6d3d1; }
      .required-note { margin-top: 0.5rem; font-size: .875rem; color: #6b7280; }
      .req { color: #b91c1c; }
    </style>
  </head>
  <body>
    <main id="auth-create-user-page" data-testid="auth-create-user-page" aria-labelledby="auth-create-user-page-title">
      <h1 id="auth-create-user-page-title" data-testid="auth-create-user-page-title">${isFirstTimeSetup ? "First Time Setup" : "Create User"}</h1>
      ${errorMessage ? `<div id="auth-create-user-error" data-testid="auth-create-user-error" class="error" role="alert">${escapeHtml(errorMessage)}</div>` : ""}
      <p>${isFirstTimeSetup ? "Create the first administrator account for this System Uptime Tracker database." : "Create a local System Uptime Tracker identity account."}</p>
      <p class="required-note"><span class="req" aria-hidden="true">*</span> Required</p>
      <form id="auth-create-user-form" data-testid="auth-create-user-form" method="post" action="/auth/create-user">
        <input type="hidden" name="returnTo" value="${escapeHtml(returnTo)}" />
        ${isFirstTimeSetup ? `<input type="hidden" name="firstTimeSetup" value="true" />` : ""}
        <label for="auth-create-user-email">Email <span class="req" aria-hidden="true">*</span></label>
        <input id="auth-create-user-email" data-testid="auth-create-user-email" name="email" type="email" autocomplete="username" required aria-required="true" value="${escapeHtml(email)}" />
        <label for="auth-create-user-display-name">Display name</label>
        <input id="auth-create-user-display-name" data-testid="auth-create-user-display-name" name="displayName" type="text" autocomplete="name" value="${escapeHtml(displayName)}" />
        <label for="auth-create-user-password">Password <span class="req" aria-hidden="true">*</span></label>
        <input id="auth-create-user-password" data-testid="auth-create-user-password" name="password" type="password" autocomplete="new-password" required aria-required="true" />
        <label for="auth-create-user-confirm-password">Confirm password <span class="req" aria-hidden="true">*</span></label>
        <input id="auth-create-user-confirm-password" data-testid="auth-create-user-confirm-password" name="confirmPassword" type="password" autocomplete="new-password" required aria-required="true" />
        <button id="auth-create-user-submit" data-testid="auth-create-user-submit" type="submit">${isFirstTimeSetup ? "Create administrator" : "Create User"}</button>
      </form>
      <p class="secondary-action"><a id="auth-login-link" data-testid="auth-login-link" href="/auth/login">Back to sign in</a></p>
    </main>
  </body>
</html>`;

const createHtmlResponse = (html: string, status = 200) =>
  new Response(html, {
    status,
    headers: {
      "content-type": "text/html; charset=utf-8",
      "cache-control": "no-store",
    },
  });

const readJsonPayload = async <TPayload>(response: Response) => {
  try {
    return (await response.json()) as TPayload;
  } catch {
    return {} as TPayload;
  }
};

const extractValidationMessage = (payload: BootstrapPayload) => {
  if (typeof payload?.message === "string" && payload.message.trim()) {
    return payload.message.trim();
  }

  if (typeof payload?.detail === "string" && payload.detail.trim()) {
    return payload.detail.trim();
  }

  const firstError = Object.values(payload?.errors || {})
    .flat()
    .find((message) => typeof message === "string" && message.trim());

  return firstError?.trim();
};

const getIdentitySetupStatus = async () => {
  const setupStatusUrl = new URL(
    "api/identity/setup-status",
    getApiBaseUrl(),
  ).toString();

  try {
    const response = await fetchWithLoopbackTlsBypass(setupStatusUrl, {
      method: "GET",
      headers: {
        accept: "application/json",
      },
      cache: "no-store",
    });

    if (!response.ok) {
      return null;
    }

    return await readJsonPayload<SetupStatusPayload>(response);
  } catch {
    return null;
  }
};

class LocalIdentityAuthClient {
  async middleware() {
    return NextResponse.next();
  }

  async getSession() {
    const cookieStore = await cookies();
    const cookie = cookieStore.get(sessionCookieName);
    if (!cookie?.value) {
      return null;
    }

    try {
      const session = decrypt(cookie.value);
      if (!session.expiresAt || Date.parse(session.expiresAt) <= Date.now()) {
        return null;
      }

      return {
        user: session.user,
      };
    } catch {
      return null;
    }
  }

  async getAccessToken() {
    const cookieStore = await cookies();
    const cookie = cookieStore.get(sessionCookieName);
    if (!cookie?.value) {
      return undefined;
    }

    try {
      const session = decrypt(cookie.value);
      if (!session.expiresAt || Date.parse(session.expiresAt) <= Date.now()) {
        return undefined;
      }

      return session.accessToken;
    } catch {
      return undefined;
    }
  }

  async login(request: Request) {
    const existingSession = await this.getSession();
    const returnTo = getReturnTo(request);
    if (existingSession) {
      return NextResponse.redirect(new URL(returnTo, getAppBaseUrl()));
    }

    const setupStatus = await getIdentitySetupStatus();
    if (
      setupStatus?.isFirstTimeSetup === true &&
      setupStatus.canCreateFirstUser !== false
    ) {
      const setupUrl = new URL("/auth/create-user", getAppBaseUrl());
      setupUrl.searchParams.set("firstSetup", "true");
      setupUrl.searchParams.set("returnTo", returnTo);

      return NextResponse.redirect(setupUrl);
    }

    return createHtmlResponse(getLoginPageHtml({ returnTo }));
  }

  async submitLogin(request: Request) {
    const formData = await request.formData();
    const email = (formData.get("email") || "").toString().trim();
    const password = (formData.get("password") || "").toString();
    const returnTo = normalizeReturnTo(
      (formData.get("returnTo") || "/").toString(),
    );

    if (!email || !password) {
      return createHtmlResponse(
        getLoginPageHtml({
          errorMessage: "Email and password are required.",
          email,
          returnTo,
        }),
        400,
      );
    }

    return await this.signInWithLocalIdentity({
      email,
      password,
      returnTo,
      renderError: (errorMessage) =>
        getLoginPageHtml({
          errorMessage,
          email,
          returnTo,
        }),
    });
  }

  async createUser(request: Request) {
    const existingSession = await this.getSession();
    const returnTo = getReturnTo(request);
    if (existingSession) {
      return NextResponse.redirect(new URL(returnTo, getAppBaseUrl()));
    }

    const setupStatus = await getIdentitySetupStatus();
    const isFirstTimeSetup = setupStatus?.isFirstTimeSetup === true;

    return createHtmlResponse(
      getCreateUserPageHtml({
        isFirstTimeSetup,
        returnTo,
      }),
    );
  }

  async submitCreateUser(request: Request) {
    const formData = await request.formData();
    const email = (formData.get("email") || "").toString().trim();
    const displayName = (formData.get("displayName") || "").toString().trim();
    const password = (formData.get("password") || "").toString();
    const confirmPassword = (formData.get("confirmPassword") || "").toString();
    const returnTo = normalizeReturnTo(
      (formData.get("returnTo") || "/").toString(),
    );
    const setupStatus = await getIdentitySetupStatus();
    const isFirstTimeSetup =
      setupStatus?.isFirstTimeSetup === true ||
      (formData.get("firstTimeSetup") || "").toString() === "true";

    const renderCreateUserError = (errorMessage: string, status = 400) =>
      createHtmlResponse(
        getCreateUserPageHtml({
          errorMessage,
          email,
          displayName,
          isFirstTimeSetup,
          returnTo,
        }),
        status,
      );

    if (!email || !password || !confirmPassword) {
      return renderCreateUserError("Email and password are required.");
    }

    if (password !== confirmPassword) {
      return renderCreateUserError("The passwords do not match.");
    }

    const bootstrapUrl = new URL(
      "api/identity/self-create",
      getApiBaseUrl(),
    ).toString();
    const requestBody = JSON.stringify({
      email,
      password,
      displayName,
    });

    let response;
    try {
      response = await fetchWithLoopbackTlsBypass(bootstrapUrl, {
        method: "POST",
        headers: {
          accept: "application/json",
          "content-type": "application/json",
        },
        body: requestBody,
        cache: "no-store",
      });
    } catch {
      const traceId = createTraceId();
      return renderCreateUserError(
        appendTraceId(
          "The user creation service could not be reached.",
          traceId,
        ),
        502,
      );
    }

    const payload = await readJsonPayload<BootstrapPayload>(response);

    if (!response.ok) {
      const traceId =
        extractTraceId(response.headers) ||
        extractTraceId(payload) ||
        createTraceId();
      const detail =
        extractValidationMessage(payload) ||
        appendTraceId("The user creation request was rejected.", traceId);

      return renderCreateUserError(detail, response.status);
    }

    return await this.signInWithLocalIdentity({
      email,
      password,
      returnTo,
      renderError: (errorMessage) =>
        getCreateUserPageHtml({
          errorMessage,
          email,
          displayName,
          isFirstTimeSetup,
          returnTo,
        }),
    });
  }

  private async signInWithLocalIdentity({
    email,
    password,
    returnTo,
    renderError,
  }: {
    email: string;
    password: string;
    returnTo: string;
    renderError: (errorMessage: string) => string;
  }) {
    const loginUrl = new URL(
      "api/identity/login?useCookies=false&useSessionCookies=false",
      getApiBaseUrl(),
    ).toString();
    const requestBody = JSON.stringify({ email, password });

    let response;
    try {
      response = await fetchWithLoopbackTlsBypass(loginUrl, {
        method: "POST",
        headers: {
          accept: "application/json",
          "content-type": "application/json",
        },
        body: requestBody,
        cache: "no-store",
      });
    } catch {
      const traceId = createTraceId();
      return createHtmlResponse(
        renderError(
          appendTraceId("The sign-in service could not be reached.", traceId),
        ),
        502,
      );
    }

    const payload = await readJsonPayload<LoginPayload>(response);

    if (!response.ok) {
      const traceId =
        extractTraceId(response.headers) ||
        extractTraceId(payload) ||
        createTraceId();
      const detail =
        typeof payload?.detail === "string" && payload.detail.trim().length > 0
          ? payload.detail.trim()
          : appendTraceId("The sign-in request was rejected.", traceId);

      return createHtmlResponse(renderError(detail), response.status);
    }

    const expiresIn =
      Number(payload.expiresIn) || defaultSessionLifetimeSeconds;
    const expires = new Date(Date.now() + expiresIn * 1000);
    const responseRedirect = NextResponse.redirect(
      new URL(returnTo, getAppBaseUrl()),
    );

    responseRedirect.cookies.set(
      sessionCookieName,
      encrypt({
        accessToken: payload.accessToken,
        expiresAt: expires.toISOString(),
        user: {
          sub: email,
          name: email,
          email,
        },
      }),
      createCookieOptions(expires),
    );

    return responseRedirect;
  }

  async callback() {
    return NextResponse.redirect(new URL("/auth/login", getAppBaseUrl()));
  }

  async logout() {
    const response = NextResponse.redirect(new URL("/", getAppBaseUrl()));
    response.cookies.delete(sessionCookieName);
    if (process.env.IMPERSONATING_COOKIE) {
      response.cookies.delete(process.env.IMPERSONATING_COOKIE);
      response.cookies.delete(`${process.env.IMPERSONATING_COOKIE}-data`);
    }

    return response;
  }
}

export const auth = (_caller?: string) => {
  if (!authInstance) {
    authInstance = new LocalIdentityAuthClient();
  }

  return authInstance;
};
