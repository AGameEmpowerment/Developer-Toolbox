const configuration = {
  setAutoCollectConsole: () => configuration,
  setAutoCollectExceptions: () => configuration,
  setAutoCollectPerformance: () => configuration,
  setAutoCollectPreAggregatedMetrics: () => configuration,
  setAutoCollectHeartbeat: () => configuration,
  enableWebInstrumentation: () => configuration,
  setAutoCollectRequests: () => configuration,
  setAutoCollectDependencies: () => configuration,
  setAutoDependencyCorrelation: () => configuration,
  setUseDiskRetryCaching: () => configuration,
  setInternalLogging: () => configuration,
  setAutoCollectIncomingRequestAzureFunctions: () => configuration,
  setSendLiveMetrics: () => configuration,
  setAzureMonitorOptions: () => configuration,
  start: () => configuration,
};

const defaultClient = {
  config: {},
  context: {
    keys: {},
    tags: {},
  },
  trackTrace: () => {},
  trackException: () => {},
  trackEvent: () => {},
  trackPageView: () => {},
  flush: (options) => options?.callback?.(),
  addTelemetryProcessor: () => {},
};

const appInsights = {
  defaultClient,
  setup: () => configuration,
  start: () => configuration,
  dispose: () => {},
  getCorrelationContext: () => null,
  startOperation: () => ({}),
  wrapWithCorrelationContext: (fn) => fn,
  Configuration: configuration,
};

export { configuration as Configuration, defaultClient };

export default appInsights;
