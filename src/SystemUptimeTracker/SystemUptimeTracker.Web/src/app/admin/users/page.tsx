import { notFound } from "next/navigation";

import { setUserActivation } from "@/features/users/server/user-management-actions";
import { assignableRoles } from "@/features/users/user-roles";
import { auth } from "@/utils/auth/auth";
import { getAuthorizationPolicies } from "@/utils/auth/authorization-policies";
import requireSignOn from "@/utils/auth/require-sign-on";
import { withServerPageLogging } from "@/utils/logging/contextual-logger";
import { serverApiGetAsJson } from "@/utils/server-api-get";

export const metadata = {
  title: "Admin Users",
};

type AdminUser = {
  userId: string;
  email: string;
  displayName: string;
  roles: string[];
  isActive: boolean;
  createdAtUtc: string;
  lastLoginAtUtc: string | null;
};

const dateFormatter = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeZone: "UTC",
});

const formatDate = (value: string | null | undefined) => {
  if (!value) return "--";
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? "--" : dateFormatter.format(date);
};

const getUsers = async (): Promise<AdminUser[]> =>
  (await serverApiGetAsJson({
    url: "api/users",
    defaultData: [],
  })) as AdminUser[];

export default async function AdminUsersPage() {
  return withServerPageLogging({
    category: "AdminUsersPage",
    context: {
      page: "AdminUsers",
      route: "/admin/users",
    },
    render: async (log, baseContext) => {
      await requireSignOn();
      const [permissions, session] = await Promise.all([
        getAuthorizationPolicies(),
        auth().getSession(),
      ]);

      if (!permissions.canManageUsers) {
        await log.warn("Rejected admin users page access", {
          ...baseContext,
          isAuthenticated: Boolean(session?.user?.sub),
        });
        notFound();
      }

      const currentUserEmail = session?.user?.email?.toLowerCase() ?? "";
      const users = await getUsers();
      const pendingCount = users.filter(
        (user) => user.roles.length === 0,
      ).length;

      await log.debug("Admin users page data loaded", {
        ...baseContext,
        userCount: users.length,
        pendingCount,
      });

      return (
        <main
          id="admin-users-page"
          data-testid="admin-users-page"
          className="container py-4 py-lg-5"
        >
          <section
            className="rounded-4 border bg-white shadow-sm p-4 p-lg-5"
            aria-labelledby="admin-users-page-title"
          >
            <div className="d-flex flex-column flex-lg-row justify-content-between gap-3 mb-4">
              <div>
                <div className="d-inline-flex align-items-center gap-2 small text-uppercase text-secondary fw-semibold mb-2">
                  <i className="bi bi-shield-lock" aria-hidden="true" />
                  <span>User Administration</span>
                </div>
                <h1
                  id="admin-users-page-title"
                  data-testid="admin-users-page-title"
                  className="display-6 fw-semibold mb-2"
                >
                  Users
                </h1>
                <p className="text-secondary mb-0">
                  Manage user accounts, approve access, and assign roles that
                  control what each person can see and do across the platform.
                </p>
              </div>

              {pendingCount > 0 && (
                <div className="d-flex flex-wrap gap-2 align-items-start">
                  <span className="badge rounded-pill text-bg-warning border text-dark px-3 py-2">
                    {pendingCount} pending
                  </span>
                </div>
              )}
            </div>

            <div className="table-responsive">
              <table
                className="table table-hover align-middle"
                data-testid="admin-users-page-table"
              >
                <caption className="caption-top text-secondary">
                  User accounts and current role access.
                </caption>
                <thead>
                  <tr>
                    <th scope="col">User</th>
                    <th scope="col">Status</th>
                    <th scope="col">Activity</th>
                    <th scope="col">Roles</th>
                    <th scope="col" className="text-end">
                      Role actions
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {users.length > 0 ? (
                    users.map((user) => {
                      const isCurrentUser =
                        user.email.toLowerCase() === currentUserEmail;
                      return (
                        <tr
                          key={user.userId}
                          data-testid={`admin-user-${user.userId}`}
                        >
                          <th scope="row">
                            <div className="d-flex align-items-center gap-2 flex-wrap">
                              <span className="fw-semibold">
                                {user.displayName || user.email}
                              </span>
                              {isCurrentUser && (
                                <span
                                  className="badge text-bg-primary"
                                  data-testid={`admin-user-${user.userId}-you-badge`}
                                >
                                  you
                                </span>
                              )}
                            </div>
                            <div className="small text-secondary">
                              {user.email}
                            </div>
                          </th>
                          <td>
                            <div className="d-flex flex-column gap-1 align-items-start">
                              {user.isActive ? (
                                <span className="badge text-bg-success d-inline-flex align-items-center gap-1">
                                  <i
                                    className="bi bi-person-check"
                                    aria-hidden="true"
                                  />
                                  <span>Active</span>
                                </span>
                              ) : (
                                <span className="badge text-bg-secondary d-inline-flex align-items-center gap-1">
                                  <i
                                    className="bi bi-person-slash"
                                    aria-hidden="true"
                                  />
                                  <span>Inactive</span>
                                </span>
                              )}
                              {user.roles.length > 0 ? (
                                <span className="badge text-bg-light border d-inline-flex align-items-center gap-1">
                                  <i
                                    className="bi bi-shield-check"
                                    aria-hidden="true"
                                  />
                                  <span>Approved</span>
                                </span>
                              ) : (
                                <span className="badge text-bg-warning d-inline-flex align-items-center gap-1">
                                  <i
                                    className="bi bi-hourglass-split"
                                    aria-hidden="true"
                                  />
                                  <span>Pending</span>
                                </span>
                              )}
                            </div>
                          </td>
                          <td>
                            <div className="small">
                              <div className="text-secondary">
                                Joined {formatDate(user.createdAtUtc)}
                              </div>
                              <div className="text-secondary">
                                Last seen{" "}
                                {user.lastLoginAtUtc
                                  ? formatDate(user.lastLoginAtUtc)
                                  : "never"}
                              </div>
                            </div>
                          </td>
                          <td>
                            <form
                              id={`user-roles-form-${user.userId}`}
                              action="/admin/users/assign-roles"
                              method="post"
                              className="d-flex flex-wrap gap-2"
                            >
                              <input
                                type="hidden"
                                name="userId"
                                value={user.userId}
                              />
                              {assignableRoles.map((role) => (
                                <div className="form-check" key={role}>
                                  <input
                                    id={`${user.userId}-${role}`}
                                    name="roles"
                                    type="checkbox"
                                    value={role}
                                    defaultChecked={user.roles.includes(role)}
                                    className="form-check-input"
                                  />
                                  <label
                                    htmlFor={`${user.userId}-${role}`}
                                    className="form-check-label"
                                  >
                                    {role}
                                  </label>
                                </div>
                              ))}
                            </form>
                          </td>
                          <td className="text-end">
                            <div className="d-inline-flex flex-column flex-sm-row gap-2 justify-content-end">
                              <button
                                type="submit"
                                form={`user-roles-form-${user.userId}`}
                                className="btn btn-sm btn-outline-primary d-inline-flex align-items-center gap-1 justify-content-center"
                                aria-label={`Save roles for ${user.displayName || user.email}`}
                              >
                                <i
                                  className="bi bi-shield-check"
                                  aria-hidden="true"
                                />
                                <span>Save roles</span>
                              </button>
                              <form action={setUserActivation}>
                                <input
                                  type="hidden"
                                  name="userId"
                                  value={user.userId}
                                />
                                <input
                                  type="hidden"
                                  name="isActive"
                                  value={user.isActive ? "false" : "true"}
                                />
                                <button
                                  type="submit"
                                  disabled={isCurrentUser}
                                  title={
                                    isCurrentUser
                                      ? "You cannot change your own activation status"
                                      : undefined
                                  }
                                  className={`btn btn-sm ${user.isActive ? "btn-outline-secondary" : "btn-outline-success"} d-inline-flex align-items-center gap-1 justify-content-center`}
                                  aria-label={`${user.isActive ? "Deactivate" : "Activate"} ${user.displayName || user.email}`}
                                >
                                  <i
                                    className={`bi ${user.isActive ? "bi-person-slash" : "bi-person-check"}`}
                                    aria-hidden="true"
                                  />
                                  <span>
                                    {user.isActive ? "Deactivate" : "Activate"}
                                  </span>
                                </button>
                              </form>
                            </div>
                          </td>
                        </tr>
                      );
                    })
                  ) : (
                    <tr>
                      <td colSpan={5}>
                        <div
                          className="rounded-3 border border-dashed bg-body-tertiary p-4 text-center"
                          data-testid="admin-users-page-empty-state"
                        >
                          <div className="mb-2">
                            <i
                              className="bi bi-person-plus fs-2 text-secondary"
                              aria-hidden="true"
                            />
                          </div>
                          <p className="fw-semibold mb-1">No users found.</p>
                          <p className="small text-secondary mb-0">
                            Users will appear here after they create an account.
                          </p>
                        </div>
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>
        </main>
      );
    },
  });
}
