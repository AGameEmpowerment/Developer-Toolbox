#!/usr/bin/env python3
"""Regression tests for reproducible commit-based architecture inventories."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = (
    Path(__file__).resolve().parents[1]
    / "docs"
    / "architecture"
    / "inventory-source.py"
)
SPEC = importlib.util.spec_from_file_location("architecture_inventory", SCRIPT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load {SCRIPT_PATH}")
INVENTORY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(INVENTORY)


class ArchitectureInventoryTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="toolbox-inventory-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.git("init", "--quiet")
        self.git("config", "core.autocrlf", "false")
        self.git("config", "core.hooksPath", str(self.root / "unused-hooks"))
        (self.root / ".gitattributes").write_text("* text\n", encoding="utf-8")
        self.source = self.root / "tests" / "ExampleTests.cs"
        self.source.parent.mkdir()
        self.source.write_bytes(b"class ExampleTests {\n[Fact]\npublic void Example() { }\n}\n")
        self.git("add", ".")
        self.git(
            "-c", "user.name=Toolbox Test",
            "-c", "user.email=toolbox-test@example.invalid",
            "-c", "commit.gpgsign=false",
            "commit", "--quiet", "-m", "Synthetic inventory source",
        )
        self.commit = self.git("rev-parse", "HEAD").decode().strip()

    def git(self, *arguments: str) -> bytes:
        return subprocess.check_output(
            ["git", "-C", str(self.root), *arguments], stderr=subprocess.PIPE
        )

    def generate(self, *, check: bool = False) -> None:
        INVENTORY.inventory(
            self.root, "AGameEmpowerment/Developer-Toolbox", self.commit, check
        )

    def commit_sources(self, sources: dict[str, str]) -> None:
        for name, content in sources.items():
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        self.git("add", *sources)
        self.git(
            "-c", "user.name=Toolbox Test",
            "-c", "user.email=toolbox-test@example.invalid",
            "-c", "commit.gpgsign=false",
            "commit", "--quiet", "-m", "Synthetic test declarations",
        )
        self.commit = self.git("rev-parse", "HEAD").decode().strip()

    def records(self) -> dict[str, dict]:
        data = json.loads(
            (self.root / "docs/architecture/test-inventory.json").read_text(encoding="utf-8")
        )
        return {record["source"]: record for record in data["files"]}

    def test_hashes_match_commit_blobs_in_a_crlf_checkout(self) -> None:
        self.source.write_bytes(self.source.read_bytes().replace(b"\n", b"\r\n"))
        self.assertEqual(b"", self.git("diff", "--name-only"))
        self.generate()
        data = json.loads(
            (self.root / "docs/architecture/test-inventory.json").read_text(encoding="utf-8")
        )
        record = data["files"][0]
        blob = self.git("show", f"{self.commit}:tests/ExampleTests.cs")
        self.assertEqual(hashlib.sha256(blob).hexdigest(), record["sha256"])
        self.assertEqual(1, record["counts"]["authoredAttributedMethodDeclarations"])

    def test_check_accepts_checkout_line_ending_changes(self) -> None:
        self.generate()
        self.source.write_bytes(self.source.read_bytes().replace(b"\n", b"\r\n"))
        self.assertEqual(b"", self.git("diff", "--name-only"))
        self.generate(check=True)

    def test_rejects_changed_source_content(self) -> None:
        self.generate()
        with self.source.open("ab") as output:
            output.write(b"// A real source change\n")
        with self.assertRaisesRegex(SystemExit, "Source differs"):
            self.generate(check=True)

    def test_check_rejects_changed_markdown_totals(self) -> None:
        self.generate()
        report = self.root / "docs/architecture/test-inventory.md"
        text = report.read_text(encoding="utf-8")
        self.assertIn("Fact: **1**", text)
        report.write_text(text.replace("Fact: **1**", "Fact: **999**"), encoding="utf-8")
        with self.assertRaisesRegex(SystemExit, "Inventory differs"):
            self.generate(check=True)

    def test_check_rejects_changed_json_totals(self) -> None:
        self.generate()
        report = self.root / "docs/architecture/test-inventory.json"
        data = json.loads(report.read_text(encoding="utf-8"))
        data["files"][0]["counts"]["Fact"] = 999
        report.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
        with self.assertRaisesRegex(SystemExit, "Inventory differs"):
            self.generate(check=True)

    def test_check_rejects_missing_markdown_report(self) -> None:
        self.generate()
        (self.root / "docs/architecture/test-inventory.md").unlink()
        with self.assertRaisesRegex(SystemExit, "Inventory differs: test-inventory.md"):
            self.generate(check=True)

    def test_counts_combined_and_chained_csharp_attributes_once_per_method(self) -> None:
        self.commit_sources({"tests/ExampleTests.cs": """class ExampleTests {
[Fact, Trait("area", "fixture")]
public void Example() { }
[Theory][InlineData(1)][InlineData(2)]
public async Task Cases(int input) { }
[Trait("area", "fixture"), Fact]
public void LastAttribute() { }
}
"""})
        self.generate()
        record = self.records()["tests/ExampleTests.cs"]
        self.assertEqual(
            {"Fact": 2, "Theory": 1, "authoredAttributedMethodDeclarations": 3},
            record["counts"],
        )
        self.assertEqual(
            [
                {"method": "Example", "line": 3}, {"method": "Cases", "line": 5},
                {"method": "LastAttribute", "line": 7},
            ],
            record["definitions"],
        )

    def test_separates_framework_definitions_from_parameter_expansion(self) -> None:
        self.commit_sources({
            "src/fixture/src/test/ExampleTests.java": """class ExampleTests {
@Test
void example() { }
@ParameterizedTest
@ValueSource(ints = {1, 2})
void cases(int input) { }
@RepeatedTest(3)
void repeated() { }
}
""",
            "tests/test_fixture.py": """import unittest
class Examples(unittest.TestCase):
    def test_method(self): pass
async def test_async(): pass
def helper(): pass
""",
            "tests/node.test.mjs": """import test from 'node:test';
test('example', () => {});
test.skip('disabled', () => {});
""",
            "tests/fixture.test.js": """import {it, test} from 'vitest';
test.each([1, 2])('case', () => {});
it.skip('disabled', () => {});
it.todo('planned');
""",
            "tests/test_fixture.sh": "exit 0\n",
            "tests/test_fixture.ps1": "exit 0\n",
            "tests/fixture.test.js.snap": "test('snapshot is not a test');\n",
        })
        self.generate()
        records = self.records()
        java = records["src/fixture/src/test/ExampleTests.java"]
        self.assertEqual("JUnit", java["framework"])
        self.assertEqual(
            {"testAnnotations": 3, "authoredAttributedMethodDeclarations": 3},
            java["counts"],
        )
        self.assertEqual({"authoredFunctions": 2}, records["tests/test_fixture.py"]["counts"])
        node = records["tests/node.test.mjs"]
        self.assertEqual("Node test", node["framework"])
        self.assertEqual({"authoredCallSites": 2, "parameterizedCallSites": 0}, node["counts"])
        vitest = records["tests/fixture.test.js"]
        self.assertEqual("Vitest", vitest["framework"])
        self.assertEqual({"authoredCallSites": 3, "parameterizedCallSites": 1}, vitest["counts"])
        for name in ("tests/test_fixture.sh", "tests/test_fixture.ps1"):
            self.assertEqual("Standalone shell scripts", records[name]["framework"])
            self.assertEqual({"scripts": 1}, records[name]["counts"])
        self.assertNotIn("tests/fixture.test.js.snap", records)


if __name__ == "__main__":
    unittest.main()
