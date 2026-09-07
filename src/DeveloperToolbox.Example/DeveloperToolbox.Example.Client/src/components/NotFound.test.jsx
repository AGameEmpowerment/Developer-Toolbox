import NotFoundContent from "./NotFound";
import { genericComponentTests, getTestContext } from "@/utils/testHelpers";

const context = getTestContext();

describe("NotFoundContent", () => {
  genericComponentTests(context, NotFoundContent);
});
