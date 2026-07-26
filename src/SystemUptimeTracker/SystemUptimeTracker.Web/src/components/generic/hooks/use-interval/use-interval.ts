import { useState, useEffect, useCallback } from "react";

type IntervalOptions = {
  milliseconds?: number;
  autoStart?: boolean;
  onTrigger?: () => void;
};

const defaultFunc = () => undefined;
const useInterval = ({
  milliseconds = 10000,
  autoStart = true,
  onTrigger = defaultFunc,
}: IntervalOptions) => {
  const [intervalId, setIntervalId] = useState<
    ReturnType<typeof setInterval> | undefined
  >(undefined);

  const restart = useCallback(() => {
    setIntervalId((curId) => {
      if (curId) {
        clearInterval(curId);
      }
      const id = setInterval(onTrigger, milliseconds);
      return id;
    });
  }, [milliseconds, onTrigger]);
  const stop = useCallback(() => {
    setIntervalId((curId) => {
      if (curId) {
        clearInterval(curId);
        return undefined;
      }
      return curId;
    });
  }, []);

  useEffect(() => {
    if (autoStart) {
      restart();
    }
  }, [restart, autoStart]);

  useEffect(() => {
    return () => {
      if (intervalId) {
        clearInterval(intervalId);
      }
    };
  }, [autoStart, restart, intervalId]);

  return {
    restart,
    stop,
  };
};

export default useInterval;
