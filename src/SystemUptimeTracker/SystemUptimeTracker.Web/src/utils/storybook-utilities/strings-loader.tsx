import React, { useState, useEffect, type ReactNode } from "react";

import loadStaticStrings from "./static-string-service";
import translationGroups from "./translation-groups";
import {
  StringsProvider,
  type StringsData,
} from "../localization/strings-provider";

const defaultFunc = () => undefined;
type StringsLoaderProps = {
  language?: string;
  children?: ReactNode | ((strings: StringsData) => ReactNode);
};

const StringsLoader = ({ language = "en", children }: StringsLoaderProps) => {
  const [strings, setStrings] = useState<StringsData>({});
  useEffect(() => {
    const load = async () => {
      const loadedStrings = await loadStaticStrings(
        language,
        translationGroups,
      );
      setStrings(loadedStrings);
    };
    load();
  }, [language]);

  if (typeof children !== "function") {
    return <StringsProvider data={strings}>{children}</StringsProvider>;
  }

  const renderStrings = children ?? defaultFunc;
  return (
    <StringsProvider data={strings}>{renderStrings(strings)}</StringsProvider>
  );
};

export default StringsLoader;
