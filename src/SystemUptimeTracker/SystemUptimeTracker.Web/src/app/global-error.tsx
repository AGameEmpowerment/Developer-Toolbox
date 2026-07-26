"use client"; // Error components must be Client Components

import { useEffect } from "react";

import { extractTraceId } from "@/utils/error-reference";
import { createClientLogger, trackException } from "@/utils/logger-client";

function getDocumentLanguage() {
  const lang = globalThis.document?.documentElement?.lang;
  return typeof lang === "string" && lang.trim().length > 0 ? lang : "en";
}

export default function Error({ error, reset }) {
  const traceId = extractTraceId(error);
  const documentLanguage = getDocumentLanguage();

  useEffect(() => {
    const log = createClientLogger("GlobalErrorBoundary");
    void log.error("Global error boundary triggered", error, {
      errorName: error?.name || "UnknownError",
      ...(traceId ? { traceId } : {}),
    });
    void trackException(error, 4, {
      category: "GlobalErrorBoundary",
      ...(traceId ? { traceId } : {}),
    });
  }, [error, traceId]);

  return (
    <html lang={documentLanguage} suppressHydrationWarning>
      <body className="bg-body-tertiary text-body">
        <main
          className="container d-flex flex-column align-items-center justify-content-center"
          style={{ minHeight: "100vh" }}
          aria-labelledby="global-error-heading"
        >
          <div className="col-12 col-md-8 col-lg-6 text-center py-5">
            <i
              className="bi bi-exclamation-circle text-danger mb-3"
              style={{ fontSize: "3rem" }}
              aria-hidden="true"
            />

            <h1 id="global-error-heading" className="h3 fw-semibold mb-2">
              Something went wrong
            </h1>

            <p className="text-secondary mb-3">
              We couldn&apos;t complete your request. Please check your
              connection and try again.
            </p>

            {traceId ? (
              <p className="text-secondary small mb-4">
                Reference: <code className="user-select-all">{traceId}</code>
              </p>
            ) : null}

            <button
              type="button"
              className="btn btn-primary d-inline-flex align-items-center gap-2"
              onClick={() => {
                const log = createClientLogger("GlobalErrorBoundary");
                void log.info("Global error boundary reset requested");
                reset();
              }}
            >
              <i className="bi bi-arrow-clockwise" aria-hidden="true" />
              <span>Try again</span>
            </button>
          </div>
        </main>
      </body>
    </html>
  );
}
