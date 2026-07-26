/** @type { import('@storybook/nextjs').Preview } */
import languages from "../src/utils/localization/app-languages";
import StringsLoader from "../src/utils/storybook-utilities/strings-loader";

const preview = {
  decorators: [
    (Story, context) => {
      const { language = "en" } = context?.globals ?? {};
      return (
        <div>
          <StringsLoader language={language}>
            <Story />
          </StringsLoader>
        </div>
      );
    },
  ],
  parameters: {
    controls: {
      matchers: {
        color: /(background|color)$/i,
        date: /Date$/i,
      },
    },
    nextjs: {
      appDirectory: true,
    },
  },
};
const items = languages.map((l) => ({
  value: l.code,
  title: `${l.englishName} (${l.name})`,
}));
export const globalTypes = {
  language: {
    name: "Language",
    description: "Language code for localization",
    defaultValue: "en",
    toolbar: {
      icon: "globe",
      items,
    },
  },
};

export default preview;
