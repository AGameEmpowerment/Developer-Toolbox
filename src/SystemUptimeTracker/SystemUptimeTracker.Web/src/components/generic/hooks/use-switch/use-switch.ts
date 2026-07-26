import { useCallback, useState } from "react";

const useSwitch = (initialValue = false) => {
  const [status, setStatus] = useState(initialValue);

  const turnOn = useCallback(() => setStatus(true), [setStatus]);
  const turnOff = useCallback(() => setStatus(false), [setStatus]);

  return [status, turnOn, turnOff];
};

export default useSwitch;
