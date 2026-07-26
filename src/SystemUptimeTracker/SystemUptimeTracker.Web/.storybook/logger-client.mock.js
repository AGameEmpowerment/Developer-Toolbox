const noopAsync = async () => {};

const mockLogger = {
  trace: noopAsync,
  debug: noopAsync,
  info: noopAsync,
  information: noopAsync,
  warn: noopAsync,
  warning: noopAsync,
  error: noopAsync,
  critical: noopAsync,
  trackEvent: noopAsync,
  trackPageView: noopAsync,
};

export function initializeLogger() {}

export function createClientLogger() {
  return mockLogger;
}

export async function logInfo() {}

export async function logWarn() {}

export async function logError() {}

export async function trackEvent() {}

export async function trackPageView() {}

export async function trackException() {}
