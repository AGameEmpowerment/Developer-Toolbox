import { useState, useEffect, useCallback, useRef } from "react";

type HoverDelayOptions = {
  mouseEnterDelay?: number;
  mouseLeaveDelay?: number;
  onHoveringChange?: (isHovering: boolean) => void;
};

const defaultFunc = () => undefined;

const useHoverDelay = ({
  mouseEnterDelay = 500,
  mouseLeaveDelay = 0,
  onHoveringChange = defaultFunc,
}: HoverDelayOptions) => {
  const [isHovering, setIsHovering] = useState<boolean>(false);
  const mouseEnterTimer = useRef<ReturnType<typeof setTimeout> | undefined>(
    undefined,
  );
  const mouseOutTimer = useRef<ReturnType<typeof setTimeout> | undefined>(
    undefined,
  );

  useEffect(() => {
    if (mouseEnterTimer.current) {
      clearTimeout(mouseEnterTimer.current);
    }
    if (mouseOutTimer.current) {
      clearTimeout(mouseOutTimer.current);
    }
  });

  const onMouseEnter = useCallback(() => {
    clearTimeout(mouseOutTimer.current);
    mouseEnterTimer.current = setTimeout(() => {
      setIsHovering(true);
      onHoveringChange(true);
    }, mouseEnterDelay);
  }, [mouseEnterDelay, onHoveringChange]);

  const onMouseLeave = useCallback(() => {
    clearTimeout(mouseEnterTimer.current);
    mouseOutTimer.current = setTimeout(() => {
      setIsHovering(false);
      onHoveringChange(false);
    }, mouseLeaveDelay);
  }, [mouseLeaveDelay, onHoveringChange]);

  return [
    isHovering,
    {
      onMouseEnter,
      onMouseLeave,
    },
  ] as const;
};

export default useHoverDelay;
