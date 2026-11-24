import { loadSettings } from "./config.mjs";
import { createResourceSamples, resourceNames } from "./resources.mjs";

const args = process.argv.slice(2);
function takeOption(name, index) {
  const value = args[index + 1];
  if (!value || value.startsWith("-")) throw new Error(`${name} requires a non-option value.`);
  args.splice(index, 2);
  return value;
}
const options = new Map();
for (let index = 0; index < args.length;) {
  const name = args[index];
  if (name === "--settings" || name === "--text") {
    if (options.has(name)) throw new Error(`${name} may only be specified once.`);
    options.set(name, takeOption(name, index));
  } else { index++; }
}
const settingsPath = options.get("--settings");
const text = options.get("--text") ?? "Hello from Node.js";
const samples = createResourceSamples(await loadSettings(settingsPath));
try {
  for (const resource of args.length ? args : resourceNames) {
    try { console.log(JSON.stringify({ resource, success: true, result: await samples.run(resource, text) })); }
    catch (error) { console.log(JSON.stringify({ resource, success: false, error: error instanceof RangeError ? error.message : "Local resource validation failed; check its configuration and running container." })); process.exitCode = 1; }
  }
} finally { await samples.close(); }
