const mockHelpPromise = ({
  succeed = true,
  delay = 3000,
  result = { success: true, error: "aaah" },
}) => {
  const p = new Promise((resolve, reject) => {
    if (delay >= 0) {
      setTimeout(() => {
        if (succeed) {
          resolve(result.success);
        } else {
          reject(new Error(result.error));
        }
      }, delay);
    }
  });
  return p;
};

export default mockHelpPromise;
