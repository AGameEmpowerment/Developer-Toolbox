import path from "node:path";
import { afterEach, expect, it, vi } from "vitest";

const flags = ["REPORT", "WATCH", "COVERAGE"];

afterEach(() => {
  vi.unstubAllEnvs();
});

it.each([
  { label: "unset", value: undefined, enabled: [] },
  { label: "false", value: "false", enabled: [] },
  { label: "0", value: "0", enabled: [] },
  { label: "true", value: "true", enabled: flags },
  { label: "TRUE", value: "TRUE", enabled: flags },
  { label: "1", value: "1", enabled: flags },
  ...flags.map((flag) => ({
    label: `only ${flag} enabled`,
    value: "0",
    overrides: { [flag]: "true" },
    enabled: [flag],
  })),
])(
  "sets client test options with $label",
  async ({ value, overrides, enabled }) => {
    for (const flag of flags) {
      vi.stubEnv(flag, overrides?.[flag] ?? value);
    }
    vi.resetModules();

    const { default: config } = await import("./vitest.config.mjs");
    const reportEnabled = enabled.includes("REPORT");

    expect(config.test.reporters).toEqual(
      reportEnabled ? ["default", "junit"] : ["default"],
    );
    expect(config.test.outputFile?.junit).toBe(
      reportEnabled
        ? path.resolve(import.meta.dirname, "junit.xml")
        : undefined,
    );
    expect(config.test.watch).toBe(enabled.includes("WATCH"));
    expect(config.test.coverage.enabled).toBe(enabled.includes("COVERAGE"));
  },
);
