import Main from "./Main";
import { genericComponentTests, getTestContext } from "@/utils/testHelpers";

const context = getTestContext();

describe("Main", () => {
  genericComponentTests(context, Main, { children: "Sample content" });
});
