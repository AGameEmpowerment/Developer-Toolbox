import "server-only";

const dictionaries = {
  en: () => import("./en.json").then((module) => module.default),
  es: () => import("./es.json").then((module) => module.default),
  fr: () => import("./fr.json").then((module) => module.default),
  pt: () => import("./pt.json").then((module) => module.default),
};

export async function getDictionary(locale) {
  return (dictionaries[locale] || dictionaries.en)();
}
