import { revalidatePath } from "next/cache";
import { NextResponse } from "next/server";

import { assignableRoles } from "@/features/users/user-roles";
import { createLogger } from "@/utils/logger-server";
import { serverApiPut } from "@/utils/server-api";

const adminUsersPath = "/admin/users";
const appBaseUrl = process.env.APP_BASE_URL;

const buildAdminUsersUrl = (request: Request) =>
  new URL(adminUsersPath, appBaseUrl || request.url);

export async function POST(request: Request) {
  const log = await createLogger("AssignUserRolesRoute");
  const formData = await request.formData();
  const userId = String(formData.get("userId") ?? "").trim();
  const roles = formData
    .getAll("roles")
    .map((role) => String(role))
    .filter((role) => assignableRoles.includes(role));

  if (!userId) {
    await log.warn("Role assignment submitted without a user id");
    return NextResponse.redirect(buildAdminUsersUrl(request), 303);
  }

  const response = await serverApiPut({
    url: `api/users/${encodeURIComponent(userId)}/roles`,
    body: { roles },
  });

  if (response === "unauthorized") {
    await log.warn("Role assignment returned unauthorized", { userId });
    return NextResponse.redirect(buildAdminUsersUrl(request), 303);
  }

  if (!response.ok) {
    await log.warn("Role assignment request failed", {
      userId,
      statusCode: response.status,
    });
    return NextResponse.redirect(buildAdminUsersUrl(request), 303);
  }

  revalidatePath(adminUsersPath);
  return NextResponse.redirect(buildAdminUsersUrl(request), 303);
}
