import http from "node:http";
import https from "node:https";
import { Readable } from "node:stream";

import { cookies } from "next/headers";

import { auth } from "@/utils/auth/auth";
import { decrypt } from "@/utils/encryption";
import {
  createTraceId,
  createTraceableError,
  extractTraceId,
  getPublicErrorDetail,
} from "@/utils/error-reference";
import { createLogger } from "@/utils/logger-server";
import { sanitizeMultipartDispositionValue } from "@/utils/multipart";

const apiUrl = process.env.API_BASE_URL;
const impersonateCookie = process.env.IMPERSONATING_COOKIE;

type ProxyFetchOptions = RequestInit & {
  headers: Record<string, string>;
  next?: {
    revalidate: number;
  };
  body?: BodyInit;
};

type AntiforgeryTokenResponse = {
  requestToken: string;
  headerName: string;
};

const getApiBaseUrl = () => {
  if (!apiUrl) {
    throw new Error("API_BASE_URL must be configured.");
  }

  return apiUrl.endsWith("/") ? apiUrl : `${apiUrl}/`;
};

const buildApiUrl = (url: string) => new URL(url, getApiBaseUrl()).toString();

const buildFetchObj = (
  method: string,
  token?: string,
  impersonateValue?: string,
  cacheMinutes = 0,
): ProxyFetchOptions => {
  const fetchObj: ProxyFetchOptions = {
    method,
    cache: "no-store",
    headers: {
      "content-type": "application/json",
      accept: "application/json",
    },
  };

  if (impersonateValue && impersonateCookie) {
    fetchObj.headers[impersonateCookie] = impersonateValue;
  }

  if (token) {
    const header = `Bearer ${token}`;
    if (process.env.CONSOLE_ACCESSTOKEN === "true") {
      console.warn(
        "[ApiRouteProxy] Access token logging is enabled, but the token value is intentionally suppressed.",
      );
    }

    fetchObj.headers.authorization = header;
  }

  if (!Number.isNaN(cacheMinutes) && cacheMinutes > 0) {
    fetchObj.cache = undefined;
    fetchObj.next = {
      revalidate: cacheMinutes,
    };
  }

  return fetchObj;
};

const getTokenToSetAsBearer = async (): Promise<string | undefined> => {
  let accessToken: string | { token?: string } | undefined;
  try {
    accessToken = await auth("route").getAccessToken();
  } catch {
    accessToken = undefined;
  }

  return typeof accessToken === "object" ? accessToken?.token : accessToken;
};

const getResponseTraceId = (response: Response, payload?: unknown) =>
  extractTraceId(response?.headers) || extractTraceId(payload);

const getRequestCookieHeader = async () => {
  const requestCookies = await cookies();
  const cookiePairs =
    typeof requestCookies.getAll === "function"
      ? requestCookies.getAll().map(({ name, value }) => `${name}=${value}`)
      : [];

  return cookiePairs.length > 0 ? cookiePairs.join("; ") : undefined;
};

const getSetCookieHeaders = (headers: Headers) => {
  const getSetCookie = (
    headers as Headers & {
      getSetCookie?: () => string[];
    }
  ).getSetCookie;

  if (typeof getSetCookie === "function") {
    return getSetCookie.call(headers);
  }

  const rawHeader = headers.get("set-cookie");
  return rawHeader ? rawHeader.split(/,(?=[^;,]+=)/) : [];
};

const mergeCookieHeaders = (...cookieHeaders: Array<string | undefined>) => {
  const cookiesByName = new Map<string, string>();

  for (const header of cookieHeaders) {
    if (!header) {
      continue;
    }

    for (const segment of header.split(";")) {
      const trimmedSegment = segment.trim();
      if (!trimmedSegment) {
        continue;
      }

      const separatorIndex = trimmedSegment.indexOf("=");
      if (separatorIndex <= 0) {
        continue;
      }

      const name = trimmedSegment.slice(0, separatorIndex).trim();
      const value = trimmedSegment.slice(separatorIndex + 1).trim();
      if (!name) {
        continue;
      }

      cookiesByName.set(name, value);
    }
  }

  if (cookiesByName.size === 0) {
    return undefined;
  }

  return Array.from(cookiesByName.entries())
    .map(([name, value]) => `${name}=${value}`)
    .join("; ");
};

const getResponseCookieHeader = (response: Response) => {
  const responseCookies = getSetCookieHeaders(response.headers)
    .map((cookie) => cookie.split(";")[0]?.trim())
    .filter((cookie): cookie is string => Boolean(cookie));

  return responseCookies.length > 0 ? responseCookies.join("; ") : undefined;
};

const isLoopbackHost = (hostname: string) =>
  hostname === "localhost" || hostname === "127.0.0.1" || hostname === "::1";

const shouldUseLoopbackTlsBypass = (url: string) => {
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
  fetchObj: ProxyFetchOptions,
) =>
  new Promise<Response>((resolve, reject) => {
    const parsedUrl = new URL(url);
    const requestFactory = parsedUrl.protocol === "https:" ? https : http;
    const request = requestFactory.request(
      {
        hostname: parsedUrl.hostname,
        port: parsedUrl.port || (parsedUrl.protocol === "https:" ? 443 : 80),
        path: `${parsedUrl.pathname}${parsedUrl.search}`,
        method: fetchObj.method,
        headers: fetchObj.headers,
        rejectUnauthorized: false,
      },
      (response) => {
        const responseBody =
          response.statusCode === 204 || response.statusCode === 304
            ? null
            : (Readable.toWeb(response) as ReadableStream);

        resolve(
          new Response(responseBody, {
            status: response.statusCode || 500,
            statusText: response.statusMessage || "",
            headers: createHeadersFromNodeResponse(response.headers),
          }),
        );

        response.on("error", reject);
      },
    );

    request.on("error", reject);

    if (fetchObj.body) {
      request.write(fetchObj.body);
    }

    request.end();
  });

const fetchWithLoopbackTlsBypass = async (
  url: string,
  fetchObj: ProxyFetchOptions,
) => {
  try {
    return await fetch(url, fetchObj);
  } catch (error) {
    if (!shouldUseLoopbackTlsBypass(url)) {
      throw error;
    }

    return await requestWithLoopbackTlsBypass(url, fetchObj);
  }
};

const parseJsonResponse = async (response: Response) => response.json();

const parseUpstreamErrorPayload = async (response: Response) => {
  if (!response.headers.get("content-type")?.includes("application/json")) {
    return undefined;
  }

  return parseJsonResponse(response);
};

const getImpersonationValue = async (
  log: Awaited<ReturnType<typeof createLogger>>,
  url: string,
  method: string,
) => {
  const myCookies = await cookies();
  const cookie = myCookies.get(impersonateCookie);
  const cookieValue = cookie?.value;

  if (!cookieValue) {
    return undefined;
  }

  try {
    return decrypt(cookieValue);
  } catch {
    await log.warn(
      `Failed to decrypt impersonation cookie for server API ${method}`,
      {
        url,
      },
    );
    return undefined;
  }
};

const getAntiforgeryContext = async ({
  log,
  token,
  impersonateValue,
  requestCookieHeader,
}: {
  log: Awaited<ReturnType<typeof createLogger>>;
  token?: string;
  impersonateValue?: string;
  requestCookieHeader?: string;
}) => {
  if (!requestCookieHeader) {
    return undefined;
  }

  const antiforgeryUrl = buildApiUrl("api/auth/antiforgery-token");
  const antiforgeryFetchObj = buildFetchObj("GET", token, impersonateValue, 0);
  antiforgeryFetchObj.headers.cookie = requestCookieHeader;

  let response;
  try {
    response = await fetchWithLoopbackTlsBypass(
      antiforgeryUrl,
      antiforgeryFetchObj,
    );
  } catch (error) {
    const traceId = createTraceId();
    await log.error(
      "Antiforgery token request failed before receiving a response",
      error,
      {
        traceId,
        url: antiforgeryUrl,
      },
    );

    throw createTraceableError(
      "The server request could not be completed.",
      traceId,
      {
        statusCode: 502,
      },
    );
  }

  if (response.status === 401) {
    const traceId = getResponseTraceId(response);
    await log.warn("Antiforgery token request returned unauthorized", {
      url: antiforgeryUrl,
      ...(traceId ? { traceId } : {}),
    });

    return "unauthorized" as const;
  }

  if (!response.ok) {
    const traceId = getResponseTraceId(response) || createTraceId();
    await log.error("Antiforgery token request failed", {
      url: antiforgeryUrl,
      statusCode: response.status,
      statusText: response.statusText,
      traceId,
    });

    throw createTraceableError(
      getPublicErrorDetail(response.status, traceId),
      traceId,
      {
        statusCode: response.status,
        statusText: response.statusText,
      },
    );
  }

  let payload;
  try {
    payload = (await response.json()) as AntiforgeryTokenResponse;
  } catch (error) {
    const traceId = getResponseTraceId(response) || createTraceId();
    await log.error("Antiforgery token response parsing failed", error, {
      url: antiforgeryUrl,
      traceId,
    });

    throw createTraceableError(
      "The server returned an unreadable response.",
      traceId,
    );
  }

  if (!payload?.requestToken || !payload?.headerName) {
    const traceId = getResponseTraceId(response, payload) || createTraceId();
    await log.error("Antiforgery token response was incomplete", {
      url: antiforgeryUrl,
      traceId,
    });

    throw createTraceableError(
      "The server returned an unexpected response.",
      traceId,
    );
  }

  return {
    headerName: payload.headerName,
    requestToken: payload.requestToken,
    cookieHeader: mergeCookieHeaders(
      requestCookieHeader,
      getResponseCookieHeader(response),
    ),
  };
};

const serverApiWrite = async ({
  method,
  url,
  body,
}: {
  method: "POST" | "PUT" | "DELETE";
  url: string;
  body?: unknown;
}) => {
  const log = await createLogger(`ServerApi${method}`);
  const token = await getTokenToSetAsBearer();
  const fullUrl = buildApiUrl(url);
  const impersonateValue = await getImpersonationValue(log, url, method);
  const requestCookieHeader = await getRequestCookieHeader();
  const fetchObj = buildFetchObj(method, token, impersonateValue, 0);

  const antiforgeryContext = await getAntiforgeryContext({
    log,
    token,
    impersonateValue,
    requestCookieHeader,
  });

  if (antiforgeryContext === "unauthorized") {
    return "unauthorized" as const;
  }

  if (antiforgeryContext) {
    fetchObj.headers[antiforgeryContext.headerName] =
      antiforgeryContext.requestToken;

    if (antiforgeryContext.cookieHeader) {
      fetchObj.headers.cookie = antiforgeryContext.cookieHeader;
    }
  }

  if (body !== undefined) {
    fetchObj.body = JSON.stringify(body);
  }

  let response;
  try {
    response = await fetchWithLoopbackTlsBypass(fullUrl, fetchObj);
  } catch (error) {
    const traceId = createTraceId();
    await log.error(
      `Server API ${method} request failed before receiving a response`,
      error,
      {
        url,
        traceId,
      },
    );
    throw createTraceableError(
      "The server request could not be completed.",
      traceId,
      {
        statusCode: 502,
      },
    );
  }

  if (response.status === 401) {
    const traceId = getResponseTraceId(response);
    await log.warn(`Server API ${method} request returned unauthorized`, {
      url,
      ...(traceId ? { traceId } : {}),
    });
    return "unauthorized" as const;
  }

  return response;
};

const serverApiPost = async ({ url, body }: { url: string; body?: unknown }) =>
  serverApiWrite({ method: "POST", url, body });

const serverApiPut = async ({ url, body }: { url: string; body?: unknown }) =>
  serverApiWrite({ method: "PUT", url, body });

const serverApiDelete = async ({ url }: { url: string }) =>
  serverApiWrite({ method: "DELETE", url });

const buildMultipartBody = async (
  formData: FormData,
): Promise<{ body: Uint8Array; contentType: string }> => {
  const boundary = `----FormBoundary${Date.now()}${Math.random().toString(36).slice(2)}`;
  const parts: Buffer[] = [];

  for (const [key, value] of formData.entries()) {
    const sanitizedKey = sanitizeMultipartDispositionValue(key);

    if (value instanceof File) {
      const fileBuffer = Buffer.from(await value.arrayBuffer());
      const sanitizedFileName = sanitizeMultipartDispositionValue(value.name);
      const sanitizedContentType = sanitizeMultipartDispositionValue(
        value.type || "application/octet-stream",
      );
      parts.push(
        Buffer.from(
          `--${boundary}\r\nContent-Disposition: form-data; name="${sanitizedKey}"; filename="${sanitizedFileName}"\r\nContent-Type: ${sanitizedContentType}\r\n\r\n`,
        ),
      );
      parts.push(fileBuffer);
      parts.push(Buffer.from("\r\n"));
    } else {
      parts.push(
        Buffer.from(
          `--${boundary}\r\nContent-Disposition: form-data; name="${sanitizedKey}"\r\n\r\n${String(value)}\r\n`,
        ),
      );
    }
  }

  parts.push(Buffer.from(`--${boundary}--\r\n`));
  return {
    body: new Uint8Array(Buffer.concat(parts)),
    contentType: `multipart/form-data; boundary=${boundary}`,
  };
};

const serverApiMultipart = async ({
  url,
  formData,
}: {
  url: string;
  formData: FormData;
}) => {
  const log = await createLogger("ServerApiMultipart");
  const token = await getTokenToSetAsBearer();
  const fullUrl = buildApiUrl(url);
  const impersonateValue = await getImpersonationValue(log, url, "POST");
  const requestCookieHeader = await getRequestCookieHeader();

  const { body, contentType } = await buildMultipartBody(formData);

  const headers: Record<string, string> = {
    "content-type": contentType,
    accept: "application/json",
  };

  if (token) {
    headers.authorization = `Bearer ${token}`;
  }

  if (impersonateValue && impersonateCookie) {
    headers[impersonateCookie] = impersonateValue;
  }

  const antiforgeryContext = await getAntiforgeryContext({
    log,
    token,
    impersonateValue,
    requestCookieHeader,
  });

  if (antiforgeryContext === "unauthorized") {
    return "unauthorized" as const;
  }

  if (antiforgeryContext) {
    headers[antiforgeryContext.headerName] = antiforgeryContext.requestToken;
    if (antiforgeryContext.cookieHeader) {
      headers.cookie = antiforgeryContext.cookieHeader;
    }
  }

  let response: Response;
  try {
    response = await fetchWithLoopbackTlsBypass(fullUrl, {
      method: "POST",
      cache: "no-store",
      headers,
      body: body as BodyInit,
    });
  } catch (error) {
    const traceId = createTraceId();
    await log.error(
      "Server API multipart POST failed before receiving a response",
      error,
      { url, traceId },
    );
    throw createTraceableError(
      "The server request could not be completed.",
      traceId,
      {
        statusCode: 502,
      },
    );
  }

  if (response.status === 401) {
    const traceId = getResponseTraceId(response);
    await log.warn("Server API multipart POST returned unauthorized", {
      url,
      ...(traceId ? { traceId } : {}),
    });
    return "unauthorized" as const;
  }

  return response;
};

const serverApiGet = async ({
  url,
  cacheMinutes = 0,
  expectedStatusCodes = [],
}: {
  url: string;
  cacheMinutes?: number;
  expectedStatusCodes?: number[];
}) => {
  const log = await createLogger("ServerApiGet");
  const token = await getTokenToSetAsBearer();
  const fullUrl = buildApiUrl(url);
  const impersonateValue = await getImpersonationValue(log, url, "GET");
  const fetchObj = buildFetchObj("GET", token, impersonateValue, cacheMinutes);
  let response;

  try {
    response = await fetchWithLoopbackTlsBypass(fullUrl, fetchObj);
  } catch (error) {
    const traceId = createTraceId();
    await log.error(
      "Server API GET request failed before receiving a response",
      error,
      {
        url,
        traceId,
      },
    );

    throw createTraceableError(
      "The server request could not be completed.",
      traceId,
      {
        statusCode: 502,
      },
    );
  }

  if (response.status === 401) {
    const traceId = getResponseTraceId(response);
    await log.warn("Server API GET request returned unauthorized", {
      url,
      ...(traceId ? { traceId } : {}),
    });
    return "unauthorized";
  }

  if (!response.ok) {
    if (expectedStatusCodes.includes(response.status)) {
      return response;
    }

    let errorPayload;

    try {
      errorPayload = await parseUpstreamErrorPayload(response);
    } catch (error) {
      const traceId = getResponseTraceId(response) || createTraceId();
      await log.error(
        "Failed to parse server API GET JSON error response",
        error,
        {
          url,
          statusCode: response.status,
          traceId,
        },
      );

      throw createTraceableError(
        getPublicErrorDetail(response.status, traceId),
        traceId,
        {
          statusCode: response.status,
          statusText: response.statusText,
        },
      );
    }

    const traceId =
      getResponseTraceId(response, errorPayload) || createTraceId();
    await log.error("Server API GET request failed", {
      url,
      statusCode: response.status,
      statusText: response.statusText,
      traceId,
    });

    throw createTraceableError(
      getPublicErrorDetail(response.status, traceId),
      traceId,
      {
        statusCode: response.status,
        statusText: response.statusText,
      },
    );
  }

  return response;
};

export {
  serverApiGet,
  serverApiPost,
  serverApiPut,
  serverApiDelete,
  serverApiMultipart,
};
