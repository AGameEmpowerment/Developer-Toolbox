import { TextDecoder, TextEncoder } from "node:util";
import axe from "axe-core";

globalThis.IS_REACT_ACT_ENVIRONMENT = true;
globalThis.TextEncoder = TextEncoder;
globalThis.TextDecoder = TextDecoder;

expect.extend({
  toHaveNoViolations(results) {
    const pass = results.violations.length === 0;
    return {
      pass,
      message: () =>
        pass
          ? "Expected accessibility violations."
          : results.violations.map(({ help }) => help).join("\n"),
    };
  },
});

globalThis.runAxe = (element) =>
  axe.run(element, {
    runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag22aa"] },
    rules: { "color-contrast": { enabled: false } },
  });

vi.mock("next/navigation", () => ({
  notFound: vi.fn(),
  usePathname: vi.fn(() => "/"),
  useRouter: vi.fn(() => ({ push: vi.fn(), replace: vi.fn() })),
}));
