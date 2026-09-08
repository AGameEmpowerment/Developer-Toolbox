// @vitest-environment node

vi.mock("server-only", () => ({}));

import { getExampleById, getExamples } from "./examples";

describe("example catalog", () => {
  it("getExamples_returnsStableCatalog", () => {
    expect(getExamples()).toHaveLength(3);
  });

  it("getExampleById_knownId_returnsExample", () => {
    expect(getExampleById("dynamic-routes")?.name).toBe("Dynamic routes");
  });

  it("getExampleById_unknownId_returnsUndefined", () => {
    expect(getExampleById("missing")).toBeUndefined();
  });
});
