import { app } from "@azure/functions";
import { workMessage } from "../config.mjs";
import { samples } from "./samples-post.mjs";

app.http("nodeEnqueueWork", { route: "work/{target}", methods: ["POST"], authLevel: "function", handler: async (request) => {
  let data;
  try { data = workMessage(await request.json()); } catch { return { status: 400, jsonBody: { error: "Invalid work message." } }; }
  if (!["servicebus", "storage-queue", "sqs"].includes(request.params.target)) return { status: 404, jsonBody: { error: "Unknown work target." } };
  return { status: 202, jsonBody: await samples.enqueueWork(data, request.params.target) };
} });
