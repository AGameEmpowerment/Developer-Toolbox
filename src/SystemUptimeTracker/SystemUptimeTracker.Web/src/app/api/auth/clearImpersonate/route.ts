import { NextResponse } from "next/server";

import { createLogger } from "@/utils/logger-server";

const cookieName = process.env.IMPERSONATING_COOKIE;

const GET = async () => {
  const log = await createLogger("ClearImpersonateApiRoute");
  const response = new NextResponse(null, {
    status: 200,
  });
  response.cookies.delete(cookieName);
  response.cookies.delete(`${cookieName}-data`);

  await log.info("Cleared impersonation cookies");
  return response;
};

export { GET };
