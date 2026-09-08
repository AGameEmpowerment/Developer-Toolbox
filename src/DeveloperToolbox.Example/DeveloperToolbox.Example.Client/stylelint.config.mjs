import stylelintConfigRecommended from "stylelint-config-recommended";

/** @type {import("stylelint").Config} */
const stylelintConfig = {
  extends: [stylelintConfigRecommended],
  ignoreFiles: ["node_modules/**", ".next/**", "out/**", "coverage/**"],
};

export default stylelintConfig;
