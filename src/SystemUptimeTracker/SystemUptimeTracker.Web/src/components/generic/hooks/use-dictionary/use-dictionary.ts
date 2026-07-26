import { useState, useCallback } from "react";

import { difference } from "../../../../utils/set-utils";

type DictionaryKey = string | number;
type DictionaryValue = unknown;
type DictionaryRecord = Record<string, DictionaryValue>;

const defaultArray: DictionaryKey[] = [];

const evaluateDictionary = (
  cur: DictionaryRecord,
  ids: DictionaryKey[],
  initialValue?: DictionaryValue | ((id: DictionaryKey) => DictionaryValue),
) => {
  const curIds = new Set<string>(Object.keys(cur));
  const normalizedIds = ids.map((id) => String(id));
  const newIds = new Set(normalizedIds);
  const idsToAdd = [...difference(newIds, curIds)] as string[];
  const idsToRemove = [...difference(curIds, newIds)] as string[];
  if (idsToAdd.length === 0 && idsToRemove.length === 0) {
    return cur;
  }

  const result = { ...cur };
  idsToRemove.forEach((d) => {
    delete result[d];
  });

  idsToAdd.forEach((id) => {
    let startData = initialValue;
    if (typeof initialValue === "function") {
      startData = initialValue(id);
    }
    result[id] = startData ?? {};
  });
  return result;
};

const useDictionary = ({
  ids: propsIds = defaultArray,
  initialValue,
}: {
  ids?: DictionaryKey[];
  initialValue?: DictionaryValue | ((id: DictionaryKey) => DictionaryValue);
}) => {
  const [dictionary, setDictionary] = useState<DictionaryRecord>(() =>
    evaluateDictionary({}, propsIds, initialValue),
  );

  const evaluateKeys = useCallback(
    (
      ids: DictionaryKey[],
      newInitial:
        | DictionaryValue
        | ((id: DictionaryKey) => DictionaryValue)
        | undefined = initialValue,
    ) => {
      setDictionary((cur) => {
        return evaluateDictionary(cur, ids, newInitial);
      });
    },
    [initialValue],
  );

  const setItem = useCallback((id: DictionaryKey, value: DictionaryValue) => {
    const normalizedId = String(id);
    setDictionary((cur) => {
      const updated = { ...cur };
      updated[normalizedId] = value;
      return updated;
    });
  }, []);
  const deleteItem = useCallback((id: DictionaryKey) => {
    const normalizedId = String(id);
    setDictionary((cur) => {
      if (!cur[normalizedId]) {
        return cur;
      }
      const updated = { ...cur };
      delete updated[normalizedId];
      return updated;
    });
  }, []);

  //this is really only intended to be used for clean up on useStatuses where we need to clear timeouts on each item
  const forEachItem = useCallback(
    (func: ((id: string, value: DictionaryValue) => void) | undefined) => {
      if (typeof func === "function") {
        for (const id in dictionary) {
          func(id, dictionary[id]);
        }
      }
    },
    [dictionary],
  );

  const find = useCallback(
    (func: ((value: DictionaryValue) => boolean) | undefined) => {
      if (typeof func === "function") {
        for (const id in dictionary) {
          const passes = func(dictionary[id]);
          if (passes) {
            return dictionary[id];
          }
        }
      }
      return undefined;
    },
    [dictionary],
  );

  const getItem = useCallback(
    (id: DictionaryKey) => {
      return dictionary[String(id)];
    },
    [dictionary],
  );

  return {
    evaluateKeys,
    getItem,
    setItem,
    deleteItem,
    forEachItem,
    find,
  };
};

export default useDictionary;
