import { axe, toHaveNoViolations } from "jest-axe";
import * as matchers from "jest-extended";
import { expect } from "vitest";

expect.extend(matchers);
expect.extend(toHaveNoViolations);
global.axe = axe;

// Set environment variables
process.env.PRIVACY_POLICY_URL = "/privacy";
process.env.TERMS_OF_USE_URL = "/terms";
process.env.MESSAGE_TERMS_URL = "/message";
