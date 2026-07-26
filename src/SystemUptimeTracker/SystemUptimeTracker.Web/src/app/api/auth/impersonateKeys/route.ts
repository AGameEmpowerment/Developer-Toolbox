import { newKeys } from "@/utils/encryption";

const GET = async () => {
  const keys = await newKeys();
  return new Response(JSON.stringify(keys), { status: 200 });
};

export { GET };
