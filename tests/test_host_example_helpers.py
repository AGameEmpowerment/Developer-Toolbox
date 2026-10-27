"""Deterministic host fixture regressions; no SDK or container engine is needed."""

import base64
import contextlib
import copy
import io
import json
from pathlib import Path
import shlex
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import test_host_examples as host


PROJECT = "toolbox-host-" + "a" * 32
DEFAULTS = {
    "Resources__AzureStorage__ConnectionString": "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=public-development-key;BlobEndpoint=http://localhost:10000/devstoreaccount1;QueueEndpoint=http://localhost:10001/devstoreaccount1;TableEndpoint=http://localhost:10002/devstoreaccount1;",
    "ServiceBusConnection": "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=public-development-key;UseDevelopmentEmulator=true;",
    "Resources__Cosmos__Key": "public-development-key",
}
PORTS = {
    "mssql": (1433,), "postgres": (5432,), "cosmosdb": (8081, 1234),
    "dynamodb": (8000,), "dynamodb-volume-init": (), "redis": (6379,),
    "smtp4dev": (25, 110, 143, 80), "otlp-seq": (80,),
    "wiremock": (8080, 8443), "azurite": (10000, 10001, 10002),
    "service-bus": (5672, 5672, 5300), "rabbitmq": (5672, 15672),
    "aws-emulator": (5000,), "aws-init": (),
}
MOUNTS = {
    "mssql": ("mssql-data", "/var/opt/mssql"),
    "postgres": ("postgres-data", "/var/lib/postgresql"),
    "cosmosdb": ("cosmos-data", "/data"),
    "dynamodb": ("dynamodb-data", "/home/dynamodblocal/data"),
    "dynamodb-volume-init": ("dynamodb-data", "/home/dynamodblocal/data"),
    "redis": ("redis-data", "/data"), "smtp4dev": ("smtp4dev-data", "/smtp4dev"),
    "otlp-seq": ("seq-data", "/data"), "rabbitmq": ("rabbitmq-data", "/var/lib/rabbitmq"),
    "azurite": ("azurite-data", "/data"),
}


def rendered(short=False):
    services = {}
    for name, ports in PORTS.items():
        mounts = []
        if name in MOUNTS:
            source, target = MOUNTS[name]
            mounts.append(f"{source}:{target}:z" if short else {"type": "volume", "source": source, "target": target})
        mounts.append("C:\\developer\\private:/shared:ro,z" if short else {"type": "bind", "source": "C:\\developer\\private", "target": "/shared"})
        services[name] = {
            "image": "official/" + name + ":latest", "container_name": "Shared" + name,
            "networks": {"shared-network": {}}, "restart": "unless-stopped",
            "depends_on": ["shared-dependency"], "volumes": mounts,
            "environment": {},
            "healthcheck": {"test": ["CMD", "rabbitmq-diagnostics", "-q", "ping"]},
            "ports": [f"127.0.0.1:12345:{port}/tcp" if short else {"target": port, "published": "12345", "host_ip": "127.0.0.1"} for port in ports],
        }
    services["mssql"]["build"] = {"context": "shared-mssql"}
    services["aws-emulator"]["build"] = {"context": "shared-aws"}
    services["aws-init"]["build"] = {"context": "shared-aws"}
    services["function-app"] = {"image": "sample-application", "container_name": "SharedSample"}
    return {"services": services,
            "volumes": {source: {"name": "dev_common_shared_" + source, "external": True} for source, _ in MOUNTS.values()},
            "networks": {"shared-network": {"external": True}}, "x-podman": {"in_pod": False}}


def definition(short=False, include_cosmos=False):
    resources = host.select_resources("csharp", include_cosmos=include_cosmos)
    return host.isolated_definition(rendered(short), PROJECT,
                                    {"mssql": "built-mssql", "aws-emulator": "built-moto"}, resources)


class IsolationTests(unittest.TestCase):
    def test_rabbitmq_readiness_probes_use_broker_user_on_fresh_volumes(self):
        fixture = definition()
        self.assertEqual(fixture["services"]["rabbitmq"]["healthcheck"]["test"],
                         ["CMD", "gosu", "rabbitmq", "rabbitmq-diagnostics", "-q", "check_port_listener", "5672"])
        with patch.object(host, "run", side_effect=[json.dumps({"Running": True}), ""]) as engine, \
             contextlib.redirect_stdout(io.StringIO()):
            host.await_resource("docker", "isolated-rabbitmq", "rabbitmq", {}, timeout=5)
        self.assertEqual(engine.call_args.args,
                         ("docker", "exec", "--user", "rabbitmq", "isolated-rabbitmq", "rabbitmq-diagnostics", "-q", "check_port_listener", "5672"))

    def test_mssql_readiness_timeout_reports_only_finite_diagnostics(self):
        secret = 'must-not-appear-in-readiness-output'
        state = {'Running': True, 'Restarting': False, 'OOMKilled': False, 'ExitCode': 0,
                 'Health': {'Status': 'unhealthy', 'Log': [{'Output': secret}]}, 'Error': secret}
        for completed, category in ((subprocess.CompletedProcess([], 1, secret, 'Login failed for user ' + secret), 'login_failed'),
                                    (subprocess.CompletedProcess([], 1, secret, 'Login timeout expired: ' + secret), 'login_timeout'),
                                    (subprocess.CompletedProcess([], 127, secret, secret), 'tool_missing'),
                                    (subprocess.CompletedProcess([], 2, secret, secret), 'unclassified'),
                                    (subprocess.TimeoutExpired(['docker'], 10, output=secret, stderr=secret), 'process_timeout')):
            with self.subTest(category=category):
                output = io.StringIO()
                options = {'side_effect': completed} if isinstance(completed, Exception) else {'return_value': completed}
                with patch.object(host, 'run', return_value=json.dumps(state)), \
                     patch.object(host.subprocess, 'run', **options) as probe, \
                     patch.object(host.time, 'monotonic', side_effect=[0, 0, 2]), \
                     patch.object(host.time, 'sleep'), contextlib.redirect_stdout(output), \
                     self.assertRaises(AssertionError) as failure:
                    host.await_resource('docker', 'isolated-mssql', 'mssql', {}, timeout=1)
                message = str(failure.exception)
                self.assertIn('probe=' + category, message)
                self.assertIn('running=True, restarting=False, oom=False, exit=0, health=unhealthy', message)
                self.assertIn('sqlcmd_exit=' + ('None' if category == 'process_timeout' else str(completed.returncode)), message)
                self.assertNotIn(secret, message + output.getvalue())
                arguments = probe.call_args.args[0]
                self.assertEqual(arguments[:5], ['docker', 'exec', 'isolated-mssql', '/bin/bash', '-c'])
                self.assertIn('SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"', arguments[5])
                self.assertNotIn('-P ', arguments[5])
                self.assertTrue(probe.call_args.kwargs['capture_output'])

    def test_mssql_success_and_diagnostic_categories_never_forward_provider_output(self):
        secret = 'must-not-appear-in-output'
        output = io.StringIO()
        with patch.object(host, 'run', return_value=json.dumps({'Running': True})), \
             patch.object(host.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, secret, secret)), \
             contextlib.redirect_stdout(output):
            host.await_resource('docker', 'isolated-mssql', 'mssql', {}, timeout=5)
        self.assertNotIn(secret, output.getvalue())
        for text, expected in (('certificate verify failed ' + secret, 'certificate_validation'),
                               ('Connection refused ' + secret, 'connection_refused'),
                               ('Query timeout expired ' + secret, 'query_timeout'),
                               ('driver could not be loaded ' + secret, 'driver_missing')):
            self.assertEqual(host.sqlcmd_failure_category(1, '', text), expected)
        unsafe = {'Running': True, 'Health': {'Status': secret}, 'ExitCode': secret, 'Error': secret}
        self.assertNotIn(secret, host.readiness_state_summary(unsafe))

    def test_dynamodb_readiness_requires_a_valid_list_tables_response(self):
        class Response:
            def __init__(self, payload, status=200):
                self.status, self.payload = status, payload

            def __enter__(self):
                return self

            def __exit__(self, *arguments):
                return False

            def read(self, limit):
                return self.payload[:limit]

        replies = [Response(b'{"TableNames":[]}', status=503), Response(b'not JSON'), Response(b'{"TableNames":"not a list"}'),
                   Response(b'{"TableNames":[]}')]
        opener = unittest.mock.Mock()
        opener.open.side_effect = replies
        with patch.object(host, 'run', return_value=json.dumps({'Running': True})), \
             patch.object(host.urllib.request, 'build_opener', return_value=opener), \
             patch.object(host.socket, 'create_connection') as socket_probe, \
             patch.object(host.time, 'sleep') as retry, contextlib.redirect_stdout(io.StringIO()):
            host.await_resource('docker', 'isolated-dynamodb', 'dynamodb', {('dynamodb', 8000): 23456}, timeout=5)
        socket_probe.assert_not_called()
        self.assertEqual(opener.open.call_count, 4)
        self.assertEqual(retry.call_count, 3)
        for call in opener.open.call_args_list:
            request = call.args[0]
            self.assertEqual(request.full_url, 'http://127.0.0.1:23456/')
            self.assertEqual(request.get_method(), 'POST')
            self.assertEqual(json.loads(request.data), {'Limit': 1})
            self.assertEqual(request.get_header('X-amz-target'), 'DynamoDB_20120810.ListTables')
            self.assertEqual(request.get_header('Content-type'), 'application/x-amz-json-1.0')
            self.assertRegex(request.get_header('Authorization'),
                r'^AWS4-HMAC-SHA256 Credential=test/[0-9]{8}/us-east-1/dynamodb/aws4_request, SignedHeaders=content-type;host;x-amz-date;x-amz-target, Signature=[a-f0-9]{64}$')

    def test_postgres_readiness_waits_for_tcp_after_socket_only_initialization(self):
        # Official initialization server: docker-library/postgres/docker-entrypoint.sh,
        # docker_temp_server_start uses listen_addresses='' until setup completes.
        probes = []

        def fake_run(runtime, *arguments, timeout=120):
            if arguments[0] == 'inspect':
                return json.dumps({'Running': True})
            self.assertEqual(arguments[:4], ('exec', 'isolated-postgres', '/bin/sh', '-c'))
            command = shlex.split(arguments[4])
            probes.append(command)
            if '-h' not in command:
                return 'Unix socket is already accepting connections'
            if len(probes) == 1:
                raise RuntimeError('The temporary initialization server does not listen on TCP.')
            return 'TCP is accepting connections'

        with patch.object(host, 'run', side_effect=fake_run), patch.object(host.time, 'sleep') as retry, \
             contextlib.redirect_stdout(io.StringIO()):
            host.await_resource('docker', 'isolated-postgres', 'postgres', {}, timeout=5)

        self.assertEqual(len(probes), 2)
        retry.assert_called_once_with(1)
        for command in probes:
            self.assertEqual(command[command.index('-h') + 1], '127.0.0.1')
            self.assertEqual(command[command.index('-p') + 1], '5432')
            self.assertEqual(command[command.index('-U') + 1], '$POSTGRES_USER')
            self.assertEqual(command[command.index('-d') + 1], '$POSTGRES_DB')

    def test_docker_and_native_podman_definitions_replace_all_shared_state(self):
        for short in (False, True):
            with self.subTest(short=short):
                source = rendered(short)
                original = copy.deepcopy(source)
                actual = host.isolated_definition(source, PROJECT,
                    {"mssql": "built-mssql", "aws-emulator": "built-moto"}, host.select_resources("csharp"))
                self.assertEqual(source, original)
                self.assertNotIn("function-app", actual["services"])
                self.assertEqual(actual["networks"], {"default": {"name": PROJECT + "-network"}})
                for name, service in actual["services"].items():
                    self.assertEqual(service["container_name"], PROJECT + "-" + name)
                    self.assertEqual(service["networks"], {"default": {"aliases": [name]}})
                    self.assertEqual(service["restart"], "no")
                    self.assertNotIn("depends_on", service)
                    self.assertNotIn("build", service)
                    self.assertTrue(all(port.startswith("127.0.0.1::") for port in service["ports"]))
                    for mount in service["volumes"]:
                        if mount["type"] == "bind":
                            self.assertEqual(name, "service-bus")
                            self.assertTrue(mount["read_only"])
                            self.assertEqual(Path(mount["source"]), host.ROOT / "containers/service-bus/Config.json")
                        else:
                            self.assertEqual(actual["volumes"][mount["source"]], {"name": PROJECT + "-" + mount["source"]})
                self.assertEqual(actual["services"]["aws-init"]["image"], "built-moto")
                self.assertEqual(actual["services"]["wiremock"]["volumes"], [])
                self.assertEqual(actual["services"]["wiremock"]["ports"], ["127.0.0.1::8080"])

    def test_fresh_credentials_and_seq_storage_key_are_valid(self):
        first, second = definition(), definition()
        sql = first["services"]["mssql"]["environment"]["MSSQL_SA_PASSWORD"]
        self.assertEqual(sql, first["services"]["service-bus"]["environment"]["MSSQL_SA_PASSWORD"])
        self.assertNotEqual(sql, second["services"]["mssql"]["environment"]["MSSQL_SA_PASSWORD"])
        self.assertNotEqual(sql, first["services"]["postgres"]["environment"]["POSTGRES_PASSWORD"])
        self.assertNotEqual(sql, first["services"]["rabbitmq"]["environment"]["RABBITMQ_DEFAULT_PASS"])
        self.assertEqual(len(base64.b64decode(first["services"]["otlp-seq"]["environment"]["SEQ_STORAGE_SECRETKEY"], validate=True)), 32)

    def test_shared_project_names_and_unsafe_ports_are_rejected(self):
        with self.assertRaises(ValueError):
            host.isolated_definition(rendered(), "dev_common_shared", {}, ("redis",))
        for value in ("65536", "0", "123/udp"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                host.target_ports({"ports": [value]})
        self.assertEqual(host.target_ports({"ports": ["127.0.0.1:5672:5672", {"target": 5672}]}), (5672,))

    def test_host_settings_use_actual_bound_ports_and_no_developer_defaults(self):
        fixture = definition(include_cosmos=True)
        ports = {(name, target): 20000 + index for index, (name, target) in enumerate(
            (name, target) for name, service in fixture["services"].items() for target in host.target_ports(service))}
        values = host.host_settings(fixture, ports, DEFAULTS)
        self.assertIn(f"127.0.0.1,{ports[('mssql', 1433)]}", values["ConnectionStrings__SqlServer"])
        self.assertIn(f"Port={ports[('postgres', 5432)]}", values["ConnectionStrings__Postgres"])
        self.assertIn(f"sb://127.0.0.1:{ports[('service-bus', 5672)]}", values["ServiceBusConnection"])
        for service, port, key in (("aws-emulator", 5000, "TOOLBOX_AWS_ENDPOINT"), ("cosmosdb", 8081, "Resources__Cosmos__Endpoint"), ("dynamodb", 8000, "Resources__DynamoDb__Endpoint")):
            self.assertTrue(values[key].endswith(":" + str(ports[(service, port)])))
        for target in (10000, 10001, 10002):
            self.assertIn(f"127.0.0.1:{ports[('azurite', target)]}/devstoreaccount1", values["Resources__AzureStorage__ConnectionString"])
        self.assertEqual(values["Resources__Cosmos__AllowUntrustedLocalCertificate"], "true")

    def test_canonical_public_defaults_are_resolved_strings_without_environment_secrets(self):
        with patch.dict(host.os.environ, {name: "developer-secret" for name in DEFAULTS}):
            values = host.public_defaults()
        self.assertEqual(set(values), set(DEFAULTS))
        self.assertTrue(all(isinstance(value, str) for value in values.values()))
        self.assertNotIn("developer-secret", json.dumps(values))
        for short in (False, True):
            with self.subTest(short=short):
                fixture = definition(short, include_cosmos=True)
                ports = {(name, target): 20000 + index for index, (name, target) in enumerate(
                    (name, target) for name, service in fixture["services"].items() for target in host.target_ports(service))}
                settings = host.host_settings(fixture, ports, values)
                self.assertIn(f"BlobEndpoint=http://127.0.0.1:{ports[('azurite', 10000)]}/devstoreaccount1", settings["Resources__AzureStorage__ConnectionString"])
                self.assertIn(f"Endpoint=sb://127.0.0.1:{ports[('service-bus', 5672)]}", settings["ServiceBusConnection"])


class HostRunnerTests(unittest.TestCase):
    def test_csharp_failure_diagnostics_accept_only_bounded_exception_class_names(self):
        for error in ('AmazonDynamoDBException', 'TimeoutException', 'Exception',
                      'must-not-appear/SecretException', 'SecretException\ncredential',
                      'A' * 128 + 'Exception', {'message': 'credential'}, None):
            with self.subTest(error=error):
                report = {'checks': [{'resource': 'dynamodb', 'status': 'failed', 'error': error}]}
                completed = subprocess.CompletedProcess([], 1, json.dumps(report), 'private-credential')
                diagnostics = {}
                self.assertEqual(host.report_statuses('csharp', completed, ('dynamodb',), diagnostics=diagnostics),
                                 {'dynamodb': 'failed'})
                expected = {'dynamodb': error} if error in ('AmazonDynamoDBException', 'TimeoutException', 'Exception') else {}
                self.assertEqual(diagnostics, expected)

    def test_csharp_failure_prints_only_exception_class_and_always_cleans_up(self):
        secret = 'must-not-appear-in-output'
        report = {'checks': [{'resource': 'dynamodb', 'status': 'failed',
                             'error': 'AmazonDynamoDBException', 'message': secret}]}
        completed = subprocess.CompletedProcess([], 1, json.dumps(report), secret)
        output = io.StringIO()
        with tempfile.TemporaryDirectory() as temporary:
            assembly = Path(temporary) / 'fixture.dll'
            assembly.touch()
            with patch.object(host, 'shared_definition', return_value=rendered()), patch.object(host, 'run') as engine, \
                 patch.object(host, 'bound_port', return_value=23456), patch.object(host, 'await_resource'), \
                 patch.object(host, 'public_defaults', return_value=DEFAULTS), patch.object(host.subprocess, 'run', return_value=completed), \
                 contextlib.redirect_stdout(output), self.assertRaises(AssertionError):
                host.verify('docker', 'csharp', ['dynamodb'], assembly=assembly)
        self.assertNotIn(secret, output.getvalue())
        self.assertIn('csharp: dynamodb failed (AmazonDynamoDBException)', output.getvalue())
        self.assertEqual(engine.call_args.args[-2:], ('down', '--volumes'))

    def test_commands_require_only_the_selected_language_sdk(self):
        expectations = {"csharp": "dotnet", "java": "java", "node": "node", "python": "chosen-python"}
        for language, executable in expectations.items():
            with self.subTest(language=language):
                commands = host.host_commands(language, ("s3", "storage-queue"), Path("private/settings.json"),
                    assembly=Path("chosen.dll"), java_jar=Path("chosen.jar"), python_executable="chosen-python")
                self.assertTrue(all(arguments[0] == executable for arguments, _ in commands))
                flattened = [resource for _, resources in commands for resource in resources]
                self.assertEqual(flattened, ["s3", "storage-queue"])
                if language == "java":
                    self.assertEqual(commands[1][0][-1], "storagequeue")
                    self.assertTrue(Path(commands[0][0][2]).is_absolute())
                if language == "csharp":
                    self.assertTrue(Path(commands[0][0][1]).is_absolute())
                if language in {"node", "python"}:
                    self.assertIn("--settings", commands[0][0])

    def test_default_cli_batches_release_sql_before_smaller_resources(self):
        with patch.object(host, "verify") as verify:
            self.assertEqual(host.main(["--runtime", "docker", "--language", "csharp"]), 0)
        batches = [call.args[2] for call in verify.call_args_list]
        self.assertEqual(len(batches), 3)
        self.assertEqual(set(batches[0]), {"sqlserver", "postgres", "servicebus", "servicebus-topic"})
        self.assertIn("s3", batches[1])
        self.assertIn("rabbitmq", batches[1])
        self.assertIn("wiremock", batches[1])
        self.assertNotIn("sqlserver", batches[1])
        self.assertIn("seq", batches[2])
        self.assertEqual(set(sum(batches, ())), set(host.select_resources("csharp")))
        fixture = definition()
        self.assertEqual(fixture["services"]["mssql"]["environment"]["MSSQL_MEMORY_LIMIT_MB"], "2048")
        self.assertEqual(fixture["services"]["otlp-seq"]["mem_limit"], "1g")
        self.assertEqual(fixture["services"]["dynamodb"]["environment"]["JAVA_TOOL_OPTIONS"], "-Xmx256m")

    def test_invalid_and_unsupported_checks_fail_before_runtime_commands(self):
        for language, resources, cosmos in (("node", ["sqlserver"], False), ("python", ["s3;unsafe"], False),
                                           ("java", None, True), ("csharp", ["functions"], False)):
            with self.subTest(language=language), patch.object(host, "shared_definition") as runtime, self.assertRaises(ValueError):
                host.verify("docker", language, resources, cosmos)
            runtime.assert_not_called()
        self.assertEqual(host.select_resources("node", ["s3", "s3"]), ("s3",))
        self.assertNotIn("cosmos", host.select_resources("node"))
        self.assertIn("cosmos", host.select_resources("node", include_cosmos=True))

    def test_child_environment_replaces_ambient_targets_and_proxies(self):
        original = {"PATH": "chosen-tools", "JAVA_HOME": "chosen-jdk", "Resources__S3__Endpoint": "https://cloud.invalid",
                    "resources__s3__endpoint": "https://another-cloud.invalid",
                    "ConnectionStrings__SqlServer": "developer-secret", "TOOLBOX_ENV_FILE": "developer.env",
                    "ServiceBusConnection": "developer-secret", "JAVA_TOOL_OPTIONS": "unsafe-proxy",
                    "HTTP_PROXY": "http://proxy.invalid", "https_proxy": "http://proxy.invalid"}
        actual = host.child_environment({"Resources__S3__Endpoint": "http://127.0.0.1:12345"}, original)
        self.assertEqual(actual["PATH"], "chosen-tools")
        self.assertEqual(actual["JAVA_HOME"], "chosen-jdk")
        self.assertEqual(actual["Resources__S3__Endpoint"], "http://127.0.0.1:12345")
        for name in ("ConnectionStrings__SqlServer", "resources__s3__endpoint", "TOOLBOX_ENV_FILE", "ServiceBusConnection", "JAVA_TOOL_OPTIONS", "HTTP_PROXY", "https_proxy"):
            self.assertNotIn(name, actual)
        self.assertEqual(original["ConnectionStrings__SqlServer"], "developer-secret")

    def test_structured_results_require_every_expected_resource_and_ignore_provider_data(self):
        valid = [{"resource": "s3", "success": True, "result": "private-output"}, {"resource": "sqs", "success": True}]
        result = subprocess.CompletedProcess([], 0, "\n".join(map(json.dumps, valid)), "private-credential")
        self.assertEqual(host.report_statuses("node", result, ("s3", "sqs")), {"s3": "passed", "sqs": "passed"})
        for documents, code in ((valid[:1], 0), (valid + valid[:1], 0), (valid, 1), ([{"resource": "s3", "success": "true"}], 0)):
            with self.subTest(documents=documents, code=code):
                completed = subprocess.CompletedProcess([], code, "\n".join(map(json.dumps, documents)), "private-credential")
                self.assertNotEqual(set(host.report_statuses("python", completed, ("s3", "sqs")).values()), {"passed"})
        csharp = {"checks": [{"resource": "s3", "status": "passed"}, {"resource": "sqs", "status": "timed_out"}]}
        self.assertEqual(host.report_statuses("csharp", subprocess.CompletedProcess([], 1, json.dumps(csharp), "secret"), ("s3", "sqs")), {"s3": "passed", "sqs": "timed_out"})

    def test_sdk_failure_output_is_withheld_and_cleanup_always_runs(self):
        secret = "must-not-appear-in-output"
        completed = subprocess.CompletedProcess([], 1, json.dumps({"resource": "redis", "success": False, "error": secret}), secret)
        output = io.StringIO()
        with patch.object(host, "shared_definition", return_value=rendered()), patch.object(host, "run") as engine, \
             patch.object(host, "bound_port", return_value=23456), patch.object(host, "await_resource"), \
             patch.object(host, "public_defaults", return_value=DEFAULTS), patch.object(host.subprocess, "run", return_value=completed), \
             contextlib.redirect_stdout(output), self.assertRaises(AssertionError):
            host.verify("docker", "node", ["redis"])
        self.assertNotIn(secret, output.getvalue())
        self.assertIn("node: redis failed", output.getvalue())
        self.assertEqual(engine.call_args.args[-2:], ("down", "--volumes"))
        arguments = engine.call_args.args
        self.assertTrue(arguments[arguments.index("-p") + 1].startswith("toolbox-host-"))

    def test_interrupt_also_cleans_up_and_value_errors_never_print_credentials(self):
        with patch.object(host, "shared_definition", return_value=rendered()), patch.object(host, "run") as engine, \
             patch.object(host, "bound_port", return_value=23456), patch.object(host, "await_resource", side_effect=KeyboardInterrupt), \
             contextlib.redirect_stdout(io.StringIO()), self.assertRaises(KeyboardInterrupt):
            host.verify("docker", "node", ["redis"])
        self.assertEqual(engine.call_args.args[-2:], ("down", "--volumes"))
        error = io.StringIO()
        with patch.object(host, "verify", side_effect=ValueError("private-credential")), contextlib.redirect_stderr(error):
            self.assertEqual(host.main(["--runtime", "docker", "--language", "node"]), 2)
        self.assertNotIn("private-credential", error.getvalue())


if __name__ == "__main__":
    unittest.main()
