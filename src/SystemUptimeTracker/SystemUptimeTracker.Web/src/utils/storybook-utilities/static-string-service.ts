//see server/strings/readme.md for information regarding how this service and our strings files interrelate.
const defaultLangCode = "en";

const getPath = (language, folderName, containingFolder = null) => {
  //updated style changed the file from folder/strings.en.json in any folder, to folder/folder.en.json to make communicating with translators easier
  const path = `${folderName}/${folderName}.${language}.json`;
  return containingFolder ? `/${containingFolder}/${path}` : path;
};
const loadFile = async (language, folderName, containingFolder = null) => {
  const filepath = getPath(language, folderName, containingFolder);
  try {
    const response = await fetch(filepath);
    const json = await response.json();
    return { [folderName]: json };
  } catch {
    return {};
  }
};

const reduceFn = (obj, item) => ({
  ...obj,
  ...item,
});

const loadStaticStrings = async (
  language = "en",
  files = ["shared", "home"],
  containingFolder = null,
) => {
  try {
    if (!files || files.length <= 0) {
      throw new Error("invalid files parameter");
    }

    const defaultResults =
      language !== defaultLangCode
        ? await Promise.all(
            files.map(async (f) =>
              loadFile(defaultLangCode, f, containingFolder),
            ),
          )
        : [];
    const results = defaultResults.reduce(reduceFn, {});

    const langResults = await Promise.all(
      files.map(async (f) => loadFile(language, f, containingFolder)),
    );
    const langObj = langResults.reduce(reduceFn, {});

    for (const prop in langObj) {
      results[prop] = { ...results[prop], ...langObj[prop] };
    }
    return results;
  } catch (e) {
    console.log(
      `Failure to load strings for language ${language}, and files: ${files} with error: ${e}`,
    );
    return {};
  }
};

export default loadStaticStrings;
