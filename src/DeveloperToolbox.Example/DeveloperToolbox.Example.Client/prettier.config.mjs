/** @type {import("prettier").Config} */
const prettierConfig = {
  trailingComma: "all",
  tabWidth: 2,
  semi: true,
  singleQuote: false,
  endOfLine: "crlf",
  overrides: [
    {
      files: "package.json",
      options: { endOfLine: "lf" },
    },
  ],
};

export default prettierConfig;
