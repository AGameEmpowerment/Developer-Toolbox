import { useEffect, useState, useCallback } from "react";

import featureFlagService from "./feature-flag-service";
import useStatus from "../use-status/use-status";

type FeatureFlagService = {
  loadFlag: (flag: string, global?: boolean) => Promise<unknown>;
  loadFlagById: (flag: string, id: number) => Promise<unknown>;
  reset: () => void;
};

type FeatureFlagRequest = {
  flag: string;
  id?: number;
  global?: boolean;
};

const defaultService: FeatureFlagService = featureFlagService();

const useFeatureFlag = (
  flag: string,
  id = -1,
  global = false,
  service: FeatureFlagService = defaultService,
) => {
  const {
    status,
    setSavingStatus: loading,
    setErrorStatus: error,
    setSuccessStatus: done,
    setResetStatus: clearStatus,
  } = useStatus(undefined, false);

  const [resetCount, setResetCount] = useState<number>(0);
  const [loadedFlag, setLoadedFlag] = useState<unknown>(undefined);

  const onKeyDown = useCallback(
    (e: KeyboardEvent) => {
      if (e.keyCode === 192) {
        service.reset();
        setResetCount((cur) => cur + 1);
      }
    },
    [service],
  );

  useEffect(() => {
    window.addEventListener("keydown", onKeyDown);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [onKeyDown]);

  const load = useCallback(async () => {
    try {
      loading();
      const data =
        id > -1
          ? await service.loadFlagById(flag, id)
          : await service.loadFlag(flag, global);
      setLoadedFlag(data);
      done();
    } catch (e) {
      error(e);
    }
  }, [flag, id, global, service, loading, error, done]);

  useEffect(() => {
    load();
  }, [load, resetCount]);

  return {
    status,
    clearStatus,
    loadedFlag,
  };
};

const defaultArray: FeatureFlagRequest[] = [];
const useFeatureFlagCollection = (
  flags = defaultArray,
  service: FeatureFlagService = defaultService,
) => {
  const {
    status,
    setSavingStatus: loading,
    setErrorStatus: error,
    setSuccessStatus: done,
    setResetStatus: clearStatus,
  } = useStatus(undefined, false);

  const [resetCount, setResetCount] = useState<number>(0);
  const [loadedFlags, setLoadedFlags] = useState<string[]>([]);

  const onKeyDown = useCallback(
    (e: KeyboardEvent) => {
      if (e.keyCode === 192) {
        service.reset();
        setResetCount((cur) => cur + 1);
      }
    },
    [service],
  );

  useEffect(() => {
    window.addEventListener("keydown", onKeyDown);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [onKeyDown]);

  const load = useCallback(async () => {
    try {
      loading();
      const newFlagResults: string[] = [];
      for (const flagRequest of flags) {
        const { flag, id = -1, global = false } = flagRequest;
        const result =
          id > -1
            ? await service.loadFlagById(flag, id)
            : await service.loadFlag(flag, global);
        if (result) {
          const useFlag = id > -1 ? `${flag}_${id}` : flag;
          newFlagResults.push(useFlag);
        }
      }

      setLoadedFlags(newFlagResults);
      done();
    } catch (e) {
      error(e);
    }
  }, [flags, service, loading, error, done]);

  useEffect(() => {
    load();
  }, [load, resetCount]);

  return {
    status,
    clearStatus,
    loadedFlags,
  };
};

export { useFeatureFlagCollection };

export default useFeatureFlag;
