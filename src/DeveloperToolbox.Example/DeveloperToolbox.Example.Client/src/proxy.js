import { match } from "@formatjs/intl-localematcher";
import { NextResponse } from "next/server";
import { DEFAULT_LOCALE, getLocaleFromRequest, LOCALES } from "./utils/i18n";

export function proxy(request) {
  const language = match(
    getLocaleFromRequest(request),
    LOCALES,
    DEFAULT_LOCALE,
  );
  const requestHeaders = new Headers(request.headers);
  requestHeaders.set("x-lang", language);
  const response = NextResponse.next({ request: { headers: requestHeaders } });
  response.headers.set("Cache-Control", "no-store, private");
  return response;
}

export const config = {
  matcher: [
    "/((?!_next/static|_next/image|favicon.ico|sitemap.xml|robots.txt).*)",
  ],
};
