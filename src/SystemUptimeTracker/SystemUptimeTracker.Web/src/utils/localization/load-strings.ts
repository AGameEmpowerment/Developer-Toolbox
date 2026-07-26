import { headers } from "next/headers";

import { loadStringGroups } from "@/utils/localization/load-string-groups";
import { createLogger } from "@/utils/logger-server";
import { langHeaderName } from "@/utils/request-context";

async function loadStrings(lang, groups) {
  const log = await createLogger("LoadStrings");
  try {
    return await loadStringGroups(groups, lang, log);
  } catch (error) {
    await log.error("Failed to load localization strings", error, {
      lang,
      groupCount: groups.length,
    });
    throw error;
  }
}

async function detectLanguageServerSide() {
  const headersList = await headers();
  //we get header strings like en-us,q=9.0;en; but we won't always have it in that format.
  //we just want that first en out of it.
  let headerLang = headersList.get(langHeaderName);

  if (headerLang) {
    headerLang = headerLang.split(",")[0];
  }
  if (headerLang) {
    headerLang = headerLang.split("-")[0];
  }
  return headerLang;
}

export { loadStrings, detectLanguageServerSide };
