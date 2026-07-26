"use server";

import { revalidatePath } from "next/cache";

import { assignableRoles } from "@/features/users/user-roles";
import { createLogger } from "@/utils/logger-server";
import { serverApiPut } from "@/utils/server-api";

const assignUserRoles = async (formData: FormData): Promise<void> => {
  const log = await createLogger("AssignUserRoles");
  const userId = String(formData.get("userId") ?? "").trim();
  const roles = formData
    .getAll("roles")
    .map((role) => String(role))
    .filter((role) => assignableRoles.includes(role));

  if (!userId) {
    await log.warn("Role assignment submitted without a user id");
    return;
  }

  const response = await serverApiPut({
    url: `api/users/${encodeURIComponent(userId)}/roles`,
    body: { roles },
  });

  if (response === "unauthorized") {
    await log.warn("Role assignment returned unauthorized", { userId });
    return;
  }

  if (!response.ok) {
    await log.warn("Role assignment request failed", {
      userId,
      statusCode: response.status,
    });
    return;
  }

  revalidatePath("/admin/users");
};

const setUserActivation = async (formData: FormData): Promise<void> => {
  const log = await createLogger("SetUserActivation");
  const userId = String(formData.get("userId") ?? "").trim();
  const isActive = String(formData.get("isActive") ?? "").trim();

  if (!userId || (isActive !== "true" && isActive !== "false")) {
    await log.warn("Activation update submitted with invalid payload", {
      userId,
      isActive,
    });
    return;
  }

  const response = await serverApiPut({
    url: `api/users/${encodeURIComponent(userId)}/activation`,
    body: { isActive: isActive === "true" },
  });

  if (response === "unauthorized") {
    await log.warn("Activation update returned unauthorized", { userId });
    return;
  }

  if (!response.ok) {
    await log.warn("Activation update request failed", {
      userId,
      statusCode: response.status,
    });
    return;
  }

  revalidatePath("/admin/users");
};

export { assignUserRoles, setUserActivation };
