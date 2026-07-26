import path from "path";

import { defineConfig } from "vitest/config";

/** @type { import('vitest/node').UserConfig } */
const customConfig = defineConfig({
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./vitest.setup.mjs"],
    reporters: ["default", "junit"],
    outputFile: {
      junit: "./junit.xml",
    },
    coverage: {
      all: false,
      enabled: false,
      reporter: ["text", "lcov"],
    },
  },
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "src"),
    },
  },
});

export default customConfig;
