import { useState, useEffect, useCallback } from "react";

import useInterval from "../use-interval/use-interval";

type CountdownOptions = {
  seconds?: number;
  onComplete?: () => void;
  onTick?: (remaining: number) => void;
  autoStart?: boolean;
  loop?: boolean;
};

const defaultFunc = () => undefined;

const useCountdown = ({
  seconds = 300, //default to 5 minutes
  onComplete = defaultFunc,
  onTick = defaultFunc,
  autoStart = true,
  loop = false,
}: CountdownOptions) => {
  const [remaining, setRemaining] = useState<number>(seconds);

  const onSecondTick = useCallback(() => {
    setRemaining((r) => {
      const newRemaining = Math.max(r - 1, 0);
      onTick(newRemaining);
      return newRemaining;
    });
  }, [onTick]);

  const { stop: stopTicker, restart: restartTicker } = useInterval({
    milliseconds: 1000,
    onTrigger: onSecondTick,
    autoStart,
  });

  const restart = useCallback(() => {
    setRemaining(seconds);
    restartTicker();
  }, [restartTicker, seconds]);

  useEffect(() => {
    if (remaining <= 0) {
      onComplete();
      if (!loop) {
        stopTicker();
      } else {
        restart();
      }
    }
  }, [loop, onComplete, remaining, restart, stopTicker]);

  return {
    remaining,
    stop: stopTicker,
    restart,
  };
};

export default useCountdown;
