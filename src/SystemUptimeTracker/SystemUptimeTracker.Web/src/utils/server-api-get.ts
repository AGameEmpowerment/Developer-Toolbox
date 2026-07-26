"use server";

import redirectToLogin from "../utils/auth/redirect-to-login";
import {
  createTraceId,
  createTraceableError,
  extractTraceId,
} from "@/utils/error-reference";
import { createLogger } from "@/utils/logger-server";
import { serverApiGet } from "@/utils/server-api";

const getResponseTraceId = (response: Response, payload?: unknown) =>
  extractTraceId(response?.headers) || extractTraceId(payload);

const serverApiGetAsJson = async (
  paramsObj: {
    url: string;
    cacheMinutes?: number;
    expectedStatusCodes?: number[];
    defaultData?: unknown;
    zodSchema?: {
      safeParse: (data: unknown) => { success: boolean };
    };
  },
  _caller?: string,
) => {
  const log = await createLogger("ServerApiGetAsJson");
  const data = await serverApiGet(paramsObj);

  if (data === "unauthorized") {
    await log.warn("Server API JSON request returned unauthorized", {
      url: paramsObj?.url,
    });
    await redirectToLogin();
    return paramsObj.defaultData;
  }

  let jsonData;
  try {
    jsonData = await data.json();
  } catch (error) {
    const traceId = getResponseTraceId(data) || createTraceId();
    await log.error("Server API JSON response parsing failed", error, {
      url: paramsObj?.url,
      traceId,
    });
    throw createTraceableError(
      "The server returned an unreadable response.",
      traceId,
    );
  }

  if (paramsObj.zodSchema) {
    const result = paramsObj.zodSchema.safeParse(jsonData);

    if (!result.success) {
      const traceId = getResponseTraceId(data, jsonData) || createTraceId();
      await log.error("Server API JSON schema validation failed", {
        url: paramsObj?.url,
        traceId,
      });
      throw createTraceableError(
        "The server returned an unexpected response.",
        traceId,
      );
    }
  }

  return jsonData;
};

export { serverApiGetAsJson };
