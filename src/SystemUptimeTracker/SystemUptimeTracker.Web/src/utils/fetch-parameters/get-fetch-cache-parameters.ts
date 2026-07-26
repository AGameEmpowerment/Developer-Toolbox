export default function getFetchCacheParameters(minutes) {
  const seconds = Math.trunc(minutes * 60);
  if (seconds <= 0) {
    return {
      cache: "no-store",
    };
  }
  return {
    next: {
      revalidate: seconds,
    },
  };
}
