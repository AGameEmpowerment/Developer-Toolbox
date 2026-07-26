import { auth } from "@/utils/auth/auth";

export const GET = async (request) => auth().createUser(request);

export const POST = async (request) => auth().submitCreateUser(request);
