import { getLocaleFromRequest } from "./i18n";

function createRequest(url, acceptLanguage = "", cookieLanguage = undefined) {
  return {
    url,
    headers: new Headers({ "accept-language": acceptLanguage }),
    cookies: {
      get: () => (cookieLanguage ? { value: cookieLanguage } : undefined),
    },
  };
}

describe("getLocaleFromRequest", () => {
  it("queryLanguage_takesPrecedence", () => {
    const request = createRequest("https://example.test/?lang=fr", "es", "pt");

    expect(getLocaleFromRequest(request)[0]).toBe("fr");
  });

  it("cookieLanguage_isReadFromValue", () => {
    const request = createRequest("https://example.test/", "es", "pt");

    expect(getLocaleFromRequest(request)[0]).toBe("pt");
  });

  it("acceptLanguage_isUsedAsFallback", () => {
    const request = createRequest("https://example.test/", "es,en;q=0.8");

    expect(getLocaleFromRequest(request)[0]).toBe("es");
  });

  it("invalidLanguageTags_areIgnored", () => {
    const request = createRequest(
      "https://example.test/?lang=not_a_locale",
      "fr",
    );

    expect(getLocaleFromRequest(request)).toEqual(["fr"]);
  });
});
