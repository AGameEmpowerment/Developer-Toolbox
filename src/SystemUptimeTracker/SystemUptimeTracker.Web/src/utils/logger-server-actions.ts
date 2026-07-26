"use server";

// Server action wrappers that delegate to the core server logger. These wrappers
// satisfy Next.js "use server" constraints by exporting only async functions.

import {
  clientLog as coreClientLog,
  clientTrackEvent as coreClientTrackEvent,
  clientTrackException as coreClientTrackException,
  clientTrackPageView as coreClientTrackPageView,
} from "./logger-server";

export async function clientLog(
  level,
  message,
  category = "",
  properties = {},
) {
  return coreClientLog(level, message, category, properties);
}

export async function clientTrackEvent(
  eventName,
  properties = {},
  measurements = {},
) {
  return coreClientTrackEvent(eventName, properties, measurements);
}

export async function clientTrackException(
  message,
  stack = "",
  severityLevel = 3,
  properties = {},
) {
  return coreClientTrackException(message, stack, severityLevel, properties);
}

export async function clientTrackPageView(
  name,
  url,
  properties = {},
  measurements = {},
) {
  return coreClientTrackPageView(name, url, properties, measurements);
}
