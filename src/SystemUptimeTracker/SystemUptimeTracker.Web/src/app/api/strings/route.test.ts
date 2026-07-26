import { beforeEach, describe, expect, it, vi } from "vitest";

const readFile = vi.fn();
const mockedHeaders = {
  get: vi.fn(),
};
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
  headers: vi.fn(async () => mockedHeaders),
}));

vi.mock("@/utils/logger-server", () => ({
  createLogger: vi.fn(async () => mockedLogger),
}));

describe("strings api route", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedHeaders.get.mockReturnValue(undefined);
  });

  it("falls back to english and logs when requested translations are missing", async () => {
    readFile.mockImplementation(async (filePath) => {
      if (String(filePath).includes("shared.en.json")) {
        return JSON.stringify({
          key1: "English one",
          key2: "English two",
        });
      }

      throw new Error("missing file");
    });

    const { GET } = await import("./route");
    const request = {
      nextUrl: {
        searchParams: new URLSearchParams("lang=fr&groups=shared"),
      },
    };

    const response = await GET(request);
    const payload = await response.json();

    expect(payload).toEqual({
      shared: {
        key1: "English one",
        key2: "English two",
      },
    });
    expect(mockedLogger.warn).toHaveBeenCalledWith(
      "Requested localization strings were not found",
      {
        group: "shared",
        requestedLanguage: "fr",
        fallbackLanguage: "en",
      },
    );
    expect(mockedLogger.error).not.toHaveBeenCalled();
  });
});
