import ProductRouteStub from "@/components/product-route-stub/ProductRouteStub";
import { withServerPageLogging } from "@/utils/logging/contextual-logger";

export const metadata = {
  title: "Login",
};

const loginRouteSummary =
  "This route anchors the product-facing login entry point while the current " +
  "working local identity flow continues to use the retained frontend auth transport.";

export default function LoginPage() {
  return withServerPageLogging({
    category: "LoginPage",
    context: {
      page: "Login",
      route: "/login",
      isStub: true,
    },
    render: async (log, baseContext) => {
      await log.debug("Rendering login stub page", {
        ...baseContext,
        currentFunctionalRoute: "/auth/login",
      });

      return (
        <ProductRouteStub
          title="Login"
          route="/login"
          summary={loginRouteSummary}
          nextStory="Story 010 and Story 011"
          primaryHref="/"
          primaryLabel="Back to home"
          secondaryHref="/auth/login"
          secondaryLabel="Use current sign in"
        >
          <p className="mb-0 text-secondary">
            The current functional sign-in route is still{" "}
            <code>/auth/login</code>. This page keeps the intended product URL
            in place for the later shell and identity stories.
          </p>
        </ProductRouteStub>
      );
    },
  });
}
