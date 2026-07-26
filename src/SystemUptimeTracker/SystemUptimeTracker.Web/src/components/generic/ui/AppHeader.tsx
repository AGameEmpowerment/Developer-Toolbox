import Link from "next/link";

import AppNavLinks from "./AppNavLinks";
import type { AuthorizationPolicies } from "@/utils/auth/authorization-policies";

type AppHeaderProps = {
  name?: string;
  permissions?: AuthorizationPolicies;
};

export default function AppHeader({
  name = "System Uptime Tracker",
  permissions,
}: AppHeaderProps) {
  return (
    <header className="border-bottom bg-white shadow-sm">
      {/* Skip link: visually hidden until focused — enables keyboard/AT users to bypass the nav */}
      <a href="#main-content" className="visually-hidden-focusable">
        Skip to main content
      </a>

      <nav
        className="navbar navbar-expand-lg container-fluid container-xl"
        aria-label="Primary navigation"
      >
        <Link
          href="/"
          className="navbar-brand d-inline-flex align-items-center gap-2 fw-semibold text-dark"
        >
          <span className="text-primary" aria-hidden="true">
            <i className="bi bi-activity fs-4" />
          </span>
          <span>{name}</span>
        </Link>

        <AppNavLinks permissions={permissions} />
      </nav>
    </header>
  );
}
