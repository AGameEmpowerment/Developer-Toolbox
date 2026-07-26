import { afterEach, describe, expect, it, vi } from "vitest";

import { createTraceId } from "./error-reference";

describe("createTraceId", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("returns 32 hex characters when randomUUID is unavailable but getRandomValues exists", () => {
    vi.stubGlobal("crypto", {
      getRandomValues: vi.fn((buffer) => {
        buffer.set(
          Uint8Array.from([
            0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xaa,
            0xbb, 0xcc, 0xdd, 0xee, 0xff,
          ]),
        );
        return buffer;
      }),
    });

    expect(createTraceId()).toBe("00112233445566778899aabbccddeeff");
  });

  it("returns 32 hex characters when crypto is unavailable", () => {
    vi.stubGlobal("crypto", undefined);
    vi.spyOn(Date, "now").mockReturnValue(0x123456789abc);
    vi.spyOn(Math, "random").mockReturnValue(0.1);

    expect(createTraceId()).toMatch(/^[0-9a-f]{32}$/);
  });
});
