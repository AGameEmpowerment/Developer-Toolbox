/* eslint-disable no-param-reassign */
import type { ReactElement } from "react";
import { act } from "react";

import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

type TestContext = {
  container?: HTMLDivElement | null;
  root?: ReturnType<typeof createRoot> | null;
};

export function getTestContext(context: TestContext = {}) {
  const origConsole: { error?: typeof window.console.error } = {};

  beforeEach(() => {
    // setup a DOM element as a render target
    origConsole.error = window.console.error;
    window.console.error = vi.fn((error: { type?: string } | undefined) => {
      /* JSDOM does not support @container queries and other newer css.  This prevents css errors from breaking the build.
       */
      if (error.type === "css parsing" || error.type === undefined) {
        return;
      }
      origConsole.error(error);
    });
    context.container = document.createElement("div");
    document.body.appendChild(context.container);
    context.root = createRoot(context.container);
  });

  afterEach(async () => {
    // cleanup on exiting
    await act(async () => {
      context.root.unmount();
    });
    await new Promise<void>((resolve) => {
      context.container.remove();
      resolve();
    });
    context.container = null;
    window.console.error = origConsole.error;
  });

  return context;
}

export const genericTests = (
  context: TestContext,
  Component: (props: Record<string, unknown>) => ReactElement,
  props: Record<string, unknown> = {},
) => {
  describe("Generic Tests", () => {
    it("should render with required props without crashing", async () => {
      await act(async () => {
        context.root.render(<Component {...props} />);
      });
    });

    it("should pass accessibility standards", async () => {
      await act(async () => {
        context.root.render(<Component {...props} />);
      });

      let results;

      await act(async () => {
        results = await global.axe(context.container);
      });

      expect(results).toHaveNoViolations();
    });
  });
};
