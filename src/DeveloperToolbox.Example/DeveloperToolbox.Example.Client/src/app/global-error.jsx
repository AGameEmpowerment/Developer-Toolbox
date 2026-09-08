"use client";

import { useEffect } from "react";
import "./error.css";

export default function GlobalError({ error, reset }) {
  useEffect(() => {
    console.error(error);
  }, [error]);

  return (
    <html lang="en">
      <body>
        <main className="error-page">
          <h1>Something went wrong</h1>
          <p>
            Try the request again. If the problem continues, review the local
            logs.
          </p>
          <button type="button" onClick={reset}>
            Retry
          </button>
        </main>
      </body>
    </html>
  );
}
