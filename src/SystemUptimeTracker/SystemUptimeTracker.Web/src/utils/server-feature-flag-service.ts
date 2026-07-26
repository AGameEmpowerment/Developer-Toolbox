"use server";
import { serverApiGetAsJson } from "./server-api-get";

const isFeatureFlagOn = async (featureFlag, global = false) => {
  const data = await serverApiGetAsJson(
    {
      url: `api/FeatureFlags/Feature?flag=${featureFlag}&global=${global}`,
    },
    "feature-flag-service/isFeatureFlagOn",
  );
  return data === true;
};

export { isFeatureFlagOn };
