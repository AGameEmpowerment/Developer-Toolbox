import { NextResponse } from "next/server";

import { canImpersonateForIdentifier } from "./impersonate-service";
import { encrypt } from "@/utils/encryption";
import { createLogger } from "@/utils/logger-server";

const Action = async (request, { params }) => {
  const log = await createLogger("ImpersonateApiRoute");
  const { id: identifier } = await params;

  const impersonateData = await canImpersonateForIdentifier(identifier);
  const { canImpersonate = false, data = null } = impersonateData || {};
  if (canImpersonate) {
    const encrypted = encrypt(data.accountId);
    //putting this on the cookie makes it so our api pass through
    //and our server api get will find this cookie and grab it.
    //note: this server decrypts it and we pass it to the back end api
    //(which is not publicly exposed, and is responsible for verifying
    // that the user can impersonate and all of that anyway). So we don't need to worry about
    //being able to decrypt this value anywhere other than this server which keeps things simpler.
    //The whole point of this encryption is to ensure that users who can sniff out this cookie and try to manipulate it
    //won't be able to get anything useful from it.

    const response = new NextResponse(null, {
      status: 200,
    });
    response.cookies.set(process.env.IMPERSONATING_COOKIE, encrypted, {
      httpOnly: true,
      path: "/",
      sameSite: "lax",
    });

    if (data) {
      response.cookies.set(
        `${process.env.IMPERSONATING_COOKIE}-data`,
        JSON.stringify(data),
        {
          path: "/",
          sameSite: "lax",
        },
      );
    }

    await log.info("Impersonation request approved", {
      identifier,
    });
    return response;
  } else {
    await log.warn("Impersonation request denied", {
      identifier,
    });
    return new Response("Forbidden", { status: 403 });
  }
};

const GET = Action;

export { GET };
