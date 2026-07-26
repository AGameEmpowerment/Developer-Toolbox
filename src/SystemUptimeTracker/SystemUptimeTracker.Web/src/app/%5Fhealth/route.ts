import { NextResponse } from "next/server";

const API_HEALTH_PATH = "_health";
const API_HEALTH_TIMEOUT_MS = 5000;

type ApiHealthResult = {
  status: "healthy" | "unhealthy";
  url?: string;
  statusCode?: number;
  error?: string;
};

const getApiBaseUrl = () => {
  const apiUrl = process.env.API_BASE_URL;

  if (!apiUrl) {
    throw new Error("API_BASE_URL must be configured.");
  }

  return apiUrl.endsWith("/") ? apiUrl : `${apiUrl}/`;
};

const getApiHealthUrl = () =>
  new URL(API_HEALTH_PATH, getApiBaseUrl()).toString();

const checkApiHealth = async (): Promise<ApiHealthResult> => {
  let apiHealthUrl: string;

  try {
    apiHealthUrl = getApiHealthUrl();
  } catch (error) {
    return {
      status: "unhealthy",
      error: error instanceof Error ? error.message : "API health URL failed.",
    };
  }

  try {
    const response = await fetch(apiHealthUrl, {
      cache: "no-store",
      signal: AbortSignal.timeout(API_HEALTH_TIMEOUT_MS),
    });

    return {
      status: response.ok ? "healthy" : "unhealthy",
      url: apiHealthUrl,
      statusCode: response.status,
    };
  } catch (error) {
    return {
      status: "unhealthy",
      url: apiHealthUrl,
      error:
        error instanceof Error ? error.message : "API health check failed.",
    };
  }
};

export const GET = async () => {
  const api = await checkApiHealth();
  const isHealthy = api.status === "healthy";

  return NextResponse.json(
    {
      status: isHealthy ? "healthy" : "unhealthy",
      frontend: {
        status: "healthy",
      },
      api,
      checkedAtUtc: new Date().toISOString(),
    },
    { status: isHealthy ? 200 : 503 },
  );
};
