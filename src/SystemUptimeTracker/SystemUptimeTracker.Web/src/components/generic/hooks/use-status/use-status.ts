import { useEffect, useState, useCallback } from "react";

export type StatusState = {
  saving: boolean;
  error: boolean | unknown;
  success: boolean;
  warn: boolean;
  data?: unknown;
};

export const initialStatus: StatusState = {
  saving: false,
  error: false,
  success: false,
  warn: false,
};
export const savingStatus: StatusState = {
  saving: true,
  error: false,
  success: false,
  warn: false,
};
export const successStatus: StatusState = {
  saving: false,
  error: false,
  success: true,
  warn: false,
};
export const warnStatus: StatusState = {
  saving: false,
  error: false,
  success: false,
  warn: true,
};

const useStatus = (
  initialValue: StatusState = initialStatus,
  clearOnSuccess = true,
) => {
  const [status, setStatus] = useState<StatusState>(initialValue);

  const setResetStatus = useCallback(() => setStatus(initialStatus), []);
  const setSavingStatus = useCallback(
    (data?: unknown) => setStatus({ ...savingStatus, data }),
    [],
  );
  const setSuccessStatus = useCallback(() => setStatus(successStatus), []);
  const setWarnStatus = useCallback(() => setStatus(warnStatus), []);
  const setErrorStatus = useCallback(
    (error: unknown) =>
      setStatus({ saving: false, error, success: false, warn: false }),
    [],
  );

  useEffect(() => {
    if (status.success && clearOnSuccess) {
      const timeout = setTimeout(() => {
        setStatus((oldStatus) => ({ ...oldStatus, success: false }));
      }, 2000);
      return () => {
        if (timeout) {
          clearTimeout(timeout);
        }
      };
    }
  }, [status, clearOnSuccess]);

  return {
    status,
    setSavingStatus,
    setErrorStatus,
    setSuccessStatus,
    setResetStatus,
    setWarnStatus,
  };
};
export default useStatus;
