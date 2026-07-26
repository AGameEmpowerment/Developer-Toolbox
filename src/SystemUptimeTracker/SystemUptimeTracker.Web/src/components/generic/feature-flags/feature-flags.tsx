import {
  Children,
  cloneElement,
  isValidElement,
  type ReactElement,
  type ReactNode,
} from "react";

import useFeatureFlag from "../hooks/use-feature-flag/use-feature-flag";

type FeatureFlagsProps = {
  flag: string;
  global?: boolean;
  service?: {
    loadFlag: (flag: string, global?: boolean) => Promise<unknown>;
    loadFlagById: (flag: string, id: number) => Promise<unknown>;
    reset: () => void;
  };
  children?: ReactNode;
};

const FeatureFlags = ({
  flag,
  global = false,
  service,
  children,
}: FeatureFlagsProps) => {
  const { status, clearStatus, loadedFlag } = useFeatureFlag(
    flag,
    -1,
    global,
    service,
  );

  const [flagOn, flagOff, loading, error] = Children.toArray(children);

  if (status.saving && loading) {
    return loading;
  }
  if (status.error && error) {
    if (isValidElement<{ clear?: () => void }>(error)) {
      return cloneElement(error as ReactElement<{ clear?: () => void }>, {
        clear: clearStatus,
      });
    }

    return error;
  }
  if (status.success && !loadedFlag && flagOff) {
    return flagOff;
  }
  if (status.success && loadedFlag && flagOn) {
    return flagOn;
  }
  return null;
};

export default FeatureFlags;
