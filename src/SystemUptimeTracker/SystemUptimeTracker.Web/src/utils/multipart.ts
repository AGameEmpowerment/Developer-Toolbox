export const sanitizeMultipartDispositionValue = (value: string): string =>
  value
    .replace(/[\r\n]+/g, " ")
    .replace(/"/g, "%22")
    .trim();
