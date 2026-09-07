import js from "@eslint/js";
import nextPlugin from "@next/eslint-plugin-next";
import jsxA11y from "eslint-plugin-jsx-a11y";
import react from "eslint-plugin-react";
import reactHooks from "eslint-plugin-react-hooks";
import globals from "globals";

export default [
  {
    ignores: [".next/**", "node_modules/**", "coverage/**", "out/**"],
  },
  {
    files: ["**/*.{js,mjs,jsx}"],
    ...js.configs.recommended,
    languageOptions: {
      ecmaVersion: "latest",
      sourceType: "module",
      globals: { ...globals.node, ...globals.browser },
      parserOptions: { ecmaFeatures: { jsx: true } },
    },
  },
  {
    files: ["**/*.{js,jsx}"],
    plugins: {
      "@next/next": nextPlugin,
      "jsx-a11y": jsxA11y,
      react,
      "react-hooks": reactHooks,
    },
    rules: {
      ...nextPlugin.configs["core-web-vitals"].rules,
      ...jsxA11y.flatConfigs.recommended.rules,
      ...react.configs.flat.recommended.rules,
      ...react.configs.flat["jsx-runtime"].rules,
      ...reactHooks.configs.recommended.rules,
      "react/prop-types": "off",
    },
    settings: { react: { version: "detect" } },
  },
  {
    files: [
      "**/*.test.{js,jsx,mjs}",
      "**/testHelpers.{js,jsx}",
      "vitest.setup.mjs",
    ],
    languageOptions: { globals: globals.vitest },
  },
];
