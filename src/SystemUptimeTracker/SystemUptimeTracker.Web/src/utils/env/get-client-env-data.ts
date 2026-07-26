"use server";

/**
 * Server-side environment utility for preparing environment data.
 *
 * This module contains server-only functions for extracting environment variables
 * that should be passed to client components via the EnvProvider.
 *
 * SECURITY: Only include environment variables that are safe to expose to the browser.
 * Do NOT include API keys, connection strings, telemetry enablement flags, sink
 * endpoints, or other sensitive data here. All telemetry is routed through server
 * actions, so telemetry configuration remains server-side only.
 *
 * @module utils/env/get-client-env-data
 */

/**
 * Get environment data for passing to the EnvProvider on the server side.
 *
 * This function MUST only be called on the server side (in server components,
 * server actions, or API routes). It reads environment variables and prepares
 * them for client-side consumption.
 *
 * SECURITY NOTE: This does NOT include Application Insights connection strings,
 * OpenTelemetry master/sink flags, or Seq endpoint/API key settings. All telemetry
 * is routed through server actions in logger-server-actions.js, ensuring secrets and
 * server-only telemetry configuration are never exposed to the browser.
 *
 * @returns {Object} Environment data object safe for client exposure
 */
export async function getClientEnvData() {
  return {
    appVersion: process.env.APP_VERSION || "0.0.0",
    environment: process.env.NODE_ENV || "development",
    loggingLevel: process.env.APP_LOGGING_LEVEL || "Information",
  };
}
