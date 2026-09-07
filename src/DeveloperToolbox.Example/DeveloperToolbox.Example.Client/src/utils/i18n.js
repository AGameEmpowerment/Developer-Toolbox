import Negotiator from "negotiator";

export const LOCALES = Object.freeze(["en", "es", "fr", "pt"]);
export const DEFAULT_LOCALE = "en";

function isWellFormedLanguageTag(language) {
  if (!language) {
    return false;
  }

  try {
    Intl.getCanonicalLocales(language);
    return true;
  } catch {
    return false;
  }
}

export function getLocaleFromRequest(request) {
  const requestedLanguage = new URL(request.url).searchParams.get("lang");
  const cookieLanguage = request.cookies.get("preferred-language")?.value;
  const acceptLanguage = request.headers.get("accept-language") || "";
  const negotiatedLanguages = new Negotiator({
    headers: { "accept-language": acceptLanguage },
  }).languages();

  return [requestedLanguage, cookieLanguage, ...negotiatedLanguages].filter(
    isWellFormedLanguageTag,
  );
}
