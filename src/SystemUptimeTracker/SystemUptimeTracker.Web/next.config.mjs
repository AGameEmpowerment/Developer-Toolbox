export const getOrigin = (value) => {
  if (!value) {
    return undefined;
  }

  const normalizedValue = value.trim();
  if (!normalizedValue) {
    return undefined;
  }

  const urlValue = /^[a-zA-Z][a-zA-Z\d+\-.]*:/.test(normalizedValue)
    ? normalizedValue
    : `https://${normalizedValue}`;

  try {
    const parsedUrl = new URL(urlValue);
    return ["http:", "https:"].includes(parsedUrl.protocol)
      ? parsedUrl.origin
      : undefined;
  } catch {
    return undefined;
  }
};

export const getDelimitedValues = (value) =>
  (value || "")
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);

const connectSources = [
  "'self'",
  "https://*.googleapis.com",
  "*.google.com",
  "https://*.gstatic.com",
  ...(process.env.NODE_ENV === "development"
    ? [
        "ws://localhost:*",
        "wss://localhost:*",
        "ws://127.0.0.1:*",
        "wss://127.0.0.1:*",
        "ws://host.docker.internal:*",
        "wss://host.docker.internal:*",
      ]
    : []),
  getOrigin(process.env.MICROSOFT_AUTHORITY),
  getOrigin("https://login.microsoftonline.com"),
].filter(Boolean);

const allowedDevOrigins = getDelimitedValues(
  process.env.NEXT_ALLOWED_DEV_ORIGINS,
);

const nextConfig = {
  ...(allowedDevOrigins.length > 0 ? { allowedDevOrigins } : {}),
  compiler: {
    // ssr and displayName are configured by default
    styledComponents: true,
  },
  poweredByHeader: false,
  serverExternalPackages: [
    "applicationinsights",
    "@opentelemetry/api",
    "@opentelemetry/sdk-logs",
    "@opentelemetry/sdk-trace-base",
    "@opentelemetry/exporter-logs-otlp-proto",
    "@opentelemetry/exporter-trace-otlp-proto",
    "@opentelemetry/resources",
  ],
  async headers() {
    const isDev = process.env.NODE_ENV === "development";
    return [
      {
        // Applies these headers to all routes
        source: "/:path*",
        headers: [
          {
            key: "Content-Security-Policy",
            value: [
              "default-src 'self';",
              "style-src 'self' 'unsafe-inline';",
              `script-src 'self' 'unsafe-inline'${
                isDev ? " 'unsafe-eval'" : ""
              };`,
              "img-src 'self' data:;",
              `connect-src ${connectSources.join(" ")};`,
              "frame-ancestors 'self';",
            ].join(" "),
          },
          {
            key: "Strict-Transport-Security",
            value: "maxAge=15552000,includeSubDomains=true,preload=false",
          },
          {
            key: "X-Content-Type-Options",
            value: "nosniff",
          },
          {
            key: "Referrer-Policy",
            value: "no-referrer",
          },
        ],
      },
    ];
  },
  output: "standalone",
};

export default nextConfig;
