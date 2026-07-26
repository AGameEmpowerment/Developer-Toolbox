"use client";
const featureFlagService = () => {
  const storage = typeof window !== "undefined" ? window.sessionStorage : null;
  const key = "feature_flags";

  const getFlagFromStorage = (flag, id = -1, global = false) => {
    const flagsObjstr = storage.getItem(key);
    let flagsObj = {};
    try {
      flagsObj = JSON.parse(flagsObjstr);
    } catch {
      flagsObj = {};
    }
    if (!flagsObj) {
      flagsObj = {};
    }
    let prop = global ? `global_${flag}` : flag;
    if (id > -1) {
      prop = `${prop}_${id}`;
    }
    return flagsObj[prop];
  };
  const updateFlagStorage = (flag, id = -1, global = false, value = null) => {
    if (value === null) {
      return;
    }
    const flagsObjstr = storage.getItem(key);
    let flagsObj = {};
    try {
      flagsObj = JSON.parse(flagsObjstr);
    } catch {
      flagsObj = {};
    }
    if (flagsObj === null || flagsObj === undefined) {
      flagsObj = {};
    }
    let prop = global ? `global_${flag}` : flag;
    if (id > -1) {
      prop = `${prop}_${id}`;
    }
    flagsObj[prop] = value;
    const updatedFlagsObjStr = JSON.stringify(flagsObj);
    storage.setItem(key, updatedFlagsObjStr);
  };

  const loadFlag = async (flag, global = false) => {
    try {
      const storedFlag = getFlagFromStorage(flag, -1, global);

      if (storedFlag === true || storedFlag === false) {
        return storedFlag;
      }
      const url = `/api/FeatureFlags/Feature?flag=${flag}&global=${global}`;
      const response = await fetch(url, { method: "GET" });
      if (response?.status !== 200) {
        throw new Error("Failed to load feature flag");
      }
      const result = await response.json();
      updateFlagStorage(flag, -1, global, result);
      return result;
    } catch {
      return null;
    }
  };

  const loadFlagById = async (flag, id) => {
    try {
      const storedFlag = getFlagFromStorage(flag, id);

      if (storedFlag === true || storedFlag === false) {
        return storedFlag;
      }
      const url = `/api/features/byId?flag=${flag}&id=${id}`;
      const result = await fetch(url, { method: "GET" });
      updateFlagStorage(flag, id, false, result);
      return result;
    } catch {
      return null;
    }
  };

  const reset = () => {
    storage.removeItem(key);
  };

  return {
    loadFlag,
    loadFlagById,
    reset,
  };
};

export default featureFlagService;
