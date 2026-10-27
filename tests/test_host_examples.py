#!/usr/bin/env python3
"""Validate one language's host examples against disposable infrastructure.

Local development only. Build the Azure mssql and AWS aws-emulator images and the
selected language's examples first. Release SQL resources before the smaller
SDK group to keep host and CI memory bounded. This script does not build images, install
SDKs, or change the shared toolbox stack. Cosmos is opt-in; Java uses its
separate documented JVM trust-store workflow instead.
"""

import argparse
import base64
import copy
from datetime import datetime, timezone
import hashlib
import hmac
import importlib.util
import json
import os
from pathlib import Path
import re
import secrets
import socket
import ssl
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import uuid

from container_test_support import ROOT, built_image, shared_definition, run


NODE = ROOT / "src/DeveloperToolbox.Example/DeveloperToolbox.Example.Node"
PYTHON = ROOT / "src/DeveloperToolbox.Example/DeveloperToolbox.Example.Python"
CSHARP_ASSEMBLY = ROOT / "src/DeveloperToolbox.Example/DeveloperToolbox.Example.App/bin/Release/net10.0/DeveloperToolbox.Example.App.dll"
JAVA_JAR = ROOT / "src/DeveloperToolbox.Java.Example/core/target/core-1.0-SNAPSHOT-cli.jar"
SDK_RESOURCES = ("s3", "sqs", "sns", "eventbridge", "rabbitmq", "blob",
                 "storage-queue", "table", "wiremock", "servicebus",
                 "seq", "redis", "mail", "dynamodb")
LANGUAGE_RESOURCES = {
    "csharp": SDK_RESOURCES + ("sqlserver", "postgres", "servicebus-topic"),
    "java": SDK_RESOURCES + ("sqlserver", "postgres", "servicebus-topic"),
    "node": SDK_RESOURCES,
    "python": SDK_RESOURCES,
}
RESOURCE_SERVICES = {
    "s3": "aws-emulator", "sqs": "aws-emulator", "sns": "aws-emulator",
    "eventbridge": "aws-emulator", "rabbitmq": "rabbitmq", "blob": "azurite",
    "storage-queue": "azurite", "table": "azurite", "wiremock": "wiremock",
    "servicebus": "service-bus", "servicebus-topic": "service-bus",
    "sqlserver": "mssql", "postgres": "postgres", "seq": "otlp-seq",
    "redis": "redis", "mail": "smtp4dev", "dynamodb": "dynamodb",
    "cosmos": "cosmosdb",
}
CONFIG_PREFIXES = ("Resources__", "ConnectionStrings__", "RABBITMQ_",
                   "POSTGRES_", "MSSQL_", "TOOLBOX_", "Validation__",
                   "AzureWebJobs", "FUNCTIONS_", "AzureFunctionsWebHost__")


def select_resources(language, requested=None, include_cosmos=False):
    """Reject unsupported checks before any SDK or infrastructure command runs."""
    if language not in LANGUAGE_RESOURCES:
        raise ValueError("Choose csharp, java, node, or python.")
    if language == "java" and include_cosmos:
        raise ValueError("Java Cosmos requires its separate documented JVM trust-store workflow.")
    allowed = LANGUAGE_RESOURCES[language] + (("cosmos",) if include_cosmos else ())
    selected = tuple(dict.fromkeys(requested or allowed))
    if not selected or any(name not in allowed for name in selected):
        raise ValueError("Choose resource names supported by the selected language; Cosmos requires --include-cosmos.")
    return selected


def service_names(resources):
    names = {RESOURCE_SERVICES[resource] for resource in resources}
    if "service-bus" in names:
        names.add("mssql")
    if "dynamodb" in names:
        names.add("dynamodb-volume-init")
    if "aws-emulator" in names:
        names.add("aws-init")
    return tuple(sorted(names))


def target_ports(service):
    """Normalize Docker JSON and native Podman Compose short port syntax."""
    result = []
    for value in service.get("ports", []):
        if isinstance(value, dict):
            target, protocol = value["target"], value.get("protocol", "tcp")
        else:
            raw, _, protocol = str(value).partition("/")
            target, protocol = raw.rsplit(":", 1)[-1], protocol or "tcp"
        port = int(target)
        if protocol != "tcp" or not 1 <= port <= 65535:
            raise ValueError("Only valid TCP resource ports are supported by this fixture.")
        if port not in result:
            result.append(port)
    return tuple(result)


def isolated_definition(rendered, project, images, resources):
    """Copy resources, replacing shared names, ports, credentials, and volumes."""
    if not re.fullmatch(r"toolbox-host-[a-f0-9]{32}", project):
        raise ValueError("An isolated project name is required.")
    services = {name: copy.deepcopy(rendered["services"][name]) for name in service_names(resources)}
    volumes = {}
    for name, service in services.items():
        service["container_name"] = project + "-" + name
        service["networks"] = {"default": {"aliases": [name]}}
        service["restart"] = "no"
        service["ports"] = [f"127.0.0.1::{port}" for port in target_ports(service)]
        service.pop("build", None)
        service.pop("depends_on", None)
        service.pop("links", None)
        if name in images:
            service["image"] = images[name]
            service["pull_policy"] = "never"
        elif name == "aws-init":
            service["image"] = images["aws-emulator"]
            service["pull_policy"] = "never"
        mounts = []
        for value in service.get("volumes", []):
            if isinstance(value, dict):
                if value.get("type") != "volume":
                    continue
                source, target = value["source"], value["target"]
            else:
                source, separator, remainder = str(value).partition(":")
                if not separator or source not in rendered.get("volumes", {}):
                    continue
                target = remainder.split(":", 1)[0]
            if source not in rendered.get("volumes", {}):
                raise ValueError("A fixture resource refers to an undeclared volume.")
            mounts.append({"type": "volume", "source": source, "target": target})
            volumes[source] = {"name": project + "-" + source}
        service["volumes"] = mounts
    sql_password = "Toolbox!a1-" + secrets.token_hex(24)
    if "mssql" in services:
        services["mssql"]["environment"] = {
            "ACCEPT_EULA": "Y", "MSSQL_AGENT_ENABLED": "true",
            "MSSQL_SA_PASSWORD": sql_password, "SA_PASSWORD": sql_password,
            "MSSQL_MEMORY_LIMIT_MB": "2048",
        }
        services["mssql"]["mem_limit"] = "3g"
    if "postgres" in services:
        services["postgres"]["environment"] = {
            "POSTGRES_USER": "toolbox", "POSTGRES_DB": "ProjectExample",
            "POSTGRES_PASSWORD": "Toolbox!a1-" + secrets.token_hex(24),
        }
    if "service-bus" in services:
        services["service-bus"]["environment"] = {
            "SQL_SERVER": "mssql", "MSSQL_SA_PASSWORD": sql_password,
            "ACCEPT_EULA": "Y", "SQL_WAIT_INTERVAL": "15",
        }
        services["service-bus"]["volumes"] = [{
            "type": "bind", "source": str(ROOT / "containers/service-bus/Config.json"),
            "target": "/ServiceBus_Emulator/ConfigFiles/Config.json", "read_only": True,
            "bind": {"selinux": "z"},
        }]
    if "rabbitmq" in services:
        services["rabbitmq"]["environment"] = {
            "RABBITMQ_DEFAULT_USER": "toolbox",
            "RABBITMQ_DEFAULT_PASS": "Toolbox!a1-" + secrets.token_hex(24),
        }
        # A root probe can create an unreadable Erlang cookie before the broker starts.
        services["rabbitmq"]["healthcheck"]["test"] = ["CMD", "gosu", "rabbitmq", "rabbitmq-diagnostics", "-q", "check_port_listener", "5672"]
    if "otlp-seq" in services:
        services["otlp-seq"]["environment"] = {
            "ACCEPT_EULA": "Y", "SEQ_FIRSTRUN_NOAUTHENTICATION": "True",
            "SEQ_STORAGE_SECRETKEY": base64.b64encode(secrets.token_bytes(32)).decode("ascii"),
        }
        services["otlp-seq"]["mem_limit"] = "1g"
    if "wiremock" in services:
        # No shared mappings, extensions, or developer keystore are mounted.
        services["wiremock"]["volumes"] = []
        services["wiremock"]["ports"] = ["127.0.0.1::8080"]
        services["wiremock"]["command"] = ["--port", "8080", "--disable-gzip"]
    if "dynamodb" in services:
        services["dynamodb"]["environment"] = {"JAVA_TOOL_OPTIONS": "-Xmx256m"}
    if "aws-emulator" in services:
        services["aws-emulator"]["environment"] = {"TOOLBOX_AWS_ENDPOINT": "http://localhost:5000"}
        services["aws-init"]["environment"] = {"TOOLBOX_AWS_ENDPOINT": "http://aws-emulator:5000"}
    return {"services": services, "networks": {"default": {"name": project + "-network"}},
            "volumes": volumes, "x-podman": copy.deepcopy(rendered.get("x-podman", {}))}


def bound_port(runtime, container, target):
    lines = run(runtime, "port", container, f"{target}/tcp", timeout=10).splitlines()
    matches = [line for line in lines if line.startswith("127.0.0.1:")]
    if len(matches) != 1:
        raise AssertionError("An isolated resource port was not bound uniquely to loopback.")
    value = int(matches[0].rsplit(":", 1)[1])
    if not 1 <= value <= 65535:
        raise AssertionError("The engine did not allocate an isolated resource port.")
    return value


def public_defaults():
    """Read public emulator values from the canonical template, never developer .env."""
    spec = importlib.util.spec_from_file_location("host_example_settings", ROOT / "setup/configure_examples.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    assignments = module.parse_dotenv((ROOT / "containers/.env.example").read_text(encoding="utf-8-sig"))
    selected = {"Resources__AzureStorage__ConnectionString", "ServiceBusConnection", "Resources__Cosmos__Key"}
    return module.resolve_settings(assignments, {}, selected)


def host_settings(definition, ports, defaults=None):
    """Give each host SDK fresh credentials and the fixture's actual loopback ports."""
    defaults = public_defaults() if defaults is None else defaults
    services = definition["services"]
    values = {"Resources__AWS__AccessKey": "test", "Resources__AWS__SecretKey": "test"}
    endpoint = lambda service, target, scheme="http": f"{scheme}://127.0.0.1:{ports[(service, target)]}"
    if "aws-emulator" in services:
        values.update({"TOOLBOX_AWS_ENDPOINT": endpoint("aws-emulator", 5000),
                       "Resources__S3__Endpoint": endpoint("aws-emulator", 5000),
                       "Resources__S3__Bucket": "toolbox-samples"})
    if "azurite" in services:
        fields = dict(part.split("=", 1) for part in defaults["Resources__AzureStorage__ConnectionString"].split(";") if part)
        account, key = fields["AccountName"], fields["AccountKey"]
        if not re.fullmatch(r"[A-Za-z0-9]+", account):
            raise ValueError("The public emulator account name is invalid.")
        connection = f"DefaultEndpointsProtocol=http;AccountName={account};AccountKey={key};"
        for name, target in (("Blob", 10000), ("Queue", 10001), ("Table", 10002)):
            connection += f"{name}Endpoint={endpoint('azurite', target)}/{account};"
        values.update({"Resources__AzureStorage__ConnectionString": connection,
                       "ToolboxAzureStorage": connection, "AzureWebJobsStorage": connection})
    if "service-bus" in services:
        fields = dict(part.split("=", 1) for part in defaults["ServiceBusConnection"].split(";") if part)
        fields["Endpoint"] = endpoint("service-bus", 5672, "sb")
        values["ServiceBusConnection"] = ";".join(f"{name}={value}" for name, value in fields.items()) + ";"
    if "mssql" in services:
        password = services["mssql"]["environment"]["MSSQL_SA_PASSWORD"]
        port = ports[("mssql", 1433)]
        values.update({"MSSQL_SA_PASSWORD": password,
                       "ConnectionStrings__SqlServer": f"Server=127.0.0.1,{port};Database=ToolboxSamples;User ID=sa;Password={password};Encrypt=True;TrustServerCertificate=True",
                       "JAVA_SQLSERVER_HOST": "127.0.0.1", "JAVA_SQLSERVER_PORT": str(port), "JAVA_SQLSERVER_USER": "sa"})
    if "postgres" in services:
        environment = services["postgres"]["environment"]
        port = ports[("postgres", 5432)]
        values.update(environment | {"JAVA_POSTGRES_HOST": "127.0.0.1", "JAVA_POSTGRES_PORT": str(port),
            "ConnectionStrings__Postgres": f"Host=127.0.0.1;Port={port};Database=ToolboxSamples;Username={environment['POSTGRES_USER']};Password={environment['POSTGRES_PASSWORD']}"})
    if "rabbitmq" in services:
        values.update(services["rabbitmq"]["environment"] | {
            "RABBITMQ_HOST": "127.0.0.1", "RABBITMQ_PORT": str(ports[("rabbitmq", 5672)])})
    for service, target, name in (("wiremock", 8080, "WireMock"), ("otlp-seq", 80, "Seq"), ("dynamodb", 8000, "DynamoDb")):
        if service in services:
            values[f"Resources__{name}__Endpoint"] = endpoint(service, target)
    if "otlp-seq" in services:
        values["Resources__Seq__ApiKey"] = ""
    if "redis" in services:
        values["Resources__Redis__ConnectionString"] = f"127.0.0.1:{ports[('redis', 6379)]}"
    if "smtp4dev" in services:
        values.update({"Resources__Mail__Host": "127.0.0.1", "Resources__Mail__Port": str(ports[("smtp4dev", 25)])})
    if "cosmosdb" in services:
        values.update({"Resources__Cosmos__Endpoint": endpoint("cosmosdb", 8081, "https"),
                       "Resources__Cosmos__Key": defaults["Resources__Cosmos__Key"],
                       "Resources__Cosmos__AllowUntrustedLocalCertificate": "true"})
    return values


def child_environment(settings, environment=None):
    """Override all fixture configuration; do not inherit a developer's SDK targets."""
    source = os.environ if environment is None else environment
    prefixes = tuple(prefix.lower() for prefix in CONFIG_PREFIXES)
    result = {name: value for name, value in source.items() if not name.lower().startswith(prefixes)
              and not (name.lower().startswith("java_") and name.lower() != "java_home")
              and name.lower() not in {"servicebusconnection", "toolboxazurestorage", "java_tool_options", "jdk_java_options", "_java_options", "node_extra_ca_certs"}
              and name.lower() not in {"http_proxy", "https_proxy", "all_proxy", "no_proxy"}}
    result.update(settings)
    result.update({"NO_PROXY": "127.0.0.1,localhost,::1", "AWS_ACCESS_KEY_ID": "test",
                   "AWS_SECRET_ACCESS_KEY": "test", "AWS_SESSION_TOKEN": "", "AWS_DEFAULT_REGION": "us-east-1"})
    return result


def host_commands(language, resources, settings_path, *, assembly=CSHARP_ASSEMBLY,
                  java_jar=JAVA_JAR, python_executable=sys.executable):
    """Use only the selected language's SDK; commands never contain credentials."""
    if language == "csharp":
        return [(tuple(["dotnet", str(Path(assembly).resolve()), "validate", *resources, "--json", "--timeout-seconds", "45"]), resources)]
    if language == "java":
        return [(("java", "-jar", str(Path(java_jar).resolve()), "storagequeue" if name == "storage-queue" else name), (name,)) for name in resources]
    if language == "node":
        return [(tuple(["node", str(NODE / "src/cli.mjs"), "--settings", str(settings_path), *resources]), resources)]
    if language == "python":
        if Path(python_executable).name != str(python_executable):
            python_executable = Path(python_executable).resolve()
        return [(tuple([str(python_executable), "-m", "toolbox_samples.cli", "--settings", str(settings_path), *resources]), resources)]
    raise ValueError("Choose a supported host example language.")


def report_statuses(language, completed, resources, *, diagnostics=None):
    """Inspect structured reports without forwarding SDK/provider output or payloads."""
    failed = dict.fromkeys(resources, "failed")
    try:
        if language == "java":
            return dict.fromkeys(resources, "passed" if completed.returncode == 0 else "failed")
        documents = [json.loads(line) for line in completed.stdout.splitlines() if line.startswith("{")]
        if language == "csharp":
            if len(documents) != 1:
                return failed
            checks = documents[0]["checks"]
            if [item["resource"] for item in checks] != list(resources):
                return failed
            statuses = {item["resource"]: item["status"] for item in checks}
            if any(status not in {"passed", "failed", "timed_out", "canceled"} for status in statuses.values()):
                return failed
            if completed.returncode and all(status == "passed" for status in statuses.values()):
                return failed
            if diagnostics is not None:
                for item in checks:
                    error = item.get("error")
                    # C# reports GetType().Name, never an SDK exception message.
                    if item["status"] != "passed" and isinstance(error, str) and len(error) <= 128 \
                            and re.fullmatch(r"(?:[A-Za-z_][A-Za-z0-9_]*)?Exception", error):
                        diagnostics[item["resource"]] = error
            return statuses
        if [item["resource"] for item in documents] != list(resources):
            return failed
        statuses = {item["resource"]: "passed" if item.get("success") is True else "failed" for item in documents}
        return failed if completed.returncode and all(value == "passed" for value in statuses.values()) else statuses
    except (ValueError, KeyError, TypeError):
        return failed


def dynamodb_readiness_request(port):
    """Sign a read-only JSON API probe using public local-only test credentials."""
    # AWS API contract: https://docs.aws.amazon.com/amazondynamodb/latest/APIReference/API_ListTables.html
    payload = b'{"Limit":1}'
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    date = timestamp[:8]
    scope = f"{date}/us-east-1/dynamodb/aws4_request"
    headers = {"content-type": "application/x-amz-json-1.0", "host": f"127.0.0.1:{port}",
               "x-amz-date": timestamp, "x-amz-target": "DynamoDB_20120810.ListTables"}
    signed_headers = ";".join(sorted(headers))
    canonical_headers = "".join(f"{name}:{headers[name]}\n" for name in sorted(headers))
    canonical = "\n".join(("POST", "/", "", canonical_headers, signed_headers,
                            hashlib.sha256(payload).hexdigest()))
    to_sign = "\n".join(("AWS4-HMAC-SHA256", timestamp, scope,
                         hashlib.sha256(canonical.encode("utf-8")).hexdigest()))
    key = b"AWS4test"
    for value in (date, "us-east-1", "dynamodb", "aws4_request"):
        key = hmac.new(key, value.encode("utf-8"), hashlib.sha256).digest()
    signature = hmac.new(key, to_sign.encode("utf-8"), hashlib.sha256).hexdigest()
    headers["authorization"] = f"AWS4-HMAC-SHA256 Credential=test/{scope}, SignedHeaders={signed_headers}, Signature={signature}"
    return urllib.request.Request(f"http://127.0.0.1:{port}/", data=payload, headers=headers)


def sqlcmd_failure_category(exit_code, stdout, stderr):
    """Map captured local SQL diagnostics to fixed labels; never return raw output."""
    if exit_code == 127:
        return "tool_missing"
    output = (stdout[:4096] + stderr[:4096]).casefold()
    for pattern, category in (("login failed", "login_failed"),
                              ("certificate verify failed", "certificate_validation"),
                              ("certificate chain", "certificate_validation"),
                              ("connection refused", "connection_refused"),
                              ("query timeout expired", "query_timeout"),
                              ("login timeout expired", "login_timeout"),
                              ("driver could not be loaded", "driver_missing")):
        if pattern in output:
            return category
    return "unclassified"


def readiness_state_summary(state):
    """Expose finite engine state, excluding arbitrary errors and health log payloads."""
    health = state.get("Health")
    status = health.get("Status") if isinstance(health, dict) else None
    status = status if isinstance(status, str) and status in {"starting", "healthy", "unhealthy"} else "unknown"
    exit_code = state.get("ExitCode")
    exit_code = exit_code if type(exit_code) is int and -255 <= exit_code <= 255 else None
    return (f"running={state.get('Running') is True}, restarting={state.get('Restarting') is True}, "
            f"oom={state.get('OOMKilled') is True}, exit={exit_code}, health={status}")


def await_resource(runtime, container, service, ports, timeout=240):
    """Check protocol readiness rather than assuming a published socket is ready."""
    print(f"Waiting for isolated {service} readiness.", flush=True)
    deadline = time.monotonic() + timeout
    last_http_status = None
    last_sql_exit, last_probe = None, "none"
    state = {}
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    while time.monotonic() < deadline:
        state = json.loads(run(runtime, "inspect", container, "--format", "{{json .State}}", timeout=10))
        if not state.get("Running"):
            raise AssertionError(f"The isolated {service} resource exited during startup ({readiness_state_summary(state)}).")
        try:
            if service == "mssql":
                command = 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -Q "SET NOCOUNT ON; SELECT 1" -b -l 5'
                completed = subprocess.run([runtime, "exec", container, "/bin/bash", "-c", command],
                    capture_output=True, text=True, encoding="utf-8", timeout=10)
                last_sql_exit = completed.returncode
                if completed.returncode:
                    last_probe = sqlcmd_failure_category(completed.returncode, completed.stdout, completed.stderr)
                    raise RuntimeError("The isolated SQL query has not succeeded.")
            elif service == "postgres":
                run(runtime, "exec", container, "/bin/sh", "-c", 'pg_isready -h 127.0.0.1 -p 5432 -U "$POSTGRES_USER" -d "$POSTGRES_DB" >/dev/null 2>&1', timeout=10)
            elif service == "aws-emulator":
                run(runtime, "exec", container, "python", "/opt/toolbox/aws/health.py", timeout=15)
            elif service == "rabbitmq":
                # Erlang ping can pass before AMQP starts accepting SDK connections.
                run(runtime, "exec", "--user", "rabbitmq", container, "rabbitmq-diagnostics", "-q", "check_port_listener", "5672", timeout=15)
            elif service == "redis":
                if run(runtime, "exec", container, "redis-cli", "ping", timeout=10) != "PONG":
                    raise RuntimeError("Redis is not ready.")
            elif service == "dynamodb":
                request = dynamodb_readiness_request(ports[(service, 8000)])
                with opener.open(request, timeout=5) as response:
                    tables = json.loads(response.read(4096))
                    if response.status != 200 or not isinstance(tables, dict) \
                            or not isinstance(tables.get("TableNames"), list) \
                            or any(not isinstance(name, str) for name in tables["TableNames"]):
                        raise RuntimeError("The isolated DynamoDB JSON API is not ready.")
            elif service in {"service-bus", "wiremock", "otlp-seq", "cosmosdb"}:
                target, path = {"service-bus": (5300, "/health"), "wiremock": (8080, "/__admin/mappings"),
                                "otlp-seq": (80, "/health"), "cosmosdb": (8081, "/")}[service]
                scheme = "https" if service == "cosmosdb" else "http"
                url = f"{scheme}://127.0.0.1:{ports[(service, target)]}{path}"
                current = opener
                if service == "cosmosdb":
                    # Explicit optional local emulator probe, never a global TLS override.
                    current = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=ssl._create_unverified_context()))
                with current.open(url, timeout=5) as response:
                    if response.status != 200:
                        raise RuntimeError("The local HTTP resource is not ready.")
            else:
                targets = (10000, 10001, 10002) if service == "azurite" else ((25,) if service == "smtp4dev" else (8000,))
                for target in targets:
                    with socket.create_connection(("127.0.0.1", ports[(service, target)]), timeout=3) as connection:
                        if service == "smtp4dev" and not connection.recv(512).startswith(b"220"):
                            raise RuntimeError("The isolated SMTP listener is not ready.")
            return
        except (OSError, RuntimeError, ValueError, subprocess.TimeoutExpired) as error:
            if service == "mssql" and isinstance(error, subprocess.TimeoutExpired):
                last_sql_exit, last_probe = None, "process_timeout"
            elif service == "mssql" and isinstance(error, OSError):
                last_sql_exit, last_probe = None, "engine_command_error"
            if isinstance(error, urllib.error.HTTPError):
                last_http_status = int(error.code)
            time.sleep(1)
    raise AssertionError(f"The isolated {service} resource did not become ready within its startup budget "
                         f"(http={last_http_status}, sqlcmd_exit={last_sql_exit}, probe={last_probe}; {readiness_state_summary(state)}).")


def verify(runtime, language, requested=None, include_cosmos=False, *, assembly=CSHARP_ASSEMBLY,
           java_jar=JAVA_JAR, python_executable=sys.executable):
    resources = select_resources(language, requested, include_cosmos)
    if language == "csharp" and not Path(assembly).is_file():
        raise ValueError("Build the C# Release host example first.")
    if language == "java" and not Path(java_jar).is_file():
        raise ValueError("Package the Java host CLI first.")
    rendered = shared_definition(runtime)
    names = service_names(resources)
    images = {name: built_image(runtime, rendered["services"][name], name, None)
              for name in ("mssql", "aws-emulator") if name in names}
    project = "toolbox-host-" + uuid.uuid4().hex
    definition = isolated_definition(rendered, project, images, resources)
    with tempfile.TemporaryDirectory(prefix=project + "-") as temporary:
        directory = Path(temporary)
        path = directory / "compose.json"
        with path.open("x", encoding="utf-8") as output:
            json.dump(definition, output)
        os.chmod(path, 0o600)
        compose = ("compose", "-f", str(path), "-p", project)
        try:
            print(f"Starting disposable infrastructure for the {language} host examples.", flush=True)
            if "dynamodb-volume-init" in names:
                run(runtime, *compose, "run", "--rm", "dynamodb-volume-init", timeout=120)
            initial = tuple(name for name in names if name not in {"aws-init", "dynamodb-volume-init", "service-bus"})
            run(runtime, *compose, "up", "-d", *initial, timeout=240)
            ports = {(name, target): bound_port(runtime, project + "-" + name, target)
                     for name in initial for target in target_ports(definition["services"][name])}
            for name in initial:
                await_resource(runtime, project + "-" + name, name, ports)
            if "aws-init" in names:
                run(runtime, *compose, "run", "--rm", "aws-init", timeout=60)
            if "service-bus" in names:
                run(runtime, *compose, "up", "-d", "service-bus", timeout=120)
                for target in target_ports(definition["services"]["service-bus"]):
                    ports[("service-bus", target)] = bound_port(runtime, project + "-service-bus", target)
                await_resource(runtime, project + "-service-bus", "service-bus", ports)
            settings = host_settings(definition, ports)
            settings_path = directory / "examples.settings.json"
            with settings_path.open("x", encoding="utf-8") as output:
                json.dump(settings, output)
            os.chmod(settings_path, 0o600)
            environment = child_environment(settings)
            # An isolated cwd keeps unrelated appsettings/dotenv files out of host startup.
            environment["TOOLBOX_ENV_FILE"] = ""
            cwd = PYTHON if language == "python" else directory
            statuses = {}
            diagnostics = {}
            commands = host_commands(language, resources, settings_path, assembly=assembly,
                                     java_jar=java_jar, python_executable=python_executable)
            for arguments, checked in commands:
                try:
                    completed = subprocess.run(arguments, cwd=cwd, env=environment,
                        capture_output=True, text=True, encoding="utf-8", timeout=45 * len(checked) + 30)
                    statuses.update(report_statuses(language, completed, checked, diagnostics=diagnostics))
                except subprocess.TimeoutExpired:
                    statuses.update(dict.fromkeys(checked, "timed_out"))
            for resource in resources:
                detail = f" ({diagnostics[resource]})" if resource in diagnostics else ""
                print(f"{language}: {resource} {statuses[resource]}{detail}", flush=True)
            if any(value != "passed" for value in statuses.values()):
                raise AssertionError("One or more host resource examples failed; provider output was withheld.")
            print(f"{language}: all {len(resources)} host resource examples passed.", flush=True)
        finally:
            failed = sys.exc_info()[0] is not None
            try:
                run(runtime, *compose, "down", "--volumes", timeout=90)
            except (RuntimeError, subprocess.TimeoutExpired):
                if not failed:
                    raise
                print("Isolated infrastructure cleanup also failed.", file=sys.stderr)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", choices=("docker", "podman"), required=True)
    parser.add_argument("--language", choices=tuple(LANGUAGE_RESOURCES), required=True)
    parser.add_argument("--resources", nargs="+")
    parser.add_argument("--include-cosmos", action="store_true", help="Opt in to local emulator TLS for C#, Node, or Python; Java uses its documented manual workflow.")
    parser.add_argument("--csharp-assembly", type=Path, default=CSHARP_ASSEMBLY)
    parser.add_argument("--java-jar", type=Path, default=JAVA_JAR)
    parser.add_argument("--python-executable", default=sys.executable)
    arguments = parser.parse_args(argv)
    try:
        resources = select_resources(arguments.language, arguments.resources, arguments.include_cosmos)
        # Keep cold SQL startup away from the JVM/broker/SDK service group on small CI runners.
        # Release its memory and data before starting the remaining resource groups.
        sql = {"sqlserver", "postgres", "servicebus", "servicebus-topic"}
        extras = {"seq", "redis", "mail", "dynamodb", "cosmos"}
        batches = (tuple(name for name in resources if name in sql),
                   tuple(name for name in resources if name not in sql | extras),
                   tuple(name for name in resources if name in extras))
        for batch in filter(None, batches):
            verify(arguments.runtime, arguments.language, batch, arguments.include_cosmos,
                   assembly=arguments.csharp_assembly, java_jar=arguments.java_jar,
                   python_executable=arguments.python_executable)
        return 0
    except KeyboardInterrupt:
        print("Interrupted; only isolated infrastructure cleanup was requested.", file=sys.stderr)
    except AssertionError as error:
        # Only this harness constructs assertion messages; SDK output stays captured.
        print(str(error), file=sys.stderr)
    except ValueError:
        print("Invalid host validation arguments or fixture configuration. Check --help and selected build prerequisites.", file=sys.stderr)
        return 2
    except Exception:
        print("Host resource validation failed. Check selected SDK/build prerequisites and isolated readiness.", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
