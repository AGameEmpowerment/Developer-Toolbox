import { spawn, spawnSync } from "node:child_process";
import { existsSync, mkdirSync } from "node:fs";
import os from "node:os";
import path from "node:path";

const defaultPort = "3001";
const certificateName = "systemuptimetracker.web";
const port = process.env.PORT?.trim() || defaultPort;

const certificateDirectory =
  process.platform === "win32"
    ? path.join(
        process.env.APPDATA || path.join(os.homedir(), "AppData", "Roaming"),
        "ASP.NET",
        "https",
      )
    : path.join(os.homedir(), ".aspnet", "https");

const certificatePath = path.join(
  certificateDirectory,
  `${certificateName}.pem`,
);
const exportedKeyPath = path.join(
  certificateDirectory,
  `${certificateName}.key`,
);

mkdirSync(certificateDirectory, { recursive: true });

if (process.platform === "linux") {
  console.warn(
    [
      "Skipping 'dotnet dev-certs https --trust' on Linux because the .NET CLI",
      "does not support it there. Trust the exported certificate manually if",
      "your browser or Node runtime still rejects it.",
    ].join(" "),
  );
} else {
  const ensureTrustedCertificateResult = spawnSync(
    "dotnet",
    ["dev-certs", "https", "--trust", "--quiet"],
    {
      stdio: "inherit",
    },
  );

  if (ensureTrustedCertificateResult.status !== 0) {
    process.exit(ensureTrustedCertificateResult.status ?? 1);
  }
}

const exportCertificateResult = spawnSync(
  "dotnet",
  [
    "dev-certs",
    "https",
    "--export-path",
    certificatePath,
    "--format",
    "PEM",
    "--no-password",
    "--quiet",
  ],
  {
    stdio: "inherit",
  },
);

if (exportCertificateResult.status !== 0) {
  process.exit(exportCertificateResult.status ?? 1);
}

const keyPath = existsSync(exportedKeyPath) ? exportedKeyPath : certificatePath;

const existingNodeOptions = process.env.NODE_OPTIONS?.trim() ?? "";
const nodeOptions = existingNodeOptions.includes("--use-system-ca")
  ? existingNodeOptions
  : [existingNodeOptions, "--use-system-ca"].filter(Boolean).join(" ");
const nextProcess = spawn(
  process.platform === "win32" ? "cmd.exe" : "npx",
  process.platform === "win32"
    ? [
        "/d",
        "/s",
        "/c",
        "npx",
        "next",
        "dev",
        "--webpack",
        "--experimental-https",
        "--experimental-https-cert",
        certificatePath,
        "--experimental-https-key",
        keyPath,
        "--hostname",
        "0.0.0.0",
        "--no-server-fast-refresh",
        "--port",
        port,
      ]
    : [
        "next",
        "dev",
        "--webpack",
        "--experimental-https",
        "--experimental-https-cert",
        certificatePath,
        "--experimental-https-key",
        keyPath,
        "--hostname",
        "0.0.0.0",
        "--no-server-fast-refresh",
        "--port",
        port,
      ],
  {
    stdio: "inherit",
    env: {
      ...process.env,
      NODE_OPTIONS: nodeOptions,
      NODE_EXTRA_CA_CERTS: certificatePath,
    },
  },
);

nextProcess.on("exit", (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
    return;
  }

  process.exit(code ?? 0);
});

nextProcess.on("error", (error) => {
  console.error("Failed to start the Next.js HTTPS dev server.", error);
  process.exit(1);
});
