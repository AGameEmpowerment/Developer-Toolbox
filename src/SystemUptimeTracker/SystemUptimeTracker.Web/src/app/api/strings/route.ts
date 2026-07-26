import { headers } from "next/headers";

import { loadStringGroups } from "@/utils/localization/load-string-groups";
import { createLogger } from "@/utils/logger-server";
import { langHeaderName } from "@/utils/request-context";

const GET = async (request) => {
  const log = await createLogger("StringsApiRoute");
  const searchParams = request.nextUrl.searchParams;
  const headersList = await headers();
  const langHeader = headersList.get(langHeaderName);
  const languageCode = searchParams.get("lang") || langHeader || "en";

  const groups = searchParams.get("groups");
  const groupArray = groups
    ? groups
        .split(",")
        .map((group) => group.trim())
        .filter((group) => group)
    : [];

  if (groupArray.length === 0) {
    await log.warn("Strings API request contained no groups");
    return Response.json({});
  }

  const results = await loadStringGroups(groupArray, languageCode, log);

  return Response.json(results);
};

export { GET };
