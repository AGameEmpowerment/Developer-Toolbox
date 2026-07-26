# Localization

We recommend grouping strings conceptually so you can limit the text dumps dropped to a given page or component.

## File name convention

When you submit to lingoport, they will just know the file name, and will ask questions about a key in a file name. Thus it is best if your file actually share the name of the parent folder to make it easier to find the key and track down it's utilization. For that reason, this is coded up to load strings expecting the them to be in strings/{grouping}/{grouping}.{lang}.js

**Note**: For your first submission to lingoport, make sure to delete all the json files that are not .en.json. If you test with some dummy files in other languages, lingoport will accept those values as valid translations.

## Loading

The strings will load with an inteligent fallback to english when a key is missing in your loaded language.

## Supported Languages

A file exists in utils/localization/app-languages.js that defines the languages you are planning to support for the application. This should be used in any menus you create in your application. It is also referenced by storybook to build the control in storybook to change languages.

## localization utilities

### app-languages

Described in the supported languages section above

### change-language

Exports a function used to change the cookie value that tells the app what language to get. Note that change language will also reload the page.

### lang-provider

Provider as described in the confluence documentation that makes the active language available to the application.

### load-strings

Has the methods used server side to get translation strings. A server page will typically have these two calls near the very first actions invoked:
const lang = await detectLanguageServerSide();
const strings = await loadStrings(lang, ["shared", "languages"]);

### safe-lookup-strings

A simple method to look up strings for server-side use, or for client use if you choose not to use the flattenedStrings object the provider gives you. Note that the flattenedStrings object uses safe lookup in its implementation.

### strings-provider

The strings provider gives you a hook that will tie into the strings as given to the web application and storybook. You pass it an array of desired strings you are going to want to render in that component. Any localized client component can use that provider pattern to request only the string groups it needs.
