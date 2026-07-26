"use client";
import type { PropsWithChildren } from "react";
import { createContext, useContext } from "react";

// Create the context
const LangContext = createContext<string | undefined>(undefined);

// Create a provider component
export const LangProvider = ({
  lang,
  children,
}: PropsWithChildren<{ lang?: string }>) => {
  return <LangContext.Provider value={lang}>{children}</LangContext.Provider>;
};

// Create a custom hook to use the LangContext
export const useLang = () => {
  return useContext(LangContext);
};
