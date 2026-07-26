import fs from "node:fs";
import path from "path";

import { languages } from "@/utils/localization/app-languages";

const fallbackLanguage = "en";
const stringGroupCache = new Map();

const mapLanguageCode = (code) => {
  if (code?.length > 2) {
    const language = languages.find((lang) => lang.code3 === code);
    if (language) {
      return language.code;
    }
  }

  return code;
};

const normalizeLanguageCode = (languageCode) => {
  const normalizedLanguageCode = languageCode
    ? languageCode.split("-")[0]
    : fallbackLanguage;
  const mappedLanguageCode = mapLanguageCode(normalizedLanguageCode);

  return mappedLanguageCode.length === 3
    ? mapLanguageCode(mappedLanguageCode)
    : mappedLanguageCode;
};

const loadFileData = async (group, languageCode) => {
  const filePath = path.join(
    process.cwd(),
    "public",
    "strings",
    group,
    `${group}.${languageCode}.json`,
  );

  try {
    if (!stringGroupCache.has(filePath)) {
      stringGroupCache.set(
        filePath,
        fs.promises
          .readFile(filePath, "utf8")
          .then((fileContents) => JSON.parse(fileContents)),
      );
    }

    const jsonData = await stringGroupCache.get(filePath);

    return {
      data: jsonData,
      found: true,
    };
  } catch {
    stringGroupCache.delete(filePath);

    return {
      data: {},
      found: false,
    };
  }
};

const loadStringGroups = async (groups, languageCode, log) => {
  const groupArray = Array.isArray(groups)
    ? groups.map((group) => group?.trim()).filter(Boolean)
    : [];
  const finalLanguageCode = normalizeLanguageCode(languageCode);
  const results = {};

  for (const group of groupArray) {
    const englishResult = await loadFileData(group, fallbackLanguage);
    let languageResult = {
      data: {},
      found: false,
    };

    if (!englishResult.found) {
      await log?.error?.("Fallback localization strings were not found", {
        group,
        fallbackLanguage,
      });
    }

    if (finalLanguageCode !== fallbackLanguage) {
      languageResult = await loadFileData(group, finalLanguageCode);

      if (!languageResult.found) {
        await log?.warn?.("Requested localization strings were not found", {
          group,
          requestedLanguage: finalLanguageCode,
          fallbackLanguage,
        });
      }
    }

    results[group] = {
      ...englishResult.data,
      ...languageResult.data,
    };
  }

  return results;
};

export { loadStringGroups };
