import { it, expect } from "vitest";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const cli = fileURLToPath(new URL("../src/cli.mjs", import.meta.url));
// Cold SDK imports can exceed the runner's default timeout on Windows.
const spawnOptions = { encoding: "utf8", timeout: 30_000 };

it.each([
  ["--settings"],
  ["--settings", "--text", "value"],
  ["--settings", ""],
  ["--text"],
  ["--text", "--settings", "settings.json"],
  ["--text", "--settings", "settings.json", "unknown-resource"],
  ["--settings", "--text", "value", "unknown-resource"],
  ["--text", ""],
])("rejects missing CLI option values before loading settings: %j", (...args) => {
  const result = spawnSync(process.execPath, [cli, ...args], spawnOptions);
  expect(result.status).toBe(1);
  expect(result.stderr).toContain(`${args[0]} requires a non-option value.`);
  expect(result.stdout).toBe("");
});

it.each([
  ["--settings", "missing-settings.json", "--text", "hello", "unknown-resource"],
  ["unknown-resource", "--text", "hello", "--settings", "missing-settings.json"],
  ["unknown-resource"],
])("preserves resource arguments with valid or absent CLI options: %j", (...args) => {
  const result = spawnSync(process.execPath, [cli, ...args], spawnOptions);
  expect(result.status).toBe(1);
  expect(result.stderr).toBe("");
  expect(JSON.parse(result.stdout)).toMatchObject({ resource: "unknown-resource", success: false });
});

it.each(["--settings", "--text"])("rejects duplicate %s options", (option) => {
  const result = spawnSync(process.execPath, [cli, option, "first", option, "second"], spawnOptions);
  expect(result.status).toBe(1);
  expect(result.stderr).toContain(`${option} may only be specified once.`);
  expect(result.stdout).toBe("");
});
