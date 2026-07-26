import { useCallback, useState } from "react";

const useToggle = (initialState = false) => {
  const [active, setActive] = useState(initialState);
  const toggleActive = useCallback(
    () => setActive((oldState) => !oldState),
    [],
  );
  return [active, toggleActive];
};

export default useToggle;
