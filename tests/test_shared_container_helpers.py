"""Verify validation fixtures resolve resources from their owning Compose groups."""

import json
from pathlib import Path
import unittest
from unittest.mock import patch

import container_test_support as shared


class SharedDefinitionTests(unittest.TestCase):
    def test_renders_each_group_separately_and_combines_fixture_resources(self):
        calls = []

        def engine(runtime, *arguments, **kwargs):
            calls.append(arguments)
            if arguments == ("compose", "config", "--help"):
                return "--format"
            project = arguments[arguments.index("-p") + 1]
            group = project.removeprefix("dev_").removesuffix("_shared")
            self.assertEqual(shared.ROOT / f"containers/docker-compose-{group}.yml",
                             Path(arguments[arguments.index("-f") + 1]))
            return json.dumps({"services": {group: {"image": group}},
                               "volumes": {group + "-data": {"name": group}},
                               "networks": {"default": {"name": project + "_default"}},
                               "x-podman": {"in_pod": False}})

        with patch.object(shared, "run", side_effect=engine):
            result = shared.shared_definition("docker")
        self.assertEqual({"common", "azure", "aws"}, set(result["services"]))
        self.assertEqual({"common-data", "azure-data", "aws-data"}, set(result["volumes"]))
        self.assertEqual({"in_pod": False}, result["x-podman"])
        self.assertEqual(4, len(calls))
        self.assertTrue(all("config" in call for call in calls))

    def test_group_render_failure_is_propagated(self):
        with patch.object(shared, "run", side_effect=["--format", RuntimeError("invalid group")]):
            with self.assertRaisesRegex(RuntimeError, "invalid group"):
                shared.shared_definition("docker")


class BuiltImageTests(unittest.TestCase):
    def test_uses_owning_cloud_project_for_docker_and_podman_image_names(self):
        for service, project in (("mssql", "dev_azure_shared"),
                                 ("aws-emulator", "dev_aws_shared")):
            for separator in ("-", "_"):
                expected = project + separator + service
                attempted = []

                def inspect(runtime, *arguments, **kwargs):
                    attempted.append(arguments[-1])
                    if arguments[-1] != expected:
                        raise RuntimeError("missing image")
                    return "[]"

                with self.subTest(service=service, separator=separator):
                    with patch.object(shared, "run", side_effect=inspect):
                        self.assertEqual(expected, shared.built_image("podman", {}, service, None))
                    self.assertTrue(all(name.startswith(project) for name in attempted))

    def test_explicit_image_overrides_project_lookup(self):
        with patch.object(shared, "run", return_value="[]") as engine:
            self.assertEqual("custom-sql", shared.built_image("docker", {}, "mssql", "custom-sql"))
        engine.assert_called_once_with("docker", "image", "inspect", "custom-sql", timeout=30)


if __name__ == "__main__":
    unittest.main()
