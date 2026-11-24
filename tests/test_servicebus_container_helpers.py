"""Check isolated Service Bus wiring without a container engine."""

import copy
import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from test_servicebus_containers import ROOT, isolated_definition, verify


class ServiceBusIsolationTests(unittest.TestCase):
    def definition(self, volumes):
        return {"services": {
            "mssql": {"container_name": "SqlServer", "build": {"context": "shared"},
                "environment": {"MSSQL_SA_PASSWORD": "auto", "SA_PASSWORD": "auto"},
                "volumes": [{"type": "volume", "source": "shared-sql-volume", "target": "/var/opt/mssql"}],
                "ports": ["10433:1433"], "networks": {"sb-emulator": {}}, "restart": "unless-stopped"},
            "service-bus": {"image": "common-emulator-image", "container_name": "ServiceBus",
                "environment": {"SQL_SERVER": "shared-sql", "MSSQL_SA_PASSWORD": "auto"}, "volumes": volumes,
                "ports": ["5672:5672"], "depends_on": {"mssql": {}}, "networks": {"sb-emulator": {}}}}}

    def test_provider_mount_formats_never_reuse_shared_storage_or_writable_config(self):
        for mounts in ([{"type": "bind", "source": "shared-config", "target": "/config"}],
                       ["C:\\shared\\Config.json:/config:z"]):
            with self.subTest(mounts=mounts):
                common = self.definition(mounts)
                original = copy.deepcopy(common)
                copied = isolated_definition(common, "isolated-test", "built-common-sql")
                sql, broker = copied["services"]["mssql"], copied["services"]["service-bus"]
                self.assertEqual([{"type": "volume", "source": "mssql-data", "target": "/var/opt/mssql"}], sql["volumes"])
                config = broker["volumes"][0]
                self.assertEqual(str(ROOT / "containers/service-bus/Config.json"), config["source"])
                self.assertTrue(config["read_only"])
                self.assertEqual("z", config["bind"]["selinux"])
                self.assertEqual([], sql["ports"])
                self.assertEqual(["127.0.0.1:0:5672", "127.0.0.1:0:5300"], broker["ports"])
                self.assertEqual("built-common-sql", sql["image"])
                self.assertEqual("common-emulator-image", broker["image"])
                self.assertEqual(original, common)

    def test_unique_projects_cannot_share_container_network_or_volume_names(self):
        common = self.definition([])
        first = isolated_definition(common, "first-test", "built-common-sql")
        second = isolated_definition(common, "second-test", "built-common-sql")
        for copied in (first, second):
            self.assertEqual("mssql", copied["services"]["service-bus"]["environment"]["SQL_SERVER"])
            for name, service in copied["services"].items():
                self.assertEqual({"default": {"aliases": [name]}}, service["networks"])
                self.assertEqual("no", service["restart"])
        self.assertNotEqual(first["volumes"]["mssql-data"]["name"], second["volumes"]["mssql-data"]["name"])
        self.assertNotEqual(first["networks"]["default"]["name"], second["networks"]["default"]["name"])
        for service in ("mssql", "service-bus"):
            self.assertNotEqual(first["services"][service]["container_name"], second["services"][service]["container_name"])

    def test_disposable_credentials_replace_both_sql_names_and_match_the_broker(self):
        for inherited in ("auto", "explicit-shared-developer-password"):
            with self.subTest(inherited=inherited):
                common = self.definition([])
                common["services"]["mssql"]["environment"].update(
                    MSSQL_SA_PASSWORD=inherited, SA_PASSWORD=inherited)
                common["services"]["service-bus"]["environment"]["MSSQL_SA_PASSWORD"] = inherited
                original = copy.deepcopy(common)
                first = isolated_definition(common, "first-test", "built-common-sql")
                second = isolated_definition(common, "second-test", "built-common-sql")
                sql = first["services"]["mssql"]["environment"]
                password = sql["MSSQL_SA_PASSWORD"]
                self.assertNotEqual(inherited, password)
                self.assertGreaterEqual(len(password), 32)
                for character_class in (r"[A-Z]", r"[a-z]", r"[0-9]", r"[^a-zA-Z0-9]"):
                    self.assertRegex(password, character_class)
                self.assertEqual(password, sql["SA_PASSWORD"])
                self.assertEqual(password, first["services"]["service-bus"]["environment"]["MSSQL_SA_PASSWORD"])
                self.assertNotEqual(password, second["services"]["mssql"]["environment"]["MSSQL_SA_PASSWORD"])
                self.assertEqual(original, common)

    def verify_with_fake_runtime(self, failure=None, cleanup_failure=False):
        """Exercise orchestration and diagnostics without changing a real engine."""
        calls, definitions = [], []
        stdout, stderr = io.StringIO(), io.StringIO()
        observed_error = None

        def fake_run(runtime, *arguments, timeout=120):
            calls.append((runtime, arguments, timeout))
            definition = json.loads(Path(arguments[arguments.index("-f") + 1]).read_text(encoding="utf-8"))
            definitions.append(definition)
            password = definition["services"]["mssql"]["environment"]["MSSQL_SA_PASSWORD"]
            if failure and "up" in arguments:
                raise RuntimeError("Provider startup failed with password " + password)
            if "logs" in arguments:
                return "SQL startup diagnostic; password=" + password
            if cleanup_failure and "down" in arguments:
                raise RuntimeError("Provider cleanup failed with password " + password)
            return ""

        with tempfile.TemporaryDirectory() as temporary, contextlib.ExitStack() as stack:
            assembly = Path(temporary) / "example.dll"
            assembly.touch()
            stack.enter_context(patch("test_servicebus_containers.shared_definition", return_value=self.definition([])))
            stack.enter_context(patch("test_servicebus_containers.built_image", return_value="built-common-sql"))
            stack.enter_context(patch("test_servicebus_containers.run", side_effect=fake_run))
            sql_ready = stack.enter_context(patch("test_servicebus_containers.await_sql"))
            stack.enter_context(patch("test_servicebus_containers.await_broker"))
            stack.enter_context(patch("test_servicebus_containers.broker_port", return_value=12345))
            stack.enter_context(patch("test_servicebus_containers.validate_examples", return_value={"passed": 2, "failed": 0}))
            stack.enter_context(contextlib.redirect_stdout(stdout))
            stack.enter_context(contextlib.redirect_stderr(stderr))
            try:
                verify("docker", assembly)
            except Exception as error:
                observed_error = error
        return calls, definitions, sql_ready, stdout.getvalue(), stderr.getvalue(), observed_error

    def test_readiness_precedes_broker_start_and_uses_three_minute_sql_budget(self):
        calls, definitions, sql_ready, stdout, stderr, error = self.verify_with_fake_runtime()
        self.assertIsNone(error)
        project = calls[0][1][calls[0][1].index("-p") + 1]
        self.assertTrue(project.startswith("toolbox-servicebus-"))
        sql_ready.assert_called_once_with("docker", project + "-mssql", timeout=180)
        self.assertEqual("mssql", calls[0][1][-1])
        self.assertEqual("service-bus", calls[1][1][-1])
        self.assertEqual(("down", "--volumes"), calls[-1][1][-2:])
        for runtime, arguments, timeout in calls:
            self.assertEqual(project, arguments[arguments.index("-p") + 1])
        password = definitions[0]["services"]["mssql"]["environment"]["MSSQL_SA_PASSWORD"]
        self.assertNotIn(password, stdout + stderr)

    def test_startup_failure_redacts_logs_and_exception_and_still_removes_only_its_project(self):
        calls, definitions, _, stdout, stderr, error = self.verify_with_fake_runtime(failure=True)
        self.assertIsNotNone(error)
        password = definitions[0]["services"]["mssql"]["environment"]["MSSQL_SA_PASSWORD"]
        self.assertNotIn(password, stdout + stderr + str(error))
        self.assertIn("SQL startup diagnostic", stderr)
        self.assertIn("[redacted local password]", stderr)
        self.assertEqual(("down", "--volumes"), calls[-1][1][-2:])
        projects = {arguments[arguments.index("-p") + 1] for _, arguments, _ in calls}
        self.assertEqual(1, len(projects))
        self.assertTrue(next(iter(projects)).startswith("toolbox-servicebus-"))

    def test_cleanup_failure_does_not_expose_provider_credentials(self):
        _, definitions, _, stdout, stderr, error = self.verify_with_fake_runtime(cleanup_failure=True)
        self.assertIsNotNone(error)
        password = definitions[0]["services"]["mssql"]["environment"]["MSSQL_SA_PASSWORD"]
        self.assertNotIn(password, stdout + stderr + str(error))


if __name__ == "__main__":
    unittest.main()
