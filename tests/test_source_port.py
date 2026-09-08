import json
import unittest
import xml.etree.ElementTree as element_tree
from pathlib import Path
from urllib.parse import urlparse


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
EXAMPLE_ROOT = REPOSITORY_ROOT / "src" / "DeveloperToolbox.Example"
PIPELINE_ROOT = REPOSITORY_ROOT / "devops" / "pipelines"


class SourcePortTests(unittest.TestCase):
    def test_generic_source_structure_is_present(self):
        expected_paths = (
            REPOSITORY_ROOT / "DeveloperToolbox.Example.slnx",
            EXAMPLE_ROOT / "DeveloperToolbox.Example.App",
            EXAMPLE_ROOT / "DeveloperToolbox.Example.Lib",
            EXAMPLE_ROOT / "DeveloperToolbox.Example.Tests",
            EXAMPLE_ROOT / "DeveloperToolbox.Example.Client",
            REPOSITORY_ROOT / "devops" / "DeveloperToolbox.Example.Lib.nuspec",
            REPOSITORY_ROOT / "devops" / "PrepNuget.ps1",
            PIPELINE_ROOT / "Project-Build.yml",
            PIPELINE_ROOT / "Project-Build-Docker.yml",
            PIPELINE_ROOT / "Project-Build-CloudFoundry.yml",
            PIPELINE_ROOT / "Project-NuGet-Build.yml",
            PIPELINE_ROOT / "Project-NuGet-NuSpec-Build.yml",
        )

        missing_paths = [str(path) for path in expected_paths if not path.exists()]

        self.assertEqual([], missing_paths)

    def test_generic_port_contains_no_private_enterprise_references(self):
        blocked_terms = (
            "jf" + "rog",
            "fest" + "ack",
            "ics" + "-eng",
            "church" + "ofjesuschrist",
            "art" + "ifactory",
            "sonar" + "qube",
        )
        roots = (EXAMPLE_ROOT, REPOSITORY_ROOT / "devops")
        text_extensions = {
            ".css",
            ".cs",
            ".csproj",
            ".js",
            ".json",
            ".jsx",
            ".md",
            ".mjs",
            ".npmrc",
            ".ps1",
            ".slnx",
            ".yml",
        }
        extensionless_names = {"dockerfile"}
        named_files = {".env.local.example"}
        violations = []

        for root in roots:
            for path in root.rglob("*"):
                is_text_file = (
                    path.suffix.lower() in text_extensions
                    or path.name.lower() in extensionless_names
                    or path.name.lower() in named_files
                )
                if not path.is_file() or not is_text_file:
                    continue
                normalized_parts = {part.lower() for part in path.parts}
                if normalized_parts.intersection({"bin", "obj", "node_modules", ".next"}):
                    continue
                content = path.read_text(encoding="utf-8").lower()
                for term in blocked_terms:
                    if term in content:
                        violations.append(f"{path.relative_to(REPOSITORY_ROOT)}: {term}")

        self.assertEqual([], violations)

    def test_example_pipelines_are_disabled_and_self_contained(self):
        pipeline_paths = sorted(PIPELINE_ROOT.glob("Project-*.yml"))

        self.assertEqual(5, len(pipeline_paths))
        for pipeline_path in pipeline_paths:
            content = pipeline_path.read_text(encoding="utf-8")
            self.assertIn("trigger: none", content, pipeline_path.name)
            self.assertIn("pr: none", content, pipeline_path.name)
            self.assertNotIn("resources:\n", content, pipeline_path.name)

    def test_nuspec_is_well_formed_and_generic(self):
        nuspec_path = REPOSITORY_ROOT / "devops" / "DeveloperToolbox.Example.Lib.nuspec"
        root = element_tree.parse(nuspec_path).getroot()
        namespace = {"n": "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"}

        self.assertEqual(
            "DeveloperToolbox.Example.Lib",
            root.findtext("n:metadata/n:id", namespaces=namespace),
        )
        self.assertEqual(
            "MIT",
            root.findtext("n:metadata/n:license", namespaces=namespace),
        )
        self.assertEqual(
            "README.md",
            root.findtext("n:metadata/n:readme", namespaces=namespace),
        )

    def test_client_lockfile_uses_the_public_npm_registry(self):
        lockfile_path = EXAMPLE_ROOT / "DeveloperToolbox.Example.Client" / "package-lock.json"
        lockfile = json.loads(lockfile_path.read_text(encoding="utf-8"))
        unexpected_hosts = set()

        for package in lockfile.get("packages", {}).values():
            resolved = package.get("resolved")
            if resolved and urlparse(resolved).hostname != "registry.npmjs.org":
                unexpected_hosts.add(urlparse(resolved).hostname)

        self.assertEqual(set(), unexpected_hosts)


if __name__ == "__main__":
    unittest.main()
