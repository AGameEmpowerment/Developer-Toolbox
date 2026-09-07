# Contributing

Use Node.js 24 and install dependencies from the public npm registry. Run
`npm run verify` before submitting changes. Keep environment secrets out of
source control and add regression tests for behavior changes.

Localization is implemented with JSON dictionaries and request-language
negotiation. Update `src/utils/i18n.js` and `src/dictionaries/index.js` together
when adding a locale. See the [Next.js internationalization guide](https://nextjs.org/docs/app/guides/internationalization).
