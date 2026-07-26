"use client";

import { useEffect, useRef } from "react";

import { usePathname } from "next/navigation";

import { useEnvironment } from "@/utils/env/env-provider";
import {
  createClientLogger,
  initializeLogger,
  trackPageView,
} from "@/utils/logger-client";

/**
 * Client-side wrapper component that initializes client logging
 * and tracks user context for telemetry.
 *
 * SECURITY: This component does NOT directly communicate with telemetry backends.
 * All telemetry is routed through a server route handler, ensuring that
 * telemetry secrets and server-only configuration stay out of the browser.
 *
 * Architecture:
 * - This component initializes client-side logging
 * - All log calls are routed to logger-client.js
 * - logger-client.js posts telemetry to /api/client-telemetry
 * - the route handler delegates to logger-server.js
 * - logger-server.js sends telemetry to Azure Application Insights
 *
 * @component
 * @param {Object} props - Component props
 * @param {React.ReactNode} props.children - Child components to render
 * @param {Object} props.user - Authenticated Microsoft user object (optional)
 * @returns {React.ReactNode} The children wrapped in logging context
 */
export default function LoggerClientWrapper({ children, user }) {
  const lastTrackedPathname = useRef<string | null>(null);
  const envData = useEnvironment();
  const pathname = usePathname();

  useEffect(() => {
    if (!envData?.environment) {
      return;
    }

    initializeLogger(envData.environment, envData.appVersion);
  }, [envData?.appVersion, envData?.environment]);

  useEffect(() => {
    const currentPathname = pathname || "/";

    if (lastTrackedPathname.current === currentPathname) {
      return;
    }

    const isInitialLoad = lastTrackedPathname.current === null;
    lastTrackedPathname.current = currentPathname;

    const log = createClientLogger("PageNavigation");
    const pageName = globalThis.document?.title || "Application Page";
    const pageUrl = globalThis.window?.location?.href || "";

    void trackPageView(pageName, pageUrl, {
      pathname: currentPathname,
      isAuthenticated: Boolean(user?.sub),
      isInitialLoad,
    });

    void log.debug("Page landed", {
      pageName,
      pathname: currentPathname,
      isAuthenticated: Boolean(user?.sub),
      isInitialLoad,
    });
  }, [pathname, user?.sub]);

  return <>{children}</>;
}
