import { beforeEach, describe, expect, it, vi } from "vitest";

const readFile = vi.fn();
const mockedLogger = {
  warn: vi.fn().mockResolvedValue(undefined),
  error: vi.fn().mockResolvedValue(undefined),
};

vi.mock("node:fs", () => ({
  default: {
    promises: {
      readFile,
    },
  },
}));

vi.mock("@/utils/request-context", () => ({
  langHeaderName: "x-lang",
}));

vi.mock("next/headers", () => ({
  headers: vi.fn(async () => ({
    get: vi.fn(),
  })),
}));

vi.mock("@/utils/logger-server", () => ({
  createLogger: vi.fn(async () => mockedLogger),
}));

describe("load-strings", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("loads requested groups from localization files without making an HTTP call", async () => {
    readFile.mockImplementation(async (filePath) => {
      if (String(filePath).includes("shared.en.json")) {
        return JSON.stringify({
          key1: "English one",
          key2: "English two",
        });
      }

      if (String(filePath).includes("languages.en.json")) {
        return JSON.stringify({
          eng: "English",
          es: "Spanish",
        });
      }

      throw new Error("missing file");
    });

    const { loadStrings } = await import("./load-strings");
    const strings = await loadStrings("eng", ["shared", "languages"]);

    expect(strings).toEqual({
      shared: {
        key1: "English one",
        key2: "English two",
      },
      languages: {
        eng: "English",
        es: "Spanish",
      },
    });
    expect(mockedLogger.warn).not.toHaveBeenCalled();
    expect(mockedLogger.error).not.toHaveBeenCalledWith(
      "Failed to load localization strings",
      expect.anything(),
      expect.anything(),
    );
  });
});
