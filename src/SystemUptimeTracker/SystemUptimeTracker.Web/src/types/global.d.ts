import "vitest";

declare global {
  interface Error {
    traceId?: string;
    statusCode?: number;
    statusText?: string;
    response?: {
      headers?: Headers;
    };
  }

  var axe:
    | ((container: Element) => Promise<{
        violations?: unknown[];
      }>)
    | undefined;
}

declare module "vitest" {
  interface Assertion<T = unknown> {
    toHaveNoViolations(): T;
  }

  interface AsymmetricMatchersContaining {
    toHaveNoViolations(): void;
  }
}
