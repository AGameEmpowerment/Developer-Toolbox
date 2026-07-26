// For more info, see https://github.com/storybookjs/eslint-plugin-storybook#configuration-flat-config-format
import js from "@eslint/js";
import importPlugin from "eslint-plugin-import";
import reactPlugin from "eslint-plugin-react";
import reactHooksPlugin from "eslint-plugin-react-hooks";
import storybook from "eslint-plugin-storybook";
import globals from "globals";
import tseslint from "typescript-eslint";

/** @type {import('eslint').Linter.Config} */
const eslintConfig = [
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    ignores: [".next/**", "coverage/**", "storybook-static/**"],
  },
  {
    files: ["**/*.{js,jsx,mjs,cjs,ts,tsx}"],
    languageOptions: {
      ecmaVersion: "latest",
      globals: {
        ...globals.browser,
        ...globals.node,
      },
      parserOptions: {
        ecmaFeatures: {
          jsx: true,
        },
        sourceType: "module",
      },
    },
    plugins: {
      import: importPlugin,
      react: reactPlugin,
      "react-hooks": reactHooksPlugin,
    },
    settings: {
      react: {
        version: "detect",
      },
    },
  },
  {
    files: ["**/*.{js,jsx,mjs,cjs,ts,tsx}"],
    plugins: {
      import: importPlugin,
      react: reactPlugin,
      "react-hooks": reactHooksPlugin,
    },
    rules: {
      "no-trailing-spaces": "error",
      "import/order": [
        "warn",
        {
          groups: ["builtin", "external", "internal"],
          pathGroups: [
            {
              pattern: "react",
              group: "external",
              position: "before",
            },
          ],
          pathGroupsExcludedImportTypes: ["react"],
          "newlines-between": "always",
          alphabetize: {
            order: "asc",
            caseInsensitive: true,
          },
        },
      ],
      "no-loss-of-precision": "error",
      "no-unreachable-loop": "error",
      "no-unsafe-optional-chaining": "error",
      "no-useless-backreference": "error",
      "react/jsx-uses-vars": "error",
      ...reactHooksPlugin.configs.recommended.rules,
      "react-hooks/set-state-in-effect": "off",
      "no-duplicate-imports": "off",
      "arrow-spacing": "error",
      "space-infix-ops": "error",
      "dot-notation": "warn",
      "no-alert": "error",
      "no-constructor-return": "error",
      "array-callback-return": "error",
      "import/first": "error",
      "import/newline-after-import": "error",
      "import/no-duplicates": "warn",
      "no-debugger": "error",
      "no-eval": "error",
      "no-lonely-if": "error",
      "no-param-reassign": [
        "warn",
        {
          props: true,
        },
      ],
      "no-return-assign": "error",
      "no-template-curly-in-string": "error",
      "no-unused-expressions": "error",
      "no-use-before-define": "warn",
      "prefer-const": "warn",
      "max-len": [
        "warn",
        {
          code: 160,
          ignoreComments: true,
          ignoreUrls: true,
          ignoreTemplateLiterals: true,
        },
      ],
      "import/no-anonymous-default-export": 0,
    },
  },
  {
    files: ["**/*.{ts,tsx}"],
    rules: {
      "no-undef": "off",
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": [
        "warn",
        {
          argsIgnorePattern: "^_",
          varsIgnorePattern: "^_",
        },
      ],
      "@typescript-eslint/no-explicit-any": "warn",
    },
  },
  {
    files: [
      "src/components/generic/client-only/client-only.js",
      "src/components/generic/hooks/use-countdown/use-countdown.js",
      "src/components/generic/hooks/use-feature-flag/use-feature-flag.js",
      "src/components/generic/hooks/use-interval/use-interval.js",
    ],
    plugins: {
      "react-hooks": reactHooksPlugin,
    },
    rules: {
      "react-hooks/set-state-in-effect": "off",
    },
  },
  {
    files: [
      "src/utils/testHelper.jsx",
      "**/*.{test,spec}.{js,jsx,ts,tsx}",
      "vitest.setup.mjs",
    ],
    languageOptions: {
      globals: {
        afterAll: "readonly",
        beforeEach: "readonly",
        beforeAll: "readonly",
        afterEach: "readonly",
        describe: "readonly",
        expect: "readonly",
        global: "readonly",
        it: "readonly",
        vi: "readonly",
      },
    },
  },
  ...storybook.configs["flat/recommended"],
];

export default eslintConfig;
