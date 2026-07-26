import { headers } from "next/headers";
import { redirect } from "next/navigation";

import { createLogger } from "@/utils/logger-server";
import { routeHeaderName } from "@/utils/request-context";

const redirectToLogin = async () => {
  const log = await createLogger("RedirectToLogin");
  const headersList = await headers();
  const returnTo = headersList.get(routeHeaderName) || "";
  const baseUrl = "/auth/login";
  const redirectUrl =
    typeof returnTo === "string" && returnTo.length > 0
      ? `${baseUrl}?returnTo=${encodeURIComponent(returnTo)}`
      : baseUrl;

  await log.info("Redirecting request to login", {
    hasReturnTo: typeof returnTo === "string" && returnTo.length > 0,
  });
  redirect(redirectUrl);
};

export default redirectToLogin;
