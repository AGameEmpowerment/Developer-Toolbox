"use client";

import type { PropsWithChildren } from "react";
import { createContext, useContext } from "react";

/**
 * Environment context for passing server-side environment variables to client components.
 *
 * This pattern allows server components to read environment variables and pass them
 * to client components without exposing secrets in NEXT_PUBLIC_* variables.
 *
 * SECURITY: Only include environment variables that are safe to expose to the browser.
 * Do NOT include API keys, connection strings, or other sensitive data here.
 *
 * For telemetry in System Uptime Tracker:
 * - Do NOT expose Application Insights connection strings or instrumentation keys
 * - Do NOT expose OpenTelemetry enablement flags or Seq sink settings
 * - Route telemetry through server actions in logger-server.js instead
 *
 * @module utils/env/env-provider
 */

/**
 * Context for environment variables
 */
export type ClientEnvironmentData = {
  appVersion?: string;
  environment?: string;
  loggingLevel?: string;
  [key: string]: string | number | boolean | undefined;
};

const EnvContext = createContext<ClientEnvironmentData | null>(null);

/**
 * Provider component that makes environment variables available to client components.
 *
 * @param {Object} props - Component props
 * @param {Object} props.data - Environment variable data object
 * @param {React.ReactNode} props.children - Child components
 * @returns {React.ReactElement} Provider component
 *
 * @example
 * // In a server component (page.jsx or layout.jsx):
 * export default async function Page() {
 *   const envData = {
 *     appVersion: process.env.APP_VERSION || '0.0.0',
 *     environment: process.env.NODE_ENV || 'development',
 *     loggingLevel: process.env.APP_LOGGING_LEVEL || 'Warning',
 *   };
 *
 *   return (
 *     <EnvProvider data={envData}>
 *       <ClientComponent />
 *     </EnvProvider>
 *   );
 * }
 */
export function EnvProvider({
  data,
  children,
}: PropsWithChildren<{ data: ClientEnvironmentData | null }>) {
  return <EnvContext.Provider value={data}>{children}</EnvContext.Provider>;
}

/**
 * Hook to access environment variables from client components.
 *
 * @returns {Object|null} Environment data object or null if not in provider
 *
 * @example
 * // In a client component:
 * 'use client';
 * import { useEnvironment } from '@/utils/env/env-provider';
 *
 * export function MyComponent() {
 *   const env = useEnvironment();
 *   console.log('App version:', env?.appVersion);
 *   return <div>Version: {env?.appVersion}</div>;
 * }
 */
export function useEnvironment() {
  const context = useContext(EnvContext);
  return context;
}

/**
 * Get environment data for passing to the EnvProvider.
 * IMPORTANT: Do not call this from client components. Use getClientEnvData()
 * from @/utils/env/get-client-env-data instead, which must be called from
 * server components only.
 *
 * For client-side access to environment data, use the useEnvironment() hook.
 *
 * @deprecated Use getClientEnvData() from @/utils/env/get-client-env-data
 * @returns {Object} Environment data object safe for client exposure
 */
export function getClientEnvData() {
  throw new Error(
    "getClientEnvData() must be called from a server component. " +
      "Import from '@/utils/env/get-client-env-data' instead.",
  );
}

export default EnvProvider;
