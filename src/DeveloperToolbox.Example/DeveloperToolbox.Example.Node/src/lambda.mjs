import { loadSettings } from "./config.mjs";
import { createResourceSamples } from "./resources.mjs";
import { createLambdaHandler } from "./handlers.mjs";

const samples = createResourceSamples(await loadSettings());
export const handler = createLambdaHandler(samples);
