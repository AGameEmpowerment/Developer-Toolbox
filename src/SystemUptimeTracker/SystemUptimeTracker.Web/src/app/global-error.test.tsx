import { act } from "react";

import { renderToStaticMarkup } from "react-dom/server";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { getTestContext } from "@/utils/testHelper";

const extractTraceId = vi.fn();
const createClientLogger = vi.fn();
const trackException = vi.fn();

const mockedLogger = {
  error: vi.fn().mockResolvedValue(undefined),
  info: vi.fn().mockResolvedValue(undefined),
};

vi.mock("@/utils/error-reference", () => ({
  extractTraceId,
}));

vi.mock("@/utils/logger-client", () => ({
  createClientLogger,
  trackException,
}));

const { default: GlobalError } = await import("./global-error");

const context = getTestContext();

async function renderError(
  error = new Error("Sensitive backend failure details"),
  reset = vi.fn(),
) {
  await act(async () => {
    context.root?.render(<GlobalError error={error} reset={reset} />);
  });

  return { error, reset };
}

describe("global error page", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    extractTraceId.mockReturnValue(undefined);
    createClientLogger.mockReturnValue(mockedLogger);
    mockedLogger.error.mockResolvedValue(undefined);
    mockedLogger.info.mockResolvedValue(undefined);
    document.documentElement.lang = "en";
  });

  it("renders safe fallback copy and a trace reference when available", async () => {
    extractTraceId.mockReturnValue("0123456789abcdef0123456789abcdef");

    await renderError();

    expect(context.container?.textContent).toContain("Something went wrong");
    expect(context.container?.textContent).toContain(
      "We couldn't complete your request.",
    );
    expect(context.container?.textContent).toContain("Reference:");
    expect(context.container?.textContent).toContain(
      "0123456789abcdef0123456789abcdef",
    );
    expect(context.container?.textContent).not.toContain(
      "Sensitive backend failure details",
    );
  });

  it("logs and tracks the error when the boundary mounts", async () => {
    const error = new Error("Telemetry failure");
    extractTraceId.mockReturnValue("feedfacefeedfacefeedfacefeedface");

    await renderError(error);

    expect(createClientLogger).toHaveBeenCalledWith("GlobalErrorBoundary");
    expect(mockedLogger.error).toHaveBeenCalledWith(
      "Global error boundary triggered",
      error,
      {
        errorName: "Error",
        traceId: "feedfacefeedfacefeedfacefeedface",
      },
    );
    expect(trackException).toHaveBeenCalledWith(error, 4, {
      category: "GlobalErrorBoundary",
      traceId: "feedfacefeedfacefeedfacefeedface",
    });
  });

  it("requests a reset when the retry button is clicked", async () => {
    const reset = vi.fn();

    await renderError(new Error("Retry me"), reset);

    const retryButton = context.container?.querySelector("button");

    await act(async () => {
      retryButton?.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });

    expect(mockedLogger.info).toHaveBeenCalledWith(
      "Global error boundary reset requested",
    );
    expect(reset).toHaveBeenCalledOnce();
  });

  it("omits the reference block when no trace id is available", async () => {
    await renderError();

    expect(context.container?.textContent).not.toContain("Reference:");
  });

  it("uses the active document language for the error document", async () => {
    document.documentElement.lang = "fr-CA";

    const markup = renderToStaticMarkup(
      <GlobalError error={new Error("Localized failure")} reset={vi.fn()} />,
    );

    expect(markup).toContain('<html lang="fr-CA"');
  });
});
