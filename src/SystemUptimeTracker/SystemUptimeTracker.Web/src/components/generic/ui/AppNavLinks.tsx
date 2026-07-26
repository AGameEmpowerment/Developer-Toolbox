"use client";

import { useEffect, useRef, useState } from "react";

import Link from "next/link";
import { usePathname } from "next/navigation";

import type { AuthorizationPolicies } from "@/utils/auth/authorization-policies";
import { useSessionUser } from "@/utils/auth/session-user-provider";

// ── Admin sub-navigation dropdown ────────────────────────────────────────────

type AdminDropdownProps = {
  isAdminActive: boolean;
  canManageUsers: boolean;
};

function AdminDropdown({ isAdminActive, canManageUsers }: AdminDropdownProps) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLLIElement>(null);

  // Close when focus moves outside the dropdown
  const handleBlur = (e: React.FocusEvent) => {
    if (!containerRef.current?.contains(e.relatedTarget as Node)) {
      setOpen(false);
    }
  };

  // Close on Escape
  useEffect(() => {
    if (!open) return;
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setOpen(false);
      }
    };
    document.addEventListener("keydown", handleKey);
    return () => document.removeEventListener("keydown", handleKey);
  }, [open]);

  useEffect(() => {
    if (!open) {
      return;
    }

    const handlePointerDown = (event: PointerEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    };

    document.addEventListener("pointerdown", handlePointerDown);
    return () => document.removeEventListener("pointerdown", handlePointerDown);
  }, [open]);

  if (!canManageUsers) {
    return null;
  }

  return (
    <li className="nav-item dropdown" ref={containerRef} onBlur={handleBlur}>
      <button
        id="admin-nav-dropdown-btn"
        type="button"
        className={`nav-link dropdown-toggle d-inline-flex align-items-center gap-1 bg-transparent border-0${
          isAdminActive ? " active" : ""
        }`}
        aria-expanded={open}
        aria-haspopup="true"
        aria-controls="admin-nav-dropdown-menu"
        onClick={() => setOpen((prev) => !prev)}
      >
        <i className="bi bi-gear" aria-hidden="true" />
        <span>Admin</span>
      </button>
      <ul
        id="admin-nav-dropdown-menu"
        className={`dropdown-menu${open ? " show" : ""}`}
        aria-label="Admin"
      >
        {canManageUsers ? (
          <li>
            <Link
              href="/admin/users"
              className="dropdown-item d-flex align-items-center gap-2"
              onClick={() => setOpen(false)}
            >
              <i className="bi bi-person-gear" aria-hidden="true" />
              <span>Users</span>
            </Link>
          </li>
        ) : null}
      </ul>
    </li>
  );
}

// ── Primary navigation links ──────────────────────────────────────────────────

type AppNavLinksProps = {
  permissions?: AuthorizationPolicies;
};

export default function AppNavLinks({ permissions }: AppNavLinksProps) {
  const [navOpen, setNavOpen] = useState(false);
  const pathname = usePathname();
  const user = useSessionUser();

  const isActive = (href: string) => pathname.startsWith(href);

  useEffect(() => {
    setNavOpen(false);
  }, [pathname]);

  const closeMobileNav = () => {
    setNavOpen(false);
  };

  return (
    <>
      {/* Mobile hamburger toggler */}
      <button
        className="navbar-toggler"
        type="button"
        aria-controls="app-nav-collapse"
        aria-expanded={navOpen}
        aria-label="Toggle navigation"
        onClick={() => setNavOpen((prev) => !prev)}
      >
        <span className="navbar-toggler-icon" />
      </button>

      {/* Collapsible nav content */}
      <div
        className={`collapse navbar-collapse${navOpen ? " show" : ""}`}
        id="app-nav-collapse"
      >
        <ul className="navbar-nav me-auto mb-2 mb-lg-0">
          <AdminDropdown
            isAdminActive={isActive("/admin")}
            canManageUsers={permissions?.canManageUsers ?? false}
          />
        </ul>

        {/* Auth status */}
        <div className="d-flex align-items-center">
          {user ? (
            <span className="text-body-secondary small d-flex align-items-center gap-1">
              <i className="bi bi-person-circle" aria-hidden="true" />
              <span>{user.name ?? user.email ?? "Signed in"}</span>
            </span>
          ) : (
            <Link
              href="/auth/login"
              className="btn btn-outline-primary btn-sm d-inline-flex align-items-center gap-1"
              onClick={closeMobileNav}
            >
              <i className="bi bi-box-arrow-in-right" aria-hidden="true" />
              <span>Sign In</span>
            </Link>
          )}
        </div>
      </div>
    </>
  );
}
