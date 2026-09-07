import { NextResponse } from "next/server";
import { proxy } from "./proxy";

vi.mock("next/server", () => ({
  NextResponse: {
    next: vi.fn(({ request } = {}) => ({
      forwardedRequest: request,
      headers: new Headers(),
    })),
  },
}));

function createRequest(language) {
  return {
    url: "https://example.test/",
    headers: new Headers({ "accept-language": language }),
    cookies: { get: vi.fn(() => undefined) },
  };
}

describe("proxy", () => {
  beforeEach(() => {
    NextResponse.next.mockClear();
  });

  it("forwards_theResolvedLocaleToServerComponents", () => {
    const response = proxy(createRequest("fr-CA,fr;q=0.9,en;q=0.8"));

    expect(response.forwardedRequest.headers.get("x-lang")).toBe("fr");
    expect(NextResponse.next).toHaveBeenCalledOnce();
  });

  it("prevents_requestSpecificResponsesFromBeingCached", () => {
    const response = proxy(createRequest("en"));

    expect(response.headers.get("Cache-Control")).toBe("no-store, private");
  });
});
