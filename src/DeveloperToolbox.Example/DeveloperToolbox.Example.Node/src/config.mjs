import { readFile } from "node:fs/promises";
import { isIP } from "node:net";

export async function loadSettings(path = new URL("../../../../containers/examples.settings.json", import.meta.url), environment = process.env) {
  let settings = {};
  try {
    settings = JSON.parse(await readFile(path, "utf8"));
  } catch (error) {
    if (error.code !== "ENOENT") throw error;
  }
  if (!settings || Array.isArray(settings) || typeof settings !== "object") throw new Error("Settings must be a JSON object.");
  return { ...settings, ...environment };
}

export function localUrl(value, names = [], { protocols = ["http:", "https:"], path = false } = {}) {
  let url;
  try { url = new URL(value); } catch { throw new Error("A local emulator endpoint is required."); }
  const host = url.hostname.replace(/^\[|\]$/g, "").toLowerCase();
  const loopback = host === "localhost" || host === "::1" || (isIP(host) === 4 && host.startsWith("127."));
  if (!protocols.includes(url.protocol) || (!loopback && !names.includes(host)) || url.username || url.password || url.search || url.hash || (!path && !["", "/"].includes(url.pathname))) {
    throw new Error("Only local toolbox emulator endpoints are allowed.");
  }
  return url;
}

function connectionFields(value, allowed) {
  const fields = {};
  const seen = new Set();
  for (const part of value.split(";").filter(Boolean)) {
    const separator = part.indexOf("=");
    const key = part.slice(0, separator);
    if (separator < 1 || !allowed.includes(key) || seen.has(key.toLowerCase())) throw new Error("Ambiguous or unsupported local connection setting.");
    seen.add(key.toLowerCase());
    fields[key] = part.slice(separator + 1);
  }
  return fields;
}

export function storageConnection(settings) {
  const value = settings.Resources__AzureStorage__ConnectionString || settings.ToolboxAzureStorage;
  if (!value || /^UseDevelopmentStorage=/i.test(value)) throw new Error("Run python setup/configure_examples.py to generate the explicit local Azure Storage connection.");
  const fields = connectionFields(value, ["DefaultEndpointsProtocol", "AccountName", "AccountKey", "BlobEndpoint", "QueueEndpoint", "TableEndpoint"]);
  for (const name of ["BlobEndpoint", "QueueEndpoint", "TableEndpoint"]) localUrl(fields[name], ["azurite"], { path: true });
  if (!fields.AccountName || !fields.AccountKey || fields.SharedAccessSignature) throw new Error("An explicit local Azure Storage account and key are required.");
  return value;
}

export function serviceBusConnection(settings) {
  const value = settings.ServiceBusConnection || "";
  const fields = connectionFields(value, ["Endpoint", "SharedAccessKeyName", "SharedAccessKey", "UseDevelopmentEmulator"]);
  localUrl(fields.Endpoint, ["service-bus"], { protocols: ["sb:"] });
  if (fields.UseDevelopmentEmulator?.toLowerCase() !== "true") throw new Error("Service Bus requires UseDevelopmentEmulator=true.");
  return value;
}

export function workMessage(value, language = "node") {
  if (!value || typeof value !== "object" || Array.isArray(value) || typeof value.id !== "string" || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value.id) || value.language !== language || typeof value.text !== "string" || !value.text.trim() || value.text.length > 1024) throw new Error("Work requires a UUID id, the matching language, and text of 1–1024 characters.");
  return { id: value.id, language, text: value.text };
}
