"use server";

import { cookies } from "next/headers";

import { createLogger } from "@/utils/logger-server";
import { appCookieName } from "@/utils/request-context";

const GET = async (request) => {
  const log = await createLogger("LanguageChangeApiRoute");
  const lang = request.nextUrl.searchParams.get("lang");
  if (lang) {
    const c = await cookies();
    c.set({
      name: appCookieName,
      value: lang,
      httpOnly: false,
      path: "/",
    });
    await log.info("Updated preferred language cookie", {
      lang,
    });
  } else {
    await log.warn("Language change request did not include a language value");
  }
  return new Response("OK", { status: 200 });
};

export { GET };
