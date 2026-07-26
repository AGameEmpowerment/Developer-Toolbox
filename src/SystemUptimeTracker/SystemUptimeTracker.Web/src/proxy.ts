import { NextResponse } from "next/server";

import {
  appCookieName,
  langHeaderName,
  routeHeaderName,
} from "@/utils/request-context";

// Helper function to set Cache-Control headers
function setNoCacheHeaders(response) {
  response.headers.set(
    "Cache-Control",
    "no-store, no-cache, must-revalidate, proxy-revalidate, private",
  );
  response.headers.set("Pragma", "no-cache"); // HTTP 1.0
  response.headers.set("Expires", "0");
  return response;
}

const impersonateCookieName = process.env.IMPERSONATING_COOKIE;

/*This middleware will intercept all requests and add a header to the request.
It is a utility meant to make it easy to see the route name from a server component/page
so that our require sign on utility has a way to redirect back to the page that was requested.
The utils/auth/require-sign-on.js utility will use this header to redirect back to the page that was requested.
*/
export default async function proxy(req) {
  let response = NextResponse.next();
  response = setNoCacheHeaders(response);

  const searchParams = new URL(req.url).searchParams;

  const lang = searchParams.get("lang");

  // backup methods for lang detection
  const cookies = req.cookies;
  const headers = req.headers;
  const appCookie = cookies.get(appCookieName)?.value;
  const appPreferredLang = cookies.get("preferred-lang");

  const policyPreferredLang = cookies.get("policy-preferred-lang");
  const acceptLang = headers.get("accept-language");

  //utilities that help our require sign on and our language detection work.
  response.headers.set(routeHeaderName, req.nextUrl.pathname);

  if (req.url.endsWith("auth/logout")) {
    if (cookies.has(impersonateCookieName)) {
      response.cookies.delete(impersonateCookieName);
    }
    const dataName = `${impersonateCookieName}-data`;
    if (cookies.has(dataName)) {
      response.cookies.delete(dataName);
    }
  }

  response.headers.set(
    langHeaderName,
    lang ||
      appCookie ||
      appPreferredLang ||
      policyPreferredLang ||
      acceptLang ||
      "eng",
  );

  return response;
}
export const config = {
  matcher: [
    /*
     * Match all request paths except for the ones starting with:
     * - _next/static (static files)
     * - _next/image (image optimization files)
     * - favicon.ico, sitemap.xml, robots.txt (metadata files)
     */
    "/((?!_next/static|_next/image|favicon.ico|sitemap.xml|robots.txt).*)",
  ],
};
