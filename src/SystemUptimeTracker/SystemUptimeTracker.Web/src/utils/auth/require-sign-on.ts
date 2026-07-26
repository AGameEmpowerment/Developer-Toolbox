import redirectToLogin from "./redirect-to-login";
import { auth } from "@/utils/auth/auth";
import { createLogger } from "@/utils/logger-server";

const requireSignOn = async () => {
  const log = await createLogger("RequireSignOn");
  let session = null;
  try {
    const a = auth("requireSignOn");
    session = await a.getSession();
  } catch (error) {
    await log.warn("Session lookup failed during sign-on requirement check", {
      errorName: error?.name || "UnknownError",
    });
    session = null;
  }
  if (!session) {
    await log.info("Redirecting anonymous request to login");
    await redirectToLogin();
    return;
  }

  await log.debug("User session verified for sign-on protected page");
};

export default requireSignOn;
