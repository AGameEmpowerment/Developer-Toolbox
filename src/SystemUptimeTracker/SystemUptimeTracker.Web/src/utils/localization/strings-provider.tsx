"use client";
import type { PropsWithChildren } from "react";
import {
  createContext,
  useContext,
  useMemo,
  useCallback,
  useState,
  useEffect,
} from "react";

import { z } from "zod";

import { safeLookupString } from "./safe-lookup-string";
import { createClientLogger } from "@/utils/logger-client";

type StringGroup = Record<string, string>;
export type StringsData = Record<string, StringGroup>;
type StringsContextValue = {
  strings: StringsData;
  changeLanguage: (lang: string) => Promise<void>;
};

const StringsContext = createContext<StringsContextValue | undefined>(
  undefined,
);
const log = createClientLogger("StringsProvider");

const useStringsHook = ({ data: startData }: { data: StringsData }) => {
  const [strings, setStrings] = useState<StringsData>(startData);
  useEffect(() => {
    setStrings(startData);
  }, [startData]);

  const changeLanguage = useCallback(
    async (lang) => {
      try {
        //api call that sets the language cookie and returns a 200
        await fetch("/api/language/change?lang=" + lang);
        //if we succeeded in changing language, we do a request to load our strings now
        const groups = Object.keys(strings || {});
        if (groups.length === 0) {
          await log.warn(
            "No localization groups were available for client language change",
          );
          return;
        }
        const response = await fetch(
          `/api/strings?lang=${lang}&groups=${groups.join(",")}`,
        );
        if (!response.ok) {
          throw new Error(`Error fetching strings: ${response.statusText}`);
        }
        const result = await response.json();
        setStrings(result);
        await log.info("Client language changed successfully", {
          lang,
          groupCount: groups.length,
        });
      } catch (error) {
        await log.error("Failed to change client language", error, {
          lang,
        });
      }
    },
    [strings],
  );
  return {
    strings,
    changeLanguage,
  };
};

const defaultObj: StringsData = {};
const StringsProvider = ({
  children,
  data = defaultObj,
}: PropsWithChildren<{ data?: StringsData }>) => {
  const hook = useStringsHook({ data });
  return (
    <StringsContext.Provider value={hook}>{children}</StringsContext.Provider>
  );
};
const groupSchema = z.object({
  group: z.string(),
  keys: z.array(z.string()),
});

const dataSchema = z.array(groupSchema);

const defaultArray: Array<{ group: string; keys: string[] }> = [];
const useStringsProvider = ({
  groups = defaultArray,
}: {
  groups?: Array<{ group: string; keys: string[] }>;
}) => {
  const hook = useContext(StringsContext); //this is all the strings we have
  if (!hook) {
    throw new Error(
      "useStringsProvider must be used within a StringsProvider.",
    );
  }

  const { strings, changeLanguage } = hook;

  const flattenedStrings = useMemo(() => {
    if (!dataSchema.safeParse(groups).success) {
      return {};
    }

    const result: Record<string, string> = {};
    groups.forEach((group) => {
      group.keys.forEach((key) => {
        result[key] = safeLookupString(strings, group.group, key);
      });
    });
    return result;
  }, [groups, strings]);

  return {
    strings,
    flattenedStrings,
    changeLanguage,
  };
};

export { StringsProvider, useStringsProvider };
