import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

const enabledByEnvironment = (name) =>
  ["1", "true"].includes(process.env[name]?.toLowerCase());

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    globals: true,
    passWithNoTests: false,
    setupFiles: [path.resolve(import.meta.dirname, "vitest.setup.mjs")],
    reporters: [
      "default",
      ...(enabledByEnvironment("REPORT") ? ["junit"] : []),
    ],
    ...(enabledByEnvironment("REPORT")
      ? {
          outputFile: { junit: path.resolve(import.meta.dirname, "junit.xml") },
        }
      : {}),
    watch: enabledByEnvironment("WATCH"),
    coverage: { enabled: enabledByEnvironment("COVERAGE") },
    alias: { "@": path.resolve(import.meta.dirname, "src") },
  },
});
