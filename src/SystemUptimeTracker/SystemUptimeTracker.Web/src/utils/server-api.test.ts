import { describe, expect, it } from "vitest";

import { sanitizeMultipartDispositionValue } from "./multipart";

describe("sanitizeMultipartDispositionValue", () => {
  it("removes CRLF characters and escapes quotes before values are written into multipart headers", () => {
    expect(
      sanitizeMultipartDispositionValue('field"name\r\nX-Injected: true'),
    ).toBe("field%22name X-Injected: true");
  });
});
