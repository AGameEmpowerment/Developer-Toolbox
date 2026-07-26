import { auth } from "@/utils/auth/auth";

export const GET = async () => auth().logout();
