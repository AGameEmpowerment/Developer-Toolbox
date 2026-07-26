"use server";
import { headers, cookies } from "next/headers";
import { redirect } from "next/navigation";

import { routeHeaderName } from "@/utils/request-context";

const setCookieAndReload = async (cookieName, cookieValue, secure = false) => {
  const c = await cookies();
  c.set({
    name: cookieName,
    value: cookieValue,
    httpOnly: secure,
    path: "/",
  });
  const headersList = await headers();
  const route = headersList.get(routeHeaderName);
  redirect(route);
};

export { setCookieAndReload };
