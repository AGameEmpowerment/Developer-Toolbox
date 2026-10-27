import { app } from "@azure/functions";
import { loadSettings } from "../config.mjs";
import { createResourceSamples } from "../resources.mjs";
import { dispatch } from "../handlers.mjs";

export const samples = createResourceSamples(await loadSettings());
app.http("nodeResourceSample", { route: "samples/{resource}", methods: ["POST"], authLevel: "function", handler: async (request) => {
  let input;
  try { input = await request.json(); } catch { return { status: 400, jsonBody: { error: "Invalid JSON body." } }; }
  return dispatch(request.params.resource, input, samples);
} });
