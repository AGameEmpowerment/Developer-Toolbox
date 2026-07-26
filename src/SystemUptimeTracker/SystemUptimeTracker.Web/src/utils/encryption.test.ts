import { beforeEach, describe, expect, it } from "vitest";

describe("encryption", () => {
  const validKey = "11".repeat(32);

  beforeEach(() => {
    process.env.IMPERSONATE_ENCRYPTION_KEY = validKey;
  });

  it("round-trips string values with valid settings", async () => {
    const { encrypt, decrypt } = await import("./encryption");
    const encrypted = encrypt("owner@example.test");

    expect(decrypt(encrypted)).toBe("owner@example.test");
  });

  it("round-trips object values with valid settings", async () => {
    const { encrypt, decrypt } = await import("./encryption");
    const encrypted = encrypt({
      email: "owner@example.test",
      roles: ["Editor"],
    });

    expect(decrypt(encrypted)).toEqual({
      email: "owner@example.test",
      roles: ["Editor"],
    });
  });

  it("uses a random iv for each encrypted payload", async () => {
    const { encrypt } = await import("./encryption");

    expect(encrypt("owner@example.test")).not.toBe(
      encrypt("owner@example.test"),
    );
  });

  it("rejects keys that are not 32 bytes of hex", async () => {
    process.env.IMPERSONATE_ENCRYPTION_KEY = "abc123";
    const { encrypt } = await import("./encryption");

    expect(() => encrypt("owner@example.test")).toThrow(
      "IMPERSONATE_ENCRYPTION_KEY must be a 256-bit hexadecimal value.",
    );
  });

  it("rejects malformed encrypted payloads", async () => {
    const { decrypt } = await import("./encryption");

    expect(() => decrypt("bad-payload")).toThrow(
      "Encrypted impersonation payload is invalid.",
    );
  });

  it("reports malformed embedded payload metadata without implying missing env vars", async () => {
    const { decrypt } = await import("./encryption");

    expect(() =>
      decrypt(`v1:${"zz".repeat(12)}:${"11".repeat(16)}:abcd`),
    ).toThrow("Impersonation payload IV must be a 96-bit hexadecimal value.");
  });
});
