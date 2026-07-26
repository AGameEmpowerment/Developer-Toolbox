import HelloWorld from "./hello-world";
import { genericTests, getTestContext } from "../../utils/testHelper.jsx";

const context = getTestContext();
describe("Main component", () => {
  genericTests(context, HelloWorld);
});
