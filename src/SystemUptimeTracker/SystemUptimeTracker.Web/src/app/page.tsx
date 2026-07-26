import UserInfo from "@/components/user-info/user-info";
import { auth } from "@/utils/auth/auth";
import requireSignOn from "@/utils/auth/require-sign-on";

export const metadata = {
  title: "Home",
  description: "Operational landing page.",
};

export default async function HomePage() {
  await requireSignOn();

  const session = await auth("home-page").getSession();
  const displayName = session?.user?.name ?? session?.user?.email ?? "there";

  return (
    <div id="home-page" data-testid="home-page" className="container py-4">
      <section className="mb-4">
        <h1 id="home-page-title" data-testid="home-page-title" className="h3">
          Welcome, {displayName}
        </h1>
        <p
          id="home-page-overview-copy"
          data-testid="home-page-overview-copy"
          className="text-secondary mb-0"
        >
          This is the operational landing page for the application. Use the
          navigation above to reach the areas available to your account.
        </p>
      </section>

      <section
        id="home-page-account"
        data-testid="home-page-account"
        aria-label="Account access"
        className="col-12 col-md-6 col-lg-4"
      >
        <UserInfo />
      </section>
    </div>
  );
}
