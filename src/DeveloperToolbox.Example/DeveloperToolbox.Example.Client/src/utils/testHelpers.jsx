import { act } from "react";
import { createRoot } from "react-dom/client";

export function getTestContext(context = {}) {
  beforeEach(() => {
    context.container = document.createElement("div");
    document.body.appendChild(context.container);
    context.root = createRoot(context.container);
  });

  afterEach(async () => {
    await act(async () => context.root.unmount());
    context.container.remove();
    context.container = undefined;
    context.root = undefined;
  });

  return context;
}

export function genericComponentTests(context, Component, props = {}) {
  it("renders_withoutCrashing", async () => {
    await act(async () => context.root.render(<Component {...props} />));
  });

  it("renderedMarkup_hasNoAutomatedAccessibilityViolations", async () => {
    await act(async () => context.root.render(<Component {...props} />));

    let results;
    await act(async () => {
      results = await globalThis.runAxe(context.container);
    });

    expect(results).toHaveNoViolations();
  });
}
