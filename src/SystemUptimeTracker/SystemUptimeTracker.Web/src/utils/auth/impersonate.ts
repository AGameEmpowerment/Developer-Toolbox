"use client";

import { getCookie } from "cookies-next";

import { createClientLogger } from "@/utils/logger-client";

const log = createClientLogger("ImpersonationClient");

const getImpersonatingData = () => {
  const baseCookie = process.env.NEXT_PUBLIC_IMPERSONATING_COOKIE;
  const fullCookie = `${baseCookie}-data`;

  const cookieValue = getCookie(fullCookie);
  const normalizedCookieValue =
    typeof cookieValue === "string" ? cookieValue : undefined;

  if (!normalizedCookieValue) {
    return null;
  }

  try {
    return JSON.parse(normalizedCookieValue);
  } catch (error) {
    void log.warn("Failed to parse impersonation cookie data", {
      errorName: error?.name || "UnknownError",
    });
    return null;
  }
};

const impersonate = async (identifier) => {
  const response = await fetch(`/api/auth/impersonate/${identifier}`, {
    method: "GET",
  });
  //200 response means we allowed impersonate and the value is in the cookie
  if (response?.status !== 200) {
    await log.warn("Impersonation request was rejected", {
      identifier,
      statusCode: response?.status ?? 0,
    });
  }
  return response?.status === 200;
};
const clearImpersonate = async () => {
  const response = await fetch(`/api/auth/clearImpersonate`, { method: "GET" });
  if (response?.status !== 200) {
    await log.warn("Failed to clear impersonation session", {
      statusCode: response?.status ?? 0,
    });
  }
  return response?.status === 200;
};

export { impersonate, clearImpersonate, getImpersonatingData };
