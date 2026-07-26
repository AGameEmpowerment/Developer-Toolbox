import { NextResponse } from "next/server";

import {
  clientLog,
  clientTrackEvent,
  clientTrackException,
  clientTrackPageView,
  createLogger,
} from "@/utils/logger-server";

const telemetryHandlers = Object.freeze({
  clientLog,
  clientTrackEvent,
  clientTrackException,
  clientTrackPageView,
});

export async function POST(request) {
  const logger = await createLogger("ClientTelemetryRoute");
  let payload;

  try {
    payload = await request.json();
  } catch {
    await logger.warning("Client telemetry request contained invalid JSON.", {
      route: "/api/client-telemetry",
      requestMethod: request.method,
    });
    return NextResponse.json(
      { error: "Telemetry payload must be valid JSON." },
      { status: 400 },
    );
  }

  const { actionName, args = [] } = payload || {};
  const telemetryHandler = telemetryHandlers[actionName];

  if (!telemetryHandler || !Array.isArray(args)) {
    await logger.warning(
      "Client telemetry request referenced an unsupported action.",
      {
        actionName:
          typeof actionName === "string" && actionName.trim().length > 0
            ? actionName.trim()
            : "unknown",
        argumentCount: Array.isArray(args) ? args.length : 0,
        route: "/api/client-telemetry",
      },
    );
    return NextResponse.json(
      { error: "Telemetry action is not supported." },
      { status: 400 },
    );
  }

  try {
    await telemetryHandler(...args);
    return NextResponse.json({ ok: true });
  } catch (error) {
    await logger.error(
      "Client telemetry dispatch failed.",
      error instanceof Error ? error : new Error("Telemetry dispatch failed."),
      {
        actionName,
        argumentCount: args.length,
        route: "/api/client-telemetry",
      },
    );
    return NextResponse.json(
      { error: "Telemetry dispatch failed." },
      { status: 500 },
    );
  }
}
