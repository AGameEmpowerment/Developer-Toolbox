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
};

export async function createLogger() {
  return mockLogger;
}

export async function clientLog() {}

export async function clientTrackEvent() {}

export async function clientTrackException() {}

export async function clientTrackPageView() {}
