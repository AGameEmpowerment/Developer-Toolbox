#!/usr/bin/env python3
"""Verify safe dotenv parsing and local sample settings generation without Docker."""

import importlib.util
import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("configure_examples", ROOT / "setup" / "configure_examples.py")
CONFIGURE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CONFIGURE)


class DotenvTests(unittest.TestCase):
    def resolve(self, source, environment=None, selected=None):
        assignments = CONFIGURE.parse_dotenv(source)
        return CONFIGURE.resolve_settings(assignments, environment or {}, set(assignments) if selected is None else selected)

    def test_single_quoted_password_keeps_semicolon_dollar_and_hash(self):
        result = self.resolve("PASSWORD='P@ss;${OTHER};$literal#tail'", {"OTHER": "wrong"})
        self.assertEqual("P@ss;${OTHER};$literal#tail", result["PASSWORD"])

    def test_double_quoted_value_supports_quotes_and_literal_dollars(self):
        result = self.resolve(r'VALUE="quote\";$$literal;\$literal;line\nnext"')
        self.assertEqual('quote";$literal;$literal;line\nnext', result["VALUE"])

    def test_comments_and_unquoted_connection_strings_remain_data(self):
        result = self.resolve('VALUE=Password=abc#123;Key=value # comment\nexport OTHER="a # b" # comment')
        self.assertEqual("Password=abc#123;Key=value", result["VALUE"])
        self.assertEqual("a # b", result["OTHER"])

    def test_shell_precedence_preserves_override_as_a_literal(self):
        result = self.resolve('PASSWORD="file"\nCOPY="${PASSWORD}"', {"PASSWORD": '$literal;"quote"'})
        self.assertEqual('$literal;"quote"', result["COPY"])

    def test_forward_references_and_literal_referenced_password(self):
        result = self.resolve('COPY="${PASSWORD}"\nPASSWORD=\'${NOT_EXPANDED};pw\'')
        self.assertEqual("${NOT_EXPANDED};pw", result["COPY"])

    def test_empty_and_unset_fallback_semantics(self):
        result = self.resolve('EMPTY=""\nA=${EMPTY:-fallback}\nB=${EMPTY-fallback}\nC=${MISSING-fallback}\nD=${EMPTY:+alternate}\nE=${EMPTY+alternate}')
        self.assertEqual(("fallback", "", "fallback", "", "alternate"), tuple(result[name] for name in "ABCDE"))

    def test_cycle_fails_without_disclosing_values(self):
        with self.assertRaisesRegex(ValueError, "Cyclic dotenv"):
            self.resolve('A=${B}\nB=${A}')

    def test_missing_required_value_hides_custom_error_message(self):
        with self.assertRaises(ValueError) as raised:
            self.resolve('A=${MISSING:?private diagnostic value}')
        self.assertNotIn("private diagnostic", str(raised.exception))

    def test_invalid_assignment_and_quote_errors_never_log_source(self):
        for source in ("bad token-secret", 'PASSWORD="token-secret'):
            with self.subTest(source=source), self.assertRaises(ValueError) as raised:
                CONFIGURE.parse_dotenv(source)
            self.assertNotIn("token-secret", str(raised.exception))

    def test_shell_command_syntax_is_literal_and_never_executed(self):
        result = self.resolve("VALUE='$(echo secret);`echo secret`'")
        self.assertEqual("$(echo secret);`echo secret`", result["VALUE"])

    def test_interpolated_database_components_are_provider_quoted(self):
        result = self.resolve('MSSQL_SA_PASSWORD=\'p;"q";$dollar\'\nPOSTGRES_USER=\'u;"q"\'\nPOSTGRES_PASSWORD=\'p;"q";$dollar\'\nConnectionStrings__SqlServer="Server=localhost,10433;Password=${MSSQL_SA_PASSWORD};Database=ToolboxSamples"\nConnectionStrings__Postgres="Host=localhost;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"')
        self.assertEqual('Server=localhost,10433;Password="p;""q"";$dollar";Database=ToolboxSamples', result["ConnectionStrings__SqlServer"])
        self.assertEqual('Host=localhost;Username="u;""q""";Password="p;""q"";$dollar"', result["ConnectionStrings__Postgres"])

    def test_explicit_connection_string_override_is_not_rewritten(self):
        result = self.resolve('ConnectionStrings__Postgres="Host=localhost;Password=original"', {"ConnectionStrings__Postgres": 'Host=localhost;Password="explicit;value"'})
        self.assertEqual('Host=localhost;Password="explicit;value"', result["ConnectionStrings__Postgres"])

    def test_localstack_token_reference_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "licensing tokens"):
            self.resolve('Resources__Secret="${LOCALSTACK_AUTH_TOKEN}"', {"LOCALSTACK_AUTH_TOKEN": "private-token"})


class ConfigureTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.containers = self.root / "containers"
        self.containers.mkdir()
        self.template = self.containers / ".env.example"
        self.template.write_text('MSSQL_SA_PASSWORD="template-password"\nLOCALSTACK_AUTH_TOKEN=""\nResources__S3__Endpoint="http://localhost:10195"\nResources__Cosmos__Key="local-emulator-key"\nConnectionStrings__SqlServer="Server=localhost,10433;Password=${MSSQL_SA_PASSWORD}"\nAzureWebJobsStorage="UseDevelopmentStorage=true"\nToolboxAzureStorage="UseDevelopmentStorage=true"\n', encoding="utf-8")
        self.dotenv = self.containers / ".env"
        self.dotenv.write_text('MSSQL_SA_PASSWORD=\'edited;"password";$literal\'\nLOCALSTACK_AUTH_TOKEN="local-private-token"\n', encoding="utf-8")
        self.app = self.root / "src" / "DeveloperToolbox.Example" / "DeveloperToolbox.Example.App" / "appsettings.json"
        self.functions = self.root / "src" / "DeveloperToolbox.Example" / "DeveloperToolbox.Example.Functions" / "local.settings.json"
        self.shared = self.containers / "examples.settings.json"

    def test_generation_merges_new_template_defaults_and_keeps_settings_shape(self):
        result = CONFIGURE.configure(self.root, environment={})
        self.assertTrue(all(written for _, written in result))
        app = json.loads(self.app.read_text())
        functions = json.loads(self.functions.read_text())
        self.assertEqual("http://localhost:10195", app["Resources"]["S3"]["Endpoint"])
        self.assertEqual('Server=localhost,10433;Password="edited;""password"";$literal"', app["ConnectionStrings"]["SqlServer"])
        self.assertEqual("local-emulator-key", functions["Values"]["Resources__Cosmos__Key"])
        self.assertEqual("dotnet-isolated", functions["Values"]["FUNCTIONS_WORKER_RUNTIME"])
        self.assertEqual(7071, functions["Host"]["LocalHttpPort"])
        self.assertFalse(functions["IsEncrypted"])

    def test_parent_environment_only_overrides_canonical_selected_settings(self):
        CONFIGURE.configure(self.root, environment={"Resources__S3__Endpoint": "http://localhost:9999", "Resources__UnlistedSecret": "parent-private-secret", "LOCALSTACK_AUTH_TOKEN": "parent-private-token", "UNRELATED_API_KEY": "unrelated-private-secret"})
        content = self.app.read_text() + self.functions.read_text() + self.shared.read_text()
        self.assertIn("http://localhost:9999", content)
        for forbidden in ("private-token", "private-secret", "LOCALSTACK_AUTH_TOKEN", "UNRELATED_API_KEY", "MSSQL_SA_PASSWORD"):
            self.assertNotIn(forbidden, content)

    def test_existing_configs_are_preserved_independently(self):
        self.app.parent.mkdir(parents=True)
        self.app.write_text('{"custom":"keep"}', encoding="utf-8")
        result = CONFIGURE.configure(self.root, environment={})
        self.assertEqual('{"custom":"keep"}', self.app.read_text())
        self.assertEqual([False, True, True, True, True, True], [written for _, written in result])

    def test_neutral_settings_are_flat_and_preserve_existing_file(self):
        CONFIGURE.configure(self.root, environment={})
        shared = json.loads(self.shared.read_text())
        self.assertEqual("local-emulator-key", shared["Resources__Cosmos__Key"])
        self.assertNotIn("FUNCTIONS_WORKER_RUNTIME", shared)
        self.shared.write_text('{"custom":"keep"}')
        CONFIGURE.configure(self.root, environment={})
        self.assertEqual('{"custom":"keep"}', self.shared.read_text())

    def test_neutral_storage_expands_canonical_key_without_changing_dotnet_settings(self):
        with self.template.open("a", encoding="utf-8") as template:
            template.write('Resources__AzureStorage__ConnectionString="UseDevelopmentStorage=true"\nTOOLBOX_CONTAINER_AZURE_STORAGE_CONNECTION="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=external-public-key;BlobEndpoint=http://azurite:10000/devstoreaccount1;QueueEndpoint=http://azurite:10001/devstoreaccount1;TableEndpoint=http://azurite:10002/devstoreaccount1;"\n')
        CONFIGURE.configure(self.root, environment={})
        shared = json.loads(self.shared.read_text())
        self.assertIn("AccountKey=external-public-key;", shared["Resources__AzureStorage__ConnectionString"])
        for port in (10000, 10001, 10002):
            self.assertIn(f"localhost:{port}/devstoreaccount1", shared["Resources__AzureStorage__ConnectionString"])
        self.assertNotIn("TOOLBOX_CONTAINER_AZURE_STORAGE_CONNECTION", shared)
        self.assertEqual("UseDevelopmentStorage=true", json.loads(self.functions.read_text())["Values"]["ToolboxAzureStorage"])

    def test_current_template_expands_development_shortcut_without_retired_container_setting(self):
        self.template.write_text((ROOT / "containers/.env.example").read_text(), encoding="utf-8")
        outputs = CONFIGURE.configure(self.root, environment={
            "Resources__AzureStorage__ConnectionString": "UseDevelopmentStorage=true"
        })
        for path, _ in (outputs[2], *outputs[3:]):
            content = json.loads(path.read_text())
            values = content.get("Values", content)
            connection = values["Resources__AzureStorage__ConnectionString"]
            self.assertIn("AccountName=devstoreaccount1;", connection)
            for port in (10000, 10001, 10002):
                self.assertIn(f"localhost:{port}/devstoreaccount1", connection)
            self.assertNotIn("TOOLBOX_CONTAINER_AZURE_STORAGE_CONNECTION", values)

    def test_neutral_storage_preserves_explicit_connection_override(self):
        with self.template.open("a", encoding="utf-8") as template:
            template.write('Resources__AzureStorage__ConnectionString="UseDevelopmentStorage=true"\n')
        value = "DefaultEndpointsProtocol=http;AccountName=custom;AccountKey=custom;BlobEndpoint=http://localhost:1234/custom;"
        CONFIGURE.configure(self.root, environment={"Resources__AzureStorage__ConnectionString": value})
        self.assertEqual(value, json.loads(self.shared.read_text())["Resources__AzureStorage__ConnectionString"])

    def test_obsolete_runtime_container_settings_are_not_copied(self):
        with self.template.open("a", encoding="utf-8") as template:
            template.write('Validation__Functions__Endpoint="http://localhost:7071"\nValidation__Functions__Key=""\nValidation__Lambda__Endpoint="http://localhost:10196"\n')
        CONFIGURE.configure(self.root, environment={"Validation__Functions__Key": "synthetic-function-key"})
        app = json.loads(self.app.read_text())
        self.assertNotIn("Validation", app)
        self.assertNotIn("synthetic-function-key", self.shared.read_text())

    def test_independent_function_projects_get_matching_worker_port_and_unique_host_id(self):
        with self.template.open("a", encoding="utf-8") as template:
            template.write('JAVA_SQLSERVER_HOST="127.0.0.1"\nPOSTGRES_PASSWORD="${MSSQL_SA_PASSWORD}"\n')
        outputs = CONFIGURE.configure(self.root, environment={})
        worker_values = []
        for (path, _), language, port in zip(outputs[3:], ("java", "node", "python"), (7072, 7073, 7074)):
            settings = json.loads(path.read_text())
            values = settings["Values"]
            self.assertEqual(language, values["FUNCTIONS_WORKER_RUNTIME"])
            self.assertEqual("toolbox-" + language, values["AzureFunctionsWebHost__hostid"])
            self.assertEqual(port, settings["Host"]["LocalHttpPort"])
            self.assertNotIn("LOCALSTACK_AUTH_TOKEN", values)
            if language == "java":
                self.assertEqual("127.0.0.1", values["JAVA_SQLSERVER_HOST"])
                self.assertEqual('edited;"password";$literal', values["MSSQL_SA_PASSWORD"])
            else:
                self.assertNotIn("MSSQL_SA_PASSWORD", values)
            worker_values.append(values["AzureFunctionsWebHost__hostid"])
        self.assertEqual(3, len(set(worker_values)))

    def test_force_replaces_existing_config(self):
        CONFIGURE.configure(self.root, environment={})
        self.app.write_text('{"custom":"replace"}', encoding="utf-8")
        CONFIGURE.configure(self.root, force=True, environment={})
        self.assertNotIn("custom", json.loads(self.app.read_text()))

    def test_failed_force_replacement_preserves_settings_and_removes_temporary_file(self):
        CONFIGURE.configure(self.root, environment={})
        original = self.app.read_bytes()
        with patch.object(CONFIGURE.os, "replace", side_effect=OSError("fixture failure")):
            with self.assertRaises(OSError):
                CONFIGURE.configure(self.root, force=True, environment={})
        self.assertEqual(original, self.app.read_bytes())
        self.assertEqual([self.app], list(self.app.parent.iterdir()))

    @unittest.skipIf(os.name == "nt", "POSIX permission bits require a Linux/macOS filesystem")
    def test_new_and_forced_settings_are_owner_readable_only(self):
        outputs = CONFIGURE.configure(self.root, environment={})
        for path, _ in outputs:
            self.assertEqual(0o600, path.stat().st_mode & 0o777)
            path.chmod(0o644)
        outputs = CONFIGURE.configure(self.root, force=True, environment={})
        for path, _ in outputs:
            self.assertEqual(0o600, path.stat().st_mode & 0o777)

    @unittest.skipIf(os.name == "nt", "Unprivileged file symlinks are not always enabled on Windows")
    def test_forced_settings_replace_the_named_file_without_following_a_symlink(self):
        self.app.parent.mkdir(parents=True)
        outside = self.root / "unrelated-settings.json"
        outside.write_text('{"keep":"unrelated"}', encoding="utf-8")
        self.app.symlink_to(outside)
        CONFIGURE.configure(self.root, force=True, environment={})
        self.assertEqual('{"keep":"unrelated"}', outside.read_text())
        self.assertFalse(self.app.is_symlink())
        self.assertIn("ConnectionStrings", json.loads(self.app.read_text()))

    def test_missing_dotenv_has_actionable_error(self):
        self.dotenv.unlink()
        with self.assertRaisesRegex(ValueError, "Create containers/.env"):
            CONFIGURE.configure(self.root, environment={})

    def test_conflicting_nested_paths_fail_before_any_config_write(self):
        with self.assertRaisesRegex(ValueError, "conflicting"):
            CONFIGURE.nested_settings({"Resources__S3": "value", "Resources__S3__Endpoint": "nested"})


if __name__ == "__main__":
    unittest.main()
