import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import * as fs from "node:fs";
import { createRequire } from "node:module";
import os from "node:os";
import path from "node:path";
import { after, test } from "node:test";
import { fileURLToPath } from "node:url";
import {
  applyDepthGuard,
  restoreSource,
} from "../apply-braces-depth-guard.mjs";

const project = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../..",
);
const installed = path.join(project, "node_modules/braces");
const runtimeFiles = [
  "package.json",
  "index.js",
  "lib/constants.js",
  "lib/utils.js",
  "lib/parse.js",
  "lib/compile.js",
  "lib/expand.js",
  "lib/stringify.js",
];
const temporaryRoot = fs.mkdtempSync(
  path.join(os.tmpdir(), "toolbox-braces-test-"),
);
let sequence = 0;

after(() => {
  const resolved = fs.realpathSync(temporaryRoot);
  assert.equal(
    path.dirname(resolved).toLowerCase(),
    fs.realpathSync(os.tmpdir()).toLowerCase(),
  );
  assert.match(path.basename(resolved), /^toolbox-braces-test-/);
  fs.rmSync(resolved, { recursive: true, force: true });
});

function copyOriginal(destination) {
  fs.mkdirSync(path.join(destination, "lib"), { recursive: true });
  for (const file of runtimeFiles) {
    const content = restoreSource(
      file,
      fs.readFileSync(path.join(installed, file), "utf8"),
    );
    fs.writeFileSync(path.join(destination, file), content);
  }
}

function fixture(copies = ["node_modules/braces"]) {
  const root = path.join(temporaryRoot, String(++sequence));
  fs.mkdirSync(path.join(root, "node_modules"), { recursive: true });
  const packages = { "": { name: "depth-guard-fixture", version: "1.0.0" } };
  for (const relative of copies) {
    copyOriginal(path.join(root, relative));
    packages[relative] = { name: "braces", version: "3.0.3", dev: true };
  }
  for (const dependency of ["fill-range", "is-number", "to-regex-range"]) {
    fs.cpSync(
      path.join(project, "node_modules", dependency),
      path.join(root, "node_modules", dependency),
      { recursive: true },
    );
  }
  fs.writeFileSync(
    path.join(root, "package-lock.json"),
    JSON.stringify({ lockfileVersion: 3, packages }),
  );
  return root;
}

function load(root) {
  return createRequire(path.join(root, "fixture.cjs"))("braces");
}

function snapshot(root) {
  const result = {};
  function visit(directory) {
    for (const item of fs.readdirSync(directory, { withFileTypes: true })) {
      const filename = path.join(directory, item.name);
      if (item.isDirectory()) {
        visit(filename);
      } else if (item.isFile()) {
        result[path.relative(root, filename)] = fs
          .readFileSync(filename)
          .toString("base64");
      }
    }
  }
  visit(root);
  return result;
}

function rejectsControlled(operation) {
  assert.throws(
    operation,
    (error) => error instanceof SyntaxError && !(error instanceof RangeError),
  );
}

const originalRoot = fixture();
const patchedRoot = fixture();
applyDepthGuard(patchedRoot);
const original = load(originalRoot);
const patched = load(patchedRoot);

test("the actual installed lock inventory is fully guarded without changes", () => {
  const result = applyDepthGuard(project, { check: true });
  assert.ok(result.copies > 0);
  assert.equal(result.changedFiles, 0);
  const actual = load(project);
  assert.deepEqual(actual.expand("a/{b,c}"), ["a/b", "a/c"]);
  rejectsControlled(() =>
    actual.compile("{".repeat(102) + "a,b" + "}".repeat(102)),
  );
});

test("installed Next.js and Stylelint globbing dependencies reject excessive brace depth", () => {
  const requireFromProject = createRequire(path.join(project, "package.json"));
  const excessive = "{".repeat(4000) + "a,b" + "}".repeat(4000);
  for (const owner of ["@next/eslint-plugin-next", "stylelint"]) {
    const requireFromOwner = createRequire(requireFromProject.resolve(owner));
    const fastGlob = requireFromOwner("fast-glob");
    const micromatch = createRequire(requireFromOwner.resolve("fast-glob"))(
      "micromatch",
    );
    for (const api of ["braces", "braceExpand", "parse"]) {
      rejectsControlled(() => micromatch[api](excessive));
    }
    rejectsControlled(() => fastGlob.generateTasks(excessive));
    assert.deepEqual(micromatch.braceExpand("a/{b,c}/*.css"), [
      "a/b/*.css",
      "a/c/*.css",
    ]);
    assert.deepEqual(
      fastGlob
        .generateTasks("a/{b,c}/*.css")
        .flatMap((task) => task.patterns)
        .sort(),
      ["a/b/*.css", "a/c/*.css"],
    );
  }
});

test("unpatched recursive walkers reproduce stack exhaustion; guarded raw APIs reject it", () => {
  for (const [open, close] of [
    ["{", "}"],
    ["(", ")"],
  ]) {
    const input = open.repeat(4000) + "a,b" + close.repeat(4000);
    assert.doesNotThrow(() => original.parse(input));
    // Keep reproduction independent of platform-specific default V8 stack sizes.
    const reproduction = spawnSync(
      process.execPath,
      [
        "--stack-size=256",
        "-e",
        `
      const assert = require('node:assert/strict');
      const path = require('node:path');
      const braces = require(path.join(process.argv[1], 'node_modules/braces'));
      const input = process.argv[2].repeat(4000) + 'a,b' + process.argv[3].repeat(4000);
      assert.doesNotThrow(() => braces.parse(input));
      for (const api of ['compile', 'expand', 'stringify']) {
        assert.throws(() => braces[api](input), RangeError, api);
      }
    `,
        originalRoot,
        open,
        close,
      ],
      { encoding: "utf8", timeout: 10000 },
    );
    assert.equal(reproduction.status, 0, reproduction.stderr);
    for (const api of ["parse", "compile", "expand", "stringify", "create"]) {
      rejectsControlled(() => patched[api](input));
    }
    rejectsControlled(() => patched(input));
    rejectsControlled(() => patched(["safe/{a,b}", input]));
  }
});

test("ordinary patterns retain upstream outputs for all public APIs", () => {
  const patterns = [
    "",
    "a/b",
    "a/{b,c}/d",
    "{1..3}",
    "{a..c}",
    "{001..003}",
    "a{b,c{d,e}}",
    "(a{b,c})",
    "[abc]/{x,y}",
    "literal\\{braces\\}",
    "${a,b}",
    "{a",
    'a{"b,c",d}',
    "{1..5..2}",
  ];
  for (const input of patterns) {
    for (const options of [{}, { keepEscaping: true, keepQuotes: true }]) {
      for (const api of ["compile", "expand", "stringify", "create"]) {
        assert.deepEqual(
          patched[api](input, options),
          original[api](input, options),
          `${api}: ${input}`,
        );
      }
      assert.deepEqual(
        patched(input, options),
        original(input, options),
        input,
      );
      assert.equal(
        patched.stringify(patched.parse(input, options), options),
        original.stringify(original.parse(input, options), options),
      );
    }
  }
});

test("100 actual parser levels are allowed and 101 are rejected, including mixed groups", () => {
  for (const [open, close] of [
    ["{", "}"],
    ["(", ")"],
    ["({", "})"],
  ]) {
    const multiplier = open.length;
    const accepted =
      open.repeat(100 / multiplier) + "a" + close.repeat(100 / multiplier);
    for (const api of ["parse", "compile", "expand", "stringify"]) {
      assert.doesNotThrow(() => patched[api](accepted));
    }
    const rejected = "{" + accepted + "}";
    rejectsControlled(() => patched.parse(rejected, { maxDepth: 100000 }));
  }
});

test("direct AST depth, child cycles and parent cycles fail before recursive traversal", () => {
  const deep = { type: "root", nodes: [] };
  let node = deep;
  for (let index = 0; index < 8000; index++) {
    const child = { type: "root", nodes: [] };
    node.nodes.push(child);
    node = child;
  }
  for (const api of ["compile", "expand", "stringify"]) {
    assert.throws(
      () => original[api](deep),
      RangeError,
      `unpatched AST ${api}`,
    );
  }
  const childrenCycle = { type: "root", nodes: [] };
  childrenCycle.nodes.push(childrenCycle);
  const parentCycle = { type: "text", value: "a" };
  parentCycle.parent = parentCycle;
  const invalidChildren = { type: "root", nodes: {} };
  const excessiveEdges = {
    type: "root",
    nodes: Array(200001).fill({ type: "text", value: "a" }),
  };
  for (const ast of [
    deep,
    childrenCycle,
    parentCycle,
    invalidChildren,
    excessiveEdges,
  ]) {
    for (const api of ["compile", "expand", "stringify"]) {
      rejectsControlled(() => patched[api](ast));
    }
  }
});

test("normal parser parent and prev backlinks remain valid", () => {
  const ast = patched.parse("a/{b,c{d,e}}/(x)");
  assert.ok(ast.nodes.some((node) => node.parent === ast));
  assert.doesNotThrow(() => patched.compile(ast));
  assert.doesNotThrow(() => patched.stringify(ast));
  assert.deepEqual(patched.expand(ast), original.expand("a/{b,c{d,e}}/(x)"));
});

test("non-string AST values fail before recursive coercion or array expansion", () => {
  let nestedValue = "a";
  for (let index = 0; index < 12000; index++) {
    nestedValue = [nestedValue];
  }
  const cyclicValue = [];
  cyclicValue.push(cyclicValue);
  const astWithValue = (value) => ({
    type: "root",
    nodes: [{ type: "text", value }],
  });

  for (const api of ["compile", "expand", "stringify"]) {
    const direct = createRequire(path.join(patchedRoot, "fixture.cjs"))(
      path.join(patchedRoot, "node_modules/braces/lib", `${api}.js`),
    );
    assert.throws(() => original[api](astWithValue(nestedValue)), RangeError);
    for (const value of [nestedValue, cyclicValue, [], {}, 42]) {
      rejectsControlled(() => patched[api](astWithValue(value)));
      rejectsControlled(() => direct(astWithValue(value)));
    }
    assert.doesNotThrow(() => patched[api](astWithValue("a")));
  }
});

test("AST counters reject recursive coercion before entering the walkers", () => {
  let nested = 1;
  for (let index = 0; index < 12000; index++) nested = [nested];
  const cyclic = [];
  cyclic.push(cyclic);
  const counterAst = (field, value) => ({
    type: "root",
    nodes: [
      {
        type: "brace",
        open: true,
        close: true,
        commas: 1,
        ranges: 0,
        [field]: value,
        nodes: [{ type: "text", value: "a" }],
      },
    ],
  });
  for (const field of ["commas", "ranges"]) {
    for (const api of ["compile", "expand"]) {
      assert.throws(() => original[api](counterAst(field, nested)), RangeError);
    }
    for (const api of ["compile", "expand", "stringify"]) {
      const direct = createRequire(path.join(patchedRoot, "fixture.cjs"))(
        path.join(patchedRoot, "node_modules/braces/lib", `${api}.js`),
      );
      for (const value of [nested, cyclic, {}, "1", Infinity, NaN, -1, 0.5]) {
        rejectsControlled(() => patched[api](counterAst(field, value)));
        rejectsControlled(() => direct(counterAst(field, value)));
      }
    }
  }
});

test("foreign parent queues reject deep, cyclic and invalid expansion values", () => {
  const externalQueueAst = (queue) => ({
    type: "root",
    nodes: [
      {
        type: "text",
        parent: { type: "root", queue },
        nodes: [{ type: "text", value: "b" }],
      },
    ],
  });
  let deep = "a";
  for (let index = 0; index < 12000; index++) deep = [deep];
  assert.throws(() => original.expand(externalQueueAst([deep])), RangeError);
  const cycle = [];
  cycle.push(cycle);
  for (const queue of [
    [deep],
    cycle,
    [Array(200001).fill("a")],
    [{}],
    [undefined],
    [Infinity],
    "a",
  ]) {
    for (const api of ["compile", "expand", "stringify"]) {
      rejectsControlled(() => patched[api](externalQueueAst(queue)));
    }
  }
  for (const queue of [[], ["a"], [["a", 1]]]) {
    assert.doesNotThrow(() => patched.expand(externalQueueAst(queue)));
  }
  const parsed = patched.parse("{1..3}/a{b,c}");
  assert.deepEqual(patched.expand(parsed), original.expand("{1..3}/a{b,c}"));
  assert.doesNotThrow(() => patched.stringify(parsed));
  assert.doesNotThrow(() => patched.expand(parsed));
});

test("patching is idempotent and check mode never mutates an unpatched install", () => {
  const root = fixture();
  const before = snapshot(root);
  assert.throws(
    () => applyDepthGuard(root, { check: true }),
    /mitigation is absent/,
  );
  assert.deepEqual(snapshot(root), before);
  assert.equal(applyDepthGuard(root).changedFiles, 5);
  const afterPatch = snapshot(root);
  assert.equal(applyDepthGuard(root).changedFiles, 0);
  assert.equal(applyDepthGuard(root, { check: true }).changedFiles, 0);
  assert.deepEqual(snapshot(root), afterPatch);
});

test("all locked nested and aliased copies are guarded", () => {
  const root = fixture([
    "node_modules/braces",
    "node_modules/owner/node_modules/braces",
    "node_modules/brace-alias",
  ]);
  assert.deepEqual(applyDepthGuard(root), {
    copies: 3,
    changedFiles: 15,
    check: false,
  });
  assert.equal(applyDepthGuard(root, { check: true }).copies, 3);
  for (const relative of [
    "node_modules/braces",
    "node_modules/owner/node_modules/braces",
    "node_modules/brace-alias",
  ]) {
    assert.ok(
      fs.existsSync(path.join(root, relative, "lib/toolbox-depth-guard.js")),
    );
  }
});

test("an unknown source hash aborts every copy before any write", () => {
  const root = fixture([
    "node_modules/braces",
    "node_modules/owner/node_modules/braces",
  ]);
  fs.appendFileSync(
    path.join(root, "node_modules/owner/node_modules/braces/lib/parse.js"),
    "\n// unexpected\n",
  );
  const before = snapshot(root);
  assert.throws(() => applyDepthGuard(root), /Unrecognized braces source hash/);
  assert.deepEqual(snapshot(root), before);
});

test("unknown versions and changed metadata fail closed", () => {
  const versionRoot = fixture();
  const lockPath = path.join(versionRoot, "package-lock.json");
  const lock = JSON.parse(fs.readFileSync(lockPath, "utf8"));
  lock.packages["node_modules/braces"].version = "3.0.4";
  fs.writeFileSync(lockPath, JSON.stringify(lock));
  assert.throws(
    () => applyDepthGuard(versionRoot),
    /unreviewed braces version/,
  );
  const metadataRoot = fixture();
  fs.appendFileSync(
    path.join(metadataRoot, "node_modules/braces/package.json"),
    "\n",
  );
  assert.throws(
    () => applyDepthGuard(metadataRoot),
    /metadata has an unrecognized source hash/,
  );
});

test("changed guard files and unlisted installed copies fail closed", () => {
  const root = fixture();
  applyDepthGuard(root);
  fs.appendFileSync(
    path.join(root, "node_modules/braces/lib/toolbox-depth-guard.js"),
    "\n// unexpected\n",
  );
  assert.throws(
    () => applyDepthGuard(root, { check: true }),
    /guard has an unrecognized hash/,
  );
  const unlisted = fixture();
  copyOriginal(path.join(unlisted, "node_modules/owner/node_modules/braces"));
  assert.throws(
    () => applyDepthGuard(unlisted),
    /absent from the reviewed lock inventory/,
  );
  assert.ok(
    !fs.existsSync(
      path.join(unlisted, "node_modules/braces/lib/toolbox-depth-guard.js"),
    ),
  );
});

test("locked package symlinks outside node_modules are rejected without modifying targets", () => {
  const root = fixture([]);
  const external = path.join(root, "external/braces");
  copyOriginal(external);
  fs.symlinkSync(
    external,
    path.join(root, "node_modules/braces"),
    process.platform === "win32" ? "junction" : "dir",
  );
  fs.writeFileSync(
    path.join(root, "package-lock.json"),
    JSON.stringify({
      lockfileVersion: 3,
      packages: {
        "node_modules/braces": { name: "braces", version: "3.0.3", dev: true },
      },
    }),
  );
  assert.throws(() => applyDepthGuard(root), /resolves outside/);
  assert.ok(!fs.existsSync(path.join(external, "lib/toolbox-depth-guard.js")));
});

test("unlisted external braces links and aliases fail before modifying any copy", () => {
  for (const relative of [
    "node_modules/owner/node_modules/braces",
    "node_modules/brace-alias",
  ]) {
    const root = fixture();
    const external = path.join(root, "external/braces");
    copyOriginal(external);
    const link = path.join(root, relative);
    fs.mkdirSync(path.dirname(link), { recursive: true });
    fs.symlinkSync(
      external,
      link,
      process.platform === "win32" ? "junction" : "dir",
    );
    const before = snapshot(external);
    assert.throws(() => applyDepthGuard(root), /resolves outside/);
    assert.deepEqual(snapshot(external), before);
    assert.ok(
      !fs.existsSync(
        path.join(root, "node_modules/braces/lib/toolbox-depth-guard.js"),
      ),
    );
  }
});

test("an omitted development dependency is allowed and reported as zero installed copies", () => {
  const root = fixture([]);
  fs.writeFileSync(
    path.join(root, "package-lock.json"),
    JSON.stringify({
      lockfileVersion: 3,
      packages: {
        "node_modules/braces": { name: "braces", version: "3.0.3", dev: true },
      },
    }),
  );
  assert.deepEqual(applyDepthGuard(root, { check: true }), {
    copies: 0,
    changedFiles: 0,
    check: true,
  });
});
