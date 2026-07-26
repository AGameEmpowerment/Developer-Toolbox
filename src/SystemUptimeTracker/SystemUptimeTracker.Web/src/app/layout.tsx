import "bootstrap/dist/css/bootstrap.min.css";
import "bootstrap-icons/font/bootstrap-icons.css";
import { headers } from "next/headers";

import { LangProvider } from "../utils/localization/lang-provider";
import AppFooter from "@/components/generic/ui/AppFooter";
import AppHeader from "@/components/generic/ui/AppHeader";
import GlobalStyles from "@/components/generic/ui/GlobalStyles";
import LoggerClientWrapper from "@/components/LoggerClientWrapper";
import Main from "@/components/Main";
import PageWrapper from "@/components/PageWrapper";
import StyledComponentsRegistry from "@/components/StyledComponentsRegistry";
import { auth } from "@/utils/auth/auth";
import { getAuthorizationPoliciesOrDefault } from "@/utils/auth/authorization-policies";
import { SessionUserProvider } from "@/utils/auth/session-user-provider";
import { EnvProvider } from "@/utils/env/env-provider";
import { getClientEnvData } from "@/utils/env/get-client-env-data";
import { langHeaderName } from "@/utils/request-context";

export const metadata = {
  title: {
    template: "%s | System Uptime Tracker",
    default: "System Uptime Tracker", // a default is required when creating a template
  },
  description: "System Uptime Tracker application starter",
};

const RootLayout = async ({ children }) => {
  const a = auth("layout");
  const session = await a.getSession();
  const permissions = session
    ? await getAuthorizationPoliciesOrDefault()
    : undefined;
  const headersList = await headers();
  const lang = headersList.get(langHeaderName);

  // Get safe runtime metadata for client components.
  // Telemetry enablement and sink configuration remain server-side.
  const envData = await getClientEnvData();

  return (
    <html lang={lang}>
      <head>
        <GlobalStyles />
      </head>
      <body className="bg-body-tertiary text-body">
        <EnvProvider data={envData}>
          <LoggerClientWrapper user={session?.user}>
            <LangProvider lang={lang}>
              <SessionUserProvider user={session?.user}>
                <StyledComponentsRegistry>
                  <PageWrapper>
                    <AppHeader
                      name="System Uptime Tracker"
                      permissions={permissions}
                    />
                    <Main id="main-content" tabIndex={-1}>
                      {children}
                    </Main>
                    <AppFooter />
                  </PageWrapper>
                </StyledComponentsRegistry>
              </SessionUserProvider>
            </LangProvider>
          </LoggerClientWrapper>
        </EnvProvider>
      </body>
    </html>
  );
};

export default RootLayout;
