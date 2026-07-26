const globalStyles = `
  :root {
    --bs-primary: #1f6feb;
    --bs-primary-rgb: 31, 111, 235;
    --bs-body-font-family: "Segoe UI Variable Text", "Segoe UI", "Noto Sans", sans-serif;
    --app-surface: #ffffff;
    --app-canvas: #f3f6fb;
    --app-ink: #18212f;
  }

  *,
  *::before,
  *::after {
    box-sizing: border-box;
  }

  html {
    color-scheme: light;
    line-height: 1.5;
    text-size-adjust: 100%;
  }

  body {
    background:
      radial-gradient(circle at top, rgba(31, 111, 235, 0.08), transparent 35%),
      linear-gradient(180deg, var(--app-canvas) 0%, #ffffff 45%);
    color: var(--app-ink);
    margin: 0;
  }

  a {
    color: var(--bs-primary);
  }

  button,
  input,
  textarea,
  select {
    font: inherit;
  }

  .text-secondary-subtle-emphasis {
    color: #5f6b7a;
  }

  .min-width-0 {
    min-width: 0;
  }
`;

export default function GlobalStyles() {
  return <style>{globalStyles}</style>;
}
