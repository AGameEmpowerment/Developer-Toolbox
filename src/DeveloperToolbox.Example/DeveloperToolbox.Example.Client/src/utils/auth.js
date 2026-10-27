import { Auth0Client } from "@auth0/nextjs-auth0/server";

export function createAuthClient() {
  if (process.env.TOOLBOX_AUTH_ENABLED !== "true") return undefined;

  return new Auth0Client({
    clientId: process.env.AUTH0_CLIENT_ID,
    clientSecret: process.env.AUTH0_CLIENT_SECRET,
    secret: process.env.AUTH0_SECRET,
    domain: process.env.AUTH0_DOMAIN,
    appBaseUrl: process.env.APP_BASE_URL,
    authorizationParameters: {
      scope: "openid profile email offline_access",
    },
    session: { absoluteDuration: 60 * 60 * 10 },
  });
}
