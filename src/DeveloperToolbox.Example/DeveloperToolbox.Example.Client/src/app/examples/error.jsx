"use client";

import { useEffect } from "react";
import "../error.css";

export default function ExamplesError({ error, reset }) {
  useEffect(() => {
    console.error(error);
  }, [error]);

  return (
    <section className="error-page" aria-labelledby="examples-error-title">
      <h1 id="examples-error-title">Examples could not be loaded</h1>
      <p>Try again or return to the home page.</p>
      <button type="button" onClick={reset}>
        Retry
      </button>
    </section>
  );
}
