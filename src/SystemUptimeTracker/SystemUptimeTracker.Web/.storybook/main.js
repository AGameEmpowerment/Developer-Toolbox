import path from "node:path";
import { fileURLToPath } from "node:url";

const dirname = path.dirname(fileURLToPath(import.meta.url));

/** @type { import('@storybook/nextjs').StorybookConfig } */
const config = {
  stories: [
    "../src/**/*.mdx",
    "../src/**/*.stories.@(js|jsx|mjs|ts|tsx)",
    "../src/**/*dev.story.@(js|jsx|ts|tsx)",
  ],
  addons: ["@chromatic-com/storybook", "@storybook/addon-docs"],
  staticDirs: ["../public/strings"],
  framework: "@storybook/nextjs",
  webpackFinal: async (webpackConfig) => {
    return {
      ...webpackConfig,
      resolve: {
        ...(webpackConfig.resolve ?? {}),
        alias: {
          ...(webpackConfig.resolve?.alias ?? {}),
          "@/utils/logger-client": path.resolve(
            dirname,
            "logger-client.mock.js",
          ),
          "@/utils/logger-server": path.resolve(
            dirname,
            "logger-server.mock.js",
          ),
          "@/utils/logger-server-actions": path.resolve(
            dirname,
            "logger-server-actions.mock.js",
          ),
          applicationinsights: path.resolve(
            dirname,
            "applicationinsights.mock.js",
          ),
        },
      },
    };
  },
};
export default config;
