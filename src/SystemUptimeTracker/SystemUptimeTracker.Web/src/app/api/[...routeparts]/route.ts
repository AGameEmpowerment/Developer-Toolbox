import http from "node:http";
import https from "node:https";

import { cookies } from "next/headers";

import getZodDefinitionForRequest from "../get-zod-definition-for-request";
import { auth } from "@/utils/auth/auth";
import { decrypt } from "@/utils/encryption";
import {
  buildPublicErrorPayload,
  createTraceId,
  extractTraceId,
  TRACE_ID_HEADER_NAME,
} from "@/utils/error-reference";
import { createLogger } from "@/utils/logger-server";

const apiUrl = process.env.API_BASE_URL;
const impersonateCookie = process.env.IMPERSONATING_COOKIE;

type ProxyFetchOptions = RequestInit & {
  headers: Record<string, string>;
  next?: {
    revalidate: number;
  };
  body?: BodyInit;
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

const getDataFromRequest = async (request: Request, contentType?: string) => {
  let json: unknown;
  let formData: Record<string, FormDataEntryValue> | undefined;

  if (contentType?.includes("application/json")) {
    json = await request.json();
  } else {
    const form = await request.formData();
    formData = Object.fromEntries(form);
  }

  return formData || json;
};

const validateAndJsonifyData = (
  method: string,
  routeParts: string[],
  useData: unknown,
) => {
  const zodValidator = getZodDefinitionForRequest(method, routeParts);
  if (zodValidator && useData) {
    try {
      zodValidator.parse(useData);
    } catch {
      throw new Error("Invalid data format");
    }
  }

  return JSON.stringify(useData);
};

const getResponseTraceId = (response: Response, payload?: unknown) =>
  extractTraceId(response?.headers) || extractTraceId(payload);

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

const createHeadersFromNodeResponse = (responseHeaders) => {
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

    if (fetchObj.body) {
      request.write(fetchObj.body);
    }

    request.end();
  });

// Next server components talk to local Aspire resources over loopback HTTPS during
// development. When Node cannot validate the local ASP.NET dev certificate, retry
// only those loopback requests with a scoped TLS bypass instead of weakening all fetches.
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

const buildJsonHeaders = (traceId?: string) => {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
  };

  if (traceId) {
    headers[TRACE_ID_HEADER_NAME] = traceId;
  }

  return headers;
};

const buildJsonResponse = (
  payload: unknown,
  status: number,
  statusText = "",
  traceId = "",
) =>
  new Response(JSON.stringify(payload), {
    status,
    ...(statusText ? { statusText } : {}),
    headers: buildJsonHeaders(traceId),
  });

const buildProxyErrorResponse = ({
  statusCode,
  statusText = "",
  title = "",
  detail = "",
  traceId,
}) =>
  buildJsonResponse(
    buildPublicErrorPayload({
      statusCode,
      title,
      detail,
      traceId,
    }),
    statusCode,
    statusText,
    traceId,
  );

const parseJsonResponse = async (response: Response) => response.json();

const parseUpstreamErrorPayload = async (response: Response) => {
  if (!response.headers.get("content-type")?.includes("application/json")) {
    return undefined;
  }

  return parseJsonResponse(response);
};

const getJoinedRoute = (routeParts: string[] = []) => routeParts.join("/");

/*NOTE:
Any updates to Action should be reflected in serverApiGet as well!!!!!!
*/
const Action = async (
  request: Request,
  { params }: { params: Promise<{ routeparts: string[] }> },
) => {
  const log = await createLogger("ApiRouteProxy");
  const token = await getTokenToSetAsBearer();
  const { routeparts } = await params;
  const route = getJoinedRoute(routeparts);
  const { method, headers } = request;
  const contentType = headers.get("content-type");
  const canHaveBody = ["POST", "PUT", "PATCH"].includes(method);

  const myCookies = await cookies();
  const cookie = myCookies.get(impersonateCookie);
  const cookieValue = cookie?.value;
  let impersonateValue: string | undefined;

  if (cookieValue) {
    try {
      impersonateValue = decrypt(cookieValue);
    } catch {
      await log.warn("Failed to decrypt impersonation cookie", {
        route,
      });
      impersonateValue = undefined;
    }
  }

  const fetchObj = buildFetchObj(method, token, impersonateValue, 0);

  if (canHaveBody) {
    try {
      const useData = await getDataFromRequest(request, contentType);
      fetchObj.body = validateAndJsonifyData(method, routeparts, useData);
    } catch (error) {
      const traceId = createTraceId();
      const errorMessage =
        error instanceof Error && error.message
          ? error.message
          : "Invalid request body";

      await log.warn("API proxy request body validation failed", {
        method,
        route,
        errorMessage,
        traceId,
      });

      return buildProxyErrorResponse({
        statusCode: 400,
        statusText: "Bad Request",
        title: "Invalid request body.",
        detail: "The request body could not be processed.",
        traceId,
      });
    }
  }

  const queryString = request.url.includes("?")
    ? request.url.split("?")[1]
    : "";
  const url = buildApiUrl(
    queryString ? `api/${route}?${queryString}` : `api/${route}`,
  );

  let response;
  try {
    response = await fetchWithLoopbackTlsBypass(url, fetchObj);
  } catch (error) {
    const traceId = createTraceId();
    await log.error(
      "Upstream API request failed before receiving a response",
      error,
      {
        method,
        route,
        traceId,
        url,
      },
    );

    return buildProxyErrorResponse({
      statusCode: 502,
      statusText: "Bad Gateway",
      detail: "The upstream service could not be reached.",
      traceId,
    });
  }

  if (!response.ok) {
    let errorPayload;

    try {
      errorPayload = await parseUpstreamErrorPayload(response);
    } catch (error) {
      const traceId = getResponseTraceId(response) || createTraceId();
      await log.error(
        "Failed to parse upstream API JSON error response",
        error,
        {
          method,
          route,
          statusCode: response.status,
          traceId,
        },
      );

      return buildProxyErrorResponse({
        statusCode: response.status,
        statusText: response.statusText,
        traceId,
      });
    }

    const traceId =
      getResponseTraceId(response, errorPayload) || createTraceId();
    await log.warn("Upstream API request returned non-success status", {
      method,
      route,
      statusCode: response.status,
      statusText: response.statusText,
      traceId,
    });

    return buildProxyErrorResponse({
      statusCode: response.status,
      statusText: response.statusText,
      traceId,
    });
  }

  if (!response.headers.get("content-type")?.includes("application/json")) {
    const traceId = getResponseTraceId(response) || createTraceId();
    await log.error("Unexpected upstream API content type", {
      method,
      route,
      contentType: response.headers.get("content-type") || "unknown",
      traceId,
    });

    return buildProxyErrorResponse({
      statusCode: 500,
      statusText: "Internal Server Error",
      traceId,
    });
  }

  try {
    const data = await parseJsonResponse(response);
    const traceId = getResponseTraceId(response, data);

    return buildJsonResponse(
      data,
      response.status,
      response.statusText,
      traceId,
    );
  } catch (error) {
    const traceId = getResponseTraceId(response) || createTraceId();
    await log.error("Failed to parse upstream API JSON response", error, {
      method,
      route,
      statusCode: response.status,
      traceId,
    });

    return buildProxyErrorResponse({
      statusCode: 500,
      statusText: "Internal Server Error",
      traceId,
    });
  }
};

const GET = Action;
const POST = Action;
const PUT = Action;
const DELETE = Action;
const PATCH = Action;

export { GET, POST, PUT, DELETE, PATCH };
