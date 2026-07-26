"use client";
import { useState, useEffect } from "react";
///This is a utility component that lets us wrap a component with this check to stop those annoying hydration errors that show up in the console.

const ClientOnly = ({ children }) => {
  const [isClient, setIsClient] = useState(false);

  useEffect(() => {
    setIsClient(true);
  }, []);

  return isClient ? children : null;
};

export default ClientOnly;
