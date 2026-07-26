import { describe, expect, it } from "vitest";

import { getDelimitedValues, getOrigin } from "./next.config.mjs";

describe("next config CSP connect-src", () => {
  it("accepts a bare Microsoft identity host by normalizing it to https", () => {
    expect(getOrigin("login.microsoftonline.com")).toBe(
      "https://login.microsoftonline.com",
    );
  });

  it("omits unsupported schemes from connect-src", () => {
    expect(getOrigin("javascript:alert(1)")).toBeUndefined();
  });

  it("parses allowed development origins from comma-delimited configuration", () => {
    expect(
      getDelimitedValues("host.docker.internal, localhost, 127.0.0.1"),
    ).toEqual(["host.docker.internal", "localhost", "127.0.0.1"]);
  });
});
