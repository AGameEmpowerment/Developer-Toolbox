"use client";

import type { PropsWithChildren } from "react";
import { createContext, useContext } from "react";

export type SessionUser = {
  sub?: string;
  name?: string;
  email?: string;
  [key: string]: unknown;
};

const SessionUserContext = createContext<SessionUser | undefined>(undefined);

export function SessionUserProvider({
  children,
  user,
}: PropsWithChildren<{ user?: SessionUser }>) {
  return (
    <SessionUserContext.Provider value={user}>
      {children}
    </SessionUserContext.Provider>
  );
}

export function useSessionUser() {
  return useContext(SessionUserContext);
}

export default SessionUserProvider;
