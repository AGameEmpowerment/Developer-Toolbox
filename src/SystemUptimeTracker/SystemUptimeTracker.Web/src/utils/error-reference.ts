export const TRACE_ID_HEADER_NAME = "x-trace-id";

const TRACE_ID_PATTERN = /\b[0-9a-f]{32}\b/i;

type TraceIdSource =
  | string
  | Headers
  | Error
  | {
      traceId?: string;
      traceID?: string;
      message?: string;
      detail?: string;
      cause?: unknown;
      headers?: Headers;
      response?: {
        headers?: Headers;
      };
      get?: (name: string) => string | null | undefined;
    };

type TraceableObject = Extract<TraceIdSource, Record<string, unknown>>;

const isHeaderLike = (
  value: TraceIdSource,
): value is Headers | { get: (name: string) => string | null | undefined } => {
  return (
    typeof value === "object" &&
    value !== null &&
    "get" in value &&
    typeof value.get === "function"
  );
};

const isTraceableObject = (value: TraceIdSource): value is TraceableObject => {
  return (
    typeof value === "object" &&
    value !== null &&
    !("get" in value && typeof value.get === "function")
  );
};

function bytesToHex(bytes: Uint8Array) {
  return Array.from(bytes, (value) => value.toString(16).padStart(2, "0")).join(
    "",
  );
}

function createPseudoRandomHexTraceId() {
  const timestampHex = Date.now().toString(16).padStart(12, "0");
  let randomHex = "";

  while (randomHex.length < 32 - timestampHex.length) {
    randomHex += Math.floor(Math.random() * 0x100000000)
      .toString(16)
      .padStart(8, "0");
  }

  return `${timestampHex}${randomHex}`.slice(0, 32);
}

function normalizeMessage(
  message: unknown,
  fallback = "The request could not be completed.",
) {
  if (typeof message !== "string") {
    return fallback;
  }

  const normalizedMessage = message.trim();
  return normalizedMessage || fallback;
}

export function createTraceId() {
  const randomUuid = globalThis.crypto?.randomUUID?.();
  if (randomUuid) {
    return randomUuid.replaceAll("-", "");
  }

  const randomBytes = globalThis.crypto?.getRandomValues?.(new Uint8Array(16));
  if (randomBytes) {
    return bytesToHex(randomBytes);
  }

  return createPseudoRandomHexTraceId();
}

export function normalizeTraceId(traceId: unknown) {
  if (typeof traceId !== "string") {
    return undefined;
  }

  const normalizedTraceId = traceId.trim();
  return normalizedTraceId || undefined;
}

export function extractTraceId(source?: TraceIdSource | null) {
  if (!source) {
    return undefined;
  }

  if (typeof source === "string") {
    return normalizeTraceId(source.match(TRACE_ID_PATTERN)?.[0]);
  }

  if (isHeaderLike(source)) {
    return normalizeTraceId(
      source.get(TRACE_ID_HEADER_NAME) ||
        source.get("Trace-Id") ||
        source.get("traceId"),
    );
  }

  if (isTraceableObject(source)) {
    return (
      normalizeTraceId(source.traceId) ||
      normalizeTraceId(source.traceID) ||
      extractTraceId(source.headers) ||
      extractTraceId(source.response?.headers) ||
      extractTraceId(source.message) ||
      extractTraceId(source.detail) ||
      extractTraceId(source.cause)
    );
  }

  return undefined;
}

export function appendTraceId(message: unknown, traceId?: unknown) {
  const normalizedMessage = normalizeMessage(message);
  const normalizedTraceId = normalizeTraceId(traceId);

  if (
    !normalizedTraceId ||
    extractTraceId(normalizedMessage) === normalizedTraceId
  ) {
    return normalizedMessage;
  }

  return `${normalizedMessage} Trace ID: ${normalizedTraceId}.`;
}

export function createTraceableError(
  message: unknown,
  traceId?: unknown,
  properties: Record<string, unknown> = {},
) {
  const normalizedTraceId = normalizeTraceId(traceId);
  const error = new Error(appendTraceId(message, normalizedTraceId));

  if (normalizedTraceId) {
    error.traceId = normalizedTraceId;
  }

  Object.assign(error, properties);
  return error;
}

export function getPublicErrorTitle(statusCode: number) {
  switch (statusCode) {
    case 400:
      return "The request could not be completed.";
    case 401:
      return "Authentication is required.";
    case 403:
      return "Access denied.";
    case 404:
      return "The requested resource was not found.";
    default:
      return "Something went wrong.";
  }
}

export function getPublicErrorDetail(
  statusCode: number,
  traceId?: unknown,
  baseMessage = "",
) {
  const fallbackMessage =
    statusCode === 401
      ? "Please sign in and try again."
      : statusCode === 403
        ? "You do not have access to complete this request."
        : statusCode === 404
          ? "The requested resource could not be found."
          : "The request could not be completed.";

  return appendTraceId(baseMessage || fallbackMessage, traceId);
}

export function buildPublicErrorPayload({
  statusCode,
  title = "",
  detail = "",
  traceId,
}: {
  statusCode: number;
  title?: string;
  detail?: string;
  traceId?: unknown;
}) {
  const normalizedTraceId = normalizeTraceId(traceId);

  return {
    error: title || getPublicErrorTitle(statusCode),
    detail: getPublicErrorDetail(statusCode, normalizedTraceId, detail),
    ...(normalizedTraceId ? { traceId: normalizedTraceId } : {}),
  };
}
