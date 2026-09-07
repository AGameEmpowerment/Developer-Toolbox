import Link from "next/link";
import { headers } from "next/headers";
import Main from "@/components/Main";
import PageWrapper from "@/components/PageWrapper";
import { DEFAULT_LOCALE } from "@/utils/i18n";
import "./global.css";

export const metadata = {
  title: {
    template: "%s - Developer Toolbox example",
    default: "Developer Toolbox example",
  },
  description: "A public local-development sample for Developer Toolbox.",
};

export default async function RootLayout({ children }) {
  const requestHeaders = await headers();
  const language = requestHeaders.get("x-lang") || DEFAULT_LOCALE;

  return (
    <html lang={language}>
      <body>
        <PageWrapper>
          <header className="site-header">
            <a className="skip-link" href="#main-content">
              Skip to main content
            </a>
            <nav aria-label="Primary navigation">
              <ul>
                <li>
                  <Link href="/">Home</Link>
                </li>
                <li>
                  <Link href="/examples">Examples</Link>
                </li>
              </ul>
            </nav>
          </header>
          <Main>{children}</Main>
          <footer className="site-footer">
            <a href="https://github.com/AGameEmpowerment/Developer-Toolbox">
              Developer Toolbox on GitHub
            </a>
          </footer>
        </PageWrapper>
      </body>
    </html>
  );
}
