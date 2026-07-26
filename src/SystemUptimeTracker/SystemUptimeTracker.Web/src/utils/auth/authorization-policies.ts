import "server-only";

import {
  auth,
  fetchWithLoopbackTlsBypass,
  getApiBaseUrl,
} from "@/utils/auth/auth";
import {
  createTraceId,
  createTraceableError,
  extractTraceId,
} from "@/utils/error-reference";

export type AuthorizationPolicies = {
  canManageUsers: boolean;
};

const defaultAuthorizationPolicies: AuthorizationPolicies = {
  canManageUsers: false,
};

const authorizationPolicyKeys = Object.keys(
  defaultAuthorizationPolicies,
) as Array<keyof AuthorizationPolicies>;

const isAuthorizationPolicies = (
  value: unknown,
): value is AuthorizationPolicies => {
  if (!value || typeof value !== "object") {
    return false;
  }

  return authorizationPolicyKeys.every(
    (key) =>
      Object.hasOwn(value, key) &&
      typeof (value as Record<string, unknown>)[key] === "boolean",
  );
};

async function fetchAuthorizationPoliciesInternal() {
  const accessToken = await auth("authorization-policies").getAccessToken();
  if (!accessToken) {
    return defaultAuthorizationPolicies;
  }

  const url = new URL("api/auth/permissions", getApiBaseUrl()).toString();

  let response: Response;
  try {
    response = await fetchWithLoopbackTlsBypass(url, {
      method: "GET",
      headers: {
        accept: "application/json",
        authorization: `Bearer ${accessToken}`,
      },
      cache: "no-store",
    });
  } catch {
    throw createTraceableError(
      "The authorization service could not be reached.",
      createTraceId(),
    );
  }

  let payload: unknown;
  try {
    payload = await response.json();
  } catch {
    payload = undefined;
  }

  if (response.status === 401 || response.status === 403) {
    return defaultAuthorizationPolicies;
  }

  if (!response.ok) {
    throw createTraceableError(
      "The authorization policy response was rejected.",
      extractTraceId(response.headers) ||
        extractTraceId(payload) ||
        createTraceId(),
      { statusCode: response.status },
    );
  }

  if (!isAuthorizationPolicies(payload)) {
    throw createTraceableError(
      "The authorization policy response was invalid.",
      extractTraceId(response.headers) ||
        extractTraceId(payload) ||
        createTraceId(),
    );
  }

  return payload;
}

export async function getAuthorizationPolicies() {
  return fetchAuthorizationPoliciesInternal();
}

export async function getAuthorizationPoliciesOrDefault() {
  try {
    return await fetchAuthorizationPoliciesInternal();
  } catch {
    return defaultAuthorizationPolicies;
  }
}

export function hasAdminRouteAccess(policies: AuthorizationPolicies) {
  return policies.canManageUsers;
}
