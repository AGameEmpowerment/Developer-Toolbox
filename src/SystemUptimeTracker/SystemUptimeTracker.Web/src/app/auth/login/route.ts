import { auth } from "@/utils/auth/auth";

export const GET = async (request) => auth().login(request);

export const POST = async (request) => auth().submitLogin(request);
