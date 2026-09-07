import mockHelpPromise from "../../../../utils/mock-help-promise";

//flagResult is the flag you want from your service
const mockFeatureFlagService = ({
  flagResult = true,
  loadSucceed = true,
  loadDelay = 800,
}) => {
  const loadFlag = () => {
    return mockHelpPromise({
      succeed: loadSucceed,
      delay: loadDelay,
      result: { success: flagResult, error: "feature flag unavailable" },
    });
  };
  const loadFlagById = () => {
    return mockHelpPromise({
      succeed: loadSucceed,
      delay: loadDelay,
      result: {
        success: flagResult,
        error: "feature flag unavailable",
      },
    });
  };
  const reset = () => {};

  return {
    loadFlag,
    loadFlagById,
    reset,
  };
};

export default mockFeatureFlagService;
