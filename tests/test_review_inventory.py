"""Regressions for framework selection in the current, commit-based inventory."""

import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "docs/reviews/inventory-source.py"
SPEC = importlib.util.spec_from_file_location("review_inventory", SCRIPT)
INVENTORY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(INVENTORY)


class ReviewInventoryTests(unittest.TestCase):
    def inventory(self, sources, repository="AGameEmpowerment/Developer-Toolbox", working_sources=None):
        temporary = tempfile.TemporaryDirectory(prefix="toolbox-review-inventory-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)

        def git(*arguments):
            return subprocess.check_output(["git", "-C", str(root), *arguments], stderr=subprocess.PIPE)

        git("init", "--quiet")
        git("config", "core.autocrlf", "false")
        git("config", "core.hooksPath", str(root / "unused-hooks"))
        for name, content in sources.items():
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        git("add", ".")
        git("-c", "user.name=Toolbox Test", "-c", "user.email=toolbox-test@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "Synthetic test inventory")
        commit = git("rev-parse", "HEAD").decode().strip()
        for name, content in (working_sources or {}).items():
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        worktree = working_sources is not None
        if worktree:
            with self.assertRaises(SystemExit):
                INVENTORY.inventory(root, repository, commit, output_dir="inventory")
        INVENTORY.inventory(root, repository, commit, output_dir="inventory", worktree=worktree)
        INVENTORY.inventory(root, repository, commit, check=True, output_dir="inventory", worktree=worktree)
        report = json.loads((root / "inventory/test-inventory.json").read_text(encoding="utf-8"))
        if worktree:
            self.assertEqual(commit, report["sourceCommit"])
            self.assertIn("Uncommitted", report["sourceState"])
            self.assertEqual(set(sources) | set(working_sources), set(report["sourceManifest"]))
        return {record["source"]: record for record in report["files"]}

    def test_worktree_inventory_includes_modified_and_untracked_tests(self):
        records = self.inventory(
            {"tests/test_original.py": "def test_old(): pass\n"},
            working_sources={
                "tests/test_original.py": "def test_changed(): pass\ndef test_second(): pass\n",
                "tests/test_added.py": "def test_new(): pass\n",
            },
        )
        self.assertEqual(2, records["tests/test_original.py"]["counts"]["authoredFunctions"])
        self.assertEqual(1, records["tests/test_added.py"]["counts"]["authoredFunctions"])

    def test_nunit_cases_in_toolbox_are_counted_once_per_authored_method(self):
        records = self.inventory({
            "tests/Example.Tests.csproj": '<Project><ItemGroup><PackageReference Include="NUnit" Version="5.0.0" /></ItemGroup></Project>',
            "tests/ExampleTests.cs": """class ExampleTests {
[TestCase(1), TestCase(2)]
public void Inline(int input) { }
[TestCaseSource(nameof(Cases))]
public async Task Source(int input) { }
[Theory]
public void Ambiguous(bool input) { }
}
""",
        })
        record = records["tests/ExampleTests.cs"]
        self.assertEqual("NUnit", record["framework"])
        self.assertEqual({"TestCase": 2, "TestCaseSource": 1, "Theory": 1,
                          "authoredAttributedMethodDeclarations": 3}, record["counts"])
        self.assertEqual(["Inline", "Source", "Ambiguous"], [item["method"] for item in record["definitions"]])

    def test_closest_package_reference_classifies_theory_independently_of_repo_name(self):
        records = self.inventory({
            "tests/Parent.csproj": '<Project><ItemGroup><PackageReference Include="NUnit" /></ItemGroup></Project>',
            "tests/NUnitTests.cs": "[Theory]\npublic void Native(bool value) { }",
            "tests/legacy/Legacy.csproj": '<Project><ItemGroup><PackageReference Include="xunit" /></ItemGroup></Project>',
            "tests/legacy/LegacyTests.cs": "[Theory]\npublic void Historical(bool value) { }",
        }, repository="AGameEmpowerment/An-Unrelated-Repository")
        self.assertEqual("NUnit", records["tests/NUnitTests.cs"]["framework"])
        self.assertEqual("xUnit", records["tests/legacy/LegacyTests.cs"]["framework"])

    def test_fully_qualified_nunit_and_junit_annotations_are_not_omitted(self):
        records = self.inventory({
            "tests/ExampleTests.cs": "[NUnit.Framework.TestCase(1)]\npublic void Example(int value) { }",
            "src/example/src/test/ExampleTests.java": """class ExampleTests {
@org.junit.jupiter.params.ParameterizedTest
@org.junit.jupiter.params.provider.ValueSource(ints = {1, 2})
void example(int value) { }
}
""",
        })
        self.assertEqual(1, records["tests/ExampleTests.cs"]["counts"]["authoredAttributedMethodDeclarations"])
        self.assertEqual(1, records["src/example/src/test/ExampleTests.java"]["counts"]["testAnnotations"])


if __name__ == "__main__":
    unittest.main()
