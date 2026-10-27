import { createHash, randomUUID } from "node:crypto";
import * as fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const VERSION = "3.0.3";
const ORIGINAL_HASHES = {
  "package.json":
    "56f08b888a4f30dc7cf8a7dbb36ffe92b737912ba36abe9d069d32167c957ac7",
  "index.js":
    "332ea07c7b006361aad12aa994ca75dc1db8e8382b884909e2f38f10b85c88a4",
  "lib/constants.js":
    "c18ac5adb57308f1ce42a28552da3a31f5d83709743ebd9a636336813a744d4b",
  "lib/utils.js":
    "b5a7596aa67730412b3c029ef09e84e6b67b8e445cffd35d1d295549c89066c7",
  "lib/parse.js":
    "e572166565f15fa6ad9865ae49d678218e32aabfd1b3720f6d0d43d39800d310",
  "lib/compile.js":
    "dc98f22eee3d511785d92a00758d5f0d48efed5f5813bdecc2de430c529b5c9f",
  "lib/expand.js":
    "41ccc196ebfa7b7781a634e721eb744e4e7bcb54cba427a7e3d6806a1b9e58f7",
  "lib/stringify.js":
    "379f22d77bfa1478341ccd49c5e4267464aabcbba03558bab332aac23fc6f23a",
};

const PATCHED_HASHES = {
  "lib/parse.js":
    "f5a32a352a32c69858fb5c20b962ef9b60ebeaff0fc48ae21de57fc589e8f952",
  "lib/compile.js":
    "fde167799053c157f0bff8d3b686a26d1ce430eb0f31256310dea9eeea652b50",
  "lib/expand.js":
    "fa575ee0d20682765486253e1ded36b059d2069313a4a21b516651d1f3f64a2e",
  "lib/stringify.js":
    "4a7c9e958d2f6129e4272da269ad50b27c5985864a94ac88d798abb812efc15f",
};

const GUARD_FILE = "lib/toolbox-depth-guard.js";
const GUARD_SOURCE = String.raw`'use strict';

// Local GHSA-vfj7-8cjw-p6xm mitigation; package metadata remains upstream 3.0.3.
const MAX_AST_DEPTH = 101;
const MAX_AST_NODES = 200000;

module.exports = ast => {
  const active = new WeakSet();
  const pending = [{ node: ast, depth: 0, exit: false }];
  const checkedQueues = new WeakSet();
  let nodes = 0;
  let edges = 0;
  let queueNodes = 0;
  let queueEdges = 0;

  // Expansion can follow a foreign parent queue, outside the supplied child tree.
  const assertQueue = queue => {
    if (queue === undefined) return;
    if (!Array.isArray(queue)) throw new SyntaxError('Braces AST queues must be arrays.');
    if (checkedQueues.has(queue)) return;
    const activeQueues = new WeakSet();
    const frames = [{ value: queue, depth: 0, exit: false }];
    while (frames.length > 0) {
      const frame = frames.pop();
      const value = frame.value;
      if (frame.exit) {
        activeQueues.delete(value);
        continue;
      }
      if (frame.depth > MAX_AST_DEPTH || ++queueNodes > MAX_AST_NODES) {
        throw new SyntaxError('Braces AST queues exceed the local traversal limit.');
      }
      if (Array.isArray(value)) {
        if (activeQueues.has(value)) throw new SyntaxError('Cyclic braces AST queues are not supported.');
        queueEdges += value.length;
        if (queueEdges > MAX_AST_NODES) throw new SyntaxError('Braces AST queues exceed the local traversal limit.');
        activeQueues.add(value);
        frames.push({ value, depth: frame.depth, exit: true });
        for (let index = value.length - 1; index >= 0; index--) {
          frames.push({ value: value[index], depth: frame.depth + 1, exit: false });
        }
      } else if (typeof value !== 'string' && !(typeof value === 'number' && Number.isFinite(value))) {
        throw new SyntaxError('Braces AST queue values must be strings or finite numbers.');
      }
    }
    checkedQueues.add(queue);
  };

  while (pending.length > 0) {
    const frame = pending.pop();
    const node = frame.node;
    if (!node || typeof node !== 'object' || Array.isArray(node)) {
      throw new SyntaxError('Invalid braces AST node.');
    }
    if (frame.exit) {
      active.delete(node);
      continue;
    }
    if (frame.depth > MAX_AST_DEPTH || ++nodes > MAX_AST_NODES) {
      throw new SyntaxError('Braces AST exceeds the local traversal limit.');
    }
    if (active.has(node)) {
      throw new SyntaxError('Cyclic braces AST children are not supported.');
    }
    if (node.value !== undefined && typeof node.value !== 'string') {
      throw new SyntaxError('Braces AST values must be strings.');
    }
    // Upstream coerces these counters numerically, which recursively stringifies arrays.
    for (const field of ['commas', 'ranges']) {
      if (node[field] !== undefined && (!Number.isSafeInteger(node[field]) || node[field] < 0)) {
        throw new SyntaxError('Braces AST counters must be nonnegative safe integers.');
      }
    }
    assertQueue(node.queue);

    // Normal parser parent/prev back-links are not child edges; expansion follows parents separately.
    const parents = new WeakSet([node]);
    let parent = node.parent;
    let parentDepth = 0;
    while (parent !== undefined) {
      if (!parent || typeof parent !== 'object' || Array.isArray(parent) || parents.has(parent) || ++parentDepth > MAX_AST_DEPTH) {
        throw new SyntaxError('Invalid or excessive braces AST parent chain.');
      }
      parents.add(parent);
      assertQueue(parent.queue);
      parent = parent.parent;
    }

    if (node.nodes !== undefined && !Array.isArray(node.nodes)) {
      throw new SyntaxError('Braces AST children must be an array.');
    }
    active.add(node);
    pending.push({ node, depth: frame.depth, exit: true });
    if (node.nodes !== undefined) {
      edges += node.nodes.length;
      if (edges > MAX_AST_NODES) {
        throw new SyntaxError('Braces AST exceeds the local traversal limit.');
      }
      for (let index = node.nodes.length - 1; index >= 0; index--) {
        pending.push({ node: node.nodes[index], depth: frame.depth + 1, exit: false });
      }
    }
  }
};
`.replace(/\r\n/g, "\n");

const PARSER_GUARD =
  "      if (stack.length > 100) {\n        throw new SyntaxError('Braces nesting exceeds the local limit of 100.');\n      }\n";
const IMPORT_GUARD = "const assertAst = require('./toolbox-depth-guard');\n";

function sha256(content) {
  return createHash("sha256").update(content).digest("hex");
}

function replaceOnce(source, before, after) {
  if (source.split(before).length !== 2) {
    throw new Error(
      "The reviewed braces source anchor does not occur exactly once.",
    );
  }
  return source.replace(before, after);
}

export function patchSource(file, source) {
  if (file === "lib/parse.js") {
    for (const kind of ["CHAR_LEFT_PARENTHESES", "CHAR_LEFT_CURLY_BRACE"]) {
      const anchor = `    if (value === ${kind}) {\n`;
      source = replaceOnce(source, anchor, anchor + PARSER_GUARD);
    }
    return source;
  }
  source = replaceOnce(
    source,
    "'use strict';\n",
    "'use strict';\n\n" + IMPORT_GUARD,
  );
  const name =
    file === "lib/stringify.js"
      ? "module.exports"
      : "const " + path.basename(file, ".js");
  const anchor = `${name} = (ast, options = {}) => {\n`;
  return replaceOnce(source, anchor, anchor + "  assertAst(ast);\n");
}

export function restoreSource(file, source) {
  if (sha256(source) === ORIGINAL_HASHES[file]) {
    return source;
  }
  if (sha256(source) !== PATCHED_HASHES[file]) {
    throw new Error(`Unrecognized braces source hash: ${file}.`);
  }
  if (file === "lib/parse.js") {
    for (const kind of ["CHAR_LEFT_PARENTHESES", "CHAR_LEFT_CURLY_BRACE"]) {
      const anchor = `    if (value === ${kind}) {\n`;
      source = replaceOnce(source, anchor + PARSER_GUARD, anchor);
    }
  } else {
    source = replaceOnce(
      source,
      "'use strict';\n\n" + IMPORT_GUARD,
      "'use strict';\n",
    );
    const name =
      file === "lib/stringify.js"
        ? "module.exports"
        : "const " + path.basename(file, ".js");
    const anchor = `${name} = (ast, options = {}) => {\n`;
    source = replaceOnce(source, anchor + "  assertAst(ast);\n", anchor);
  }
  if (sha256(source) !== ORIGINAL_HASHES[file]) {
    throw new Error(`Patched braces source could not be verified: ${file}.`);
  }
  return source;
}

function within(directory, target) {
  const relative = path.relative(directory, target);
  return (
    relative === "" ||
    (!relative.startsWith(".." + path.sep) &&
      relative !== ".." &&
      !path.isAbsolute(relative))
  );
}

function checkedFile(modulesRoot, target, optional = false) {
  if (fs.lstatSync(target, { throwIfNoEntry: false })?.isSymbolicLink()) {
    const resolved = fs.realpathSync(target);
    if (!within(modulesRoot, resolved)) {
      throw new Error(
        "Braces package files must remain inside this project's node_modules.",
      );
    }
  }
  if (!fs.existsSync(target)) {
    if (optional && !fs.existsSync(path.dirname(target))) {
      throw new Error("The installed braces lib directory is missing.");
    }
    if (optional) {
      return undefined;
    }
    throw new Error("A locked braces package file is missing.");
  }
  const resolved = fs.realpathSync(target);
  if (!within(modulesRoot, resolved) || !fs.statSync(resolved).isFile()) {
    throw new Error(
      "Braces package files must remain inside this project's node_modules.",
    );
  }
  return resolved;
}

function readJson(filename, label) {
  try {
    return JSON.parse(fs.readFileSync(filename, "utf8"));
  } catch {
    throw new Error(
      `Cannot read valid ${label}; file contents will not be printed.`,
    );
  }
}

function rejectUnlistedCopies(modulesRoot, reviewedCopies) {
  const pending = [modulesRoot];
  const visited = new Set();
  while (pending.length > 0) {
    const directory = fs.realpathSync(pending.pop());
    if (!within(modulesRoot, directory) || visited.has(directory)) {
      continue;
    }
    visited.add(directory);
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      if (
        entry.name.startsWith(".") ||
        (!entry.isDirectory() && !entry.isSymbolicLink())
      ) {
        continue;
      }
      const packagePath = path.join(directory, entry.name);
      if (!fs.existsSync(packagePath)) {
        continue;
      }
      const resolved = fs.realpathSync(packagePath);
      const metadataPath = path.join(resolved, "package.json");
      if (fs.existsSync(metadataPath)) {
        const metadata = readJson(metadataPath, "installed package metadata");
        if (metadata.name === "braces") {
          if (!within(modulesRoot, resolved)) {
            throw new Error(
              "An installed braces copy resolves outside this project's node_modules.",
            );
          }
          if (!reviewedCopies.has(resolved)) {
            throw new Error(
              "An installed braces copy is absent from the reviewed lock inventory.",
            );
          }
        }
      }
      if (!within(modulesRoot, resolved)) {
        continue;
      }
      if (entry.name.startsWith("@")) {
        pending.push(resolved);
        continue;
      }
      const nested = path.join(resolved, "node_modules");
      if (fs.existsSync(nested)) {
        pending.push(nested);
      }
    }
  }
}

export function applyDepthGuard(projectRoot, { check = false } = {}) {
  const project = fs.realpathSync(projectRoot);
  const lock = readJson(
    path.join(project, "package-lock.json"),
    "npm lock metadata",
  );
  if (!lock.packages || ![2, 3].includes(lock.lockfileVersion)) {
    throw new Error(
      "The braces mitigation requires an npm v2/v3 package lock.",
    );
  }
  const modulesRoot = fs.realpathSync(path.join(project, "node_modules"));
  if (!within(project, modulesRoot)) {
    throw new Error("node_modules must remain inside this project.");
  }
  const plans = new Map();
  const copies = new Set();
  for (const [relative, entry] of Object.entries(lock.packages)) {
    if (
      !/(^|\/)node_modules\/braces$/.test(relative) &&
      entry.name !== "braces"
    ) {
      continue;
    }
    const target = path.resolve(project, relative);
    if (
      !within(path.join(project, "node_modules"), target) ||
      relative.split("/").includes("..")
    ) {
      throw new Error(
        "The braces lock path escapes this project's node_modules.",
      );
    }
    if (entry.version !== VERSION) {
      throw new Error(
        "An unreviewed braces version is present; update or retire the local mitigation.",
      );
    }
    if (!fs.existsSync(target)) {
      if (entry.dev === true || entry.optional === true) {
        continue;
      }
      throw new Error("A required locked braces package is not installed.");
    }
    const packageRoot = fs.realpathSync(target);
    if (!within(modulesRoot, packageRoot)) {
      throw new Error(
        "The braces package resolves outside this project's node_modules.",
      );
    }
    if (copies.has(packageRoot)) {
      continue;
    }
    copies.add(packageRoot);
    const packageFile = checkedFile(
      modulesRoot,
      path.join(packageRoot, "package.json"),
    );
    if (
      sha256(fs.readFileSync(packageFile)) !== ORIGINAL_HASHES["package.json"]
    ) {
      throw new Error(
        "Installed braces package metadata has an unrecognized source hash.",
      );
    }
    const metadata = readJson(packageFile, "braces package metadata");
    if (metadata.name !== "braces" || metadata.version !== VERSION) {
      throw new Error(
        "Installed braces metadata differs from the reviewed version.",
      );
    }
    for (const [file, originalHash] of Object.entries(ORIGINAL_HASHES)) {
      const filename = checkedFile(modulesRoot, path.join(packageRoot, file));
      const source = fs.readFileSync(filename, "utf8");
      const hash = sha256(source);
      if (hash !== originalHash && hash !== PATCHED_HASHES[file]) {
        throw new Error(`Unrecognized braces source hash: ${file}.`);
      }
      if (PATCHED_HASHES[file]) {
        const patched = patchSource(file, restoreSource(file, source));
        if (sha256(patched) !== PATCHED_HASHES[file]) {
          throw new Error(
            "The generated braces patch differs from the reviewed patch hash.",
          );
        }
        if (check && hash !== PATCHED_HASHES[file]) {
          throw new Error(
            "The installed braces mitigation is absent; run the postinstall helper.",
          );
        }
        if (hash !== PATCHED_HASHES[file]) {
          plans.set(filename, patched);
        }
      }
    }
    const guardTarget = path.join(packageRoot, GUARD_FILE);
    const existingGuard = checkedFile(modulesRoot, guardTarget, true);
    if (
      existingGuard &&
      sha256(fs.readFileSync(existingGuard)) !== sha256(GUARD_SOURCE)
    ) {
      throw new Error(
        "The installed braces traversal guard has an unrecognized hash.",
      );
    }
    if (!existingGuard) {
      if (check) {
        throw new Error("The installed braces traversal guard is missing.");
      }
      plans.set(guardTarget, GUARD_SOURCE);
    }
  }
  rejectUnlistedCopies(modulesRoot, copies);
  // Validate every copy before writing; npm installation must not have concurrent writers.
  for (const [filename, content] of plans) {
    const temporary = filename + ".toolbox-" + randomUUID();
    try {
      fs.writeFileSync(temporary, content, { flag: "wx" });
      fs.renameSync(temporary, filename);
    } finally {
      if (fs.existsSync(temporary)) {
        fs.unlinkSync(temporary);
      }
    }
  }
  return { copies: copies.size, changedFiles: plans.size, check };
}

const scriptPath = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]) === scriptPath) {
  try {
    const arguments_ = process.argv.slice(2);
    if (arguments_.some((argument) => argument !== "--check")) {
      throw new Error(
        "Usage: node scripts/apply-braces-depth-guard.mjs [--check]",
      );
    }
    const result = applyDepthGuard(
      path.resolve(path.dirname(scriptPath), ".."),
      { check: arguments_.includes("--check") },
    );
    console.log(
      `Braces local mitigation ${result.check ? "verified" : "applied"}: ${result.copies} installed copy/copies.`,
    );
  } catch (error) {
    console.error(`Braces local mitigation failed: ${error.message}`);
    process.exitCode = 1;
  }
}
