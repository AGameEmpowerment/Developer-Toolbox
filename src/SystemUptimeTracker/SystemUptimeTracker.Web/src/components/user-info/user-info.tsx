"use client";

import ClientOnly from "../generic/client-only/client-only";
import { useSessionUser } from "@/utils/auth/session-user-provider";

const UserInfo = ({ sectionId = "user-info" }) => {
  const user = useSessionUser();
  const titleId = `${sectionId}-title`;
  const displayNameId = `${sectionId}-display-name`;
  const sessionStateId = `${sectionId}-session-state`;
  const sessionLinkId = `${sectionId}-session-link`;

  return (
    <ClientOnly>
      <section
        id={sectionId}
        data-testid={sectionId}
        aria-labelledby={titleId}
        className="rounded-3 bg-body-tertiary p-3"
      >
        <h2
          id={titleId}
          data-testid={titleId}
          className="h6 text-uppercase text-secondary fw-semibold mb-2"
        >
          Session
        </h2>
        {user ? (
          <>
            <div
              id={`${sectionId}-identity`}
              data-testid={`${sectionId}-identity`}
              className="mb-3"
            >
              <p
                id={displayNameId}
                data-testid={displayNameId}
                className="mb-0 fw-semibold"
              >
                {user.name}
              </p>
            </div>
            <a
              href="/auth/logout"
              id={sessionLinkId}
              data-testid={sessionLinkId}
              className="btn btn-outline-secondary btn-sm"
            >
              Logout
            </a>
          </>
        ) : (
          <>
            <p
              id={sessionStateId}
              data-testid={sessionStateId}
              className="text-secondary mb-3"
            >
              No active session.
            </p>
            <a
              href="/auth/login"
              id={sessionLinkId}
              data-testid={sessionLinkId}
              className="btn btn-primary btn-sm"
            >
              Sign In
            </a>
          </>
        )}
      </section>
    </ClientOnly>
  );
};

export default UserInfo;
