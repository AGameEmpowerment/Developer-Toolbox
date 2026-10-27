#!/usr/bin/env python3
"""Validate the C# Service Bus queue/topic examples in an isolated local stack.

Local development only. Build the Azure mssql image and the Release example app
first. The test copies the Azure Service Bus/SQL definitions, uses fresh named
volumes and loopback ports, then removes only its own resources. Docker and
Podman with a Compose provider are supported; no shared toolbox data is changed.
"""

import argparse
import copy
import json
import os
import secrets
import subprocess
import sys
import tempfile
import time
import uuid
import urllib.error
import urllib.request
from pathlib import Path

from container_test_support import ROOT, built_image, shared_definition, run


DEFAULT_ASSEMBLY = ROOT / "src/DeveloperToolbox.Example/DeveloperToolbox.Example.App/bin/Release/net10.0/DeveloperToolbox.Example.App.dll"


def isolated_definition(rendered, project, sql_image):
    """Copy the local examples but replace every shared name, network, mount, and port."""
    # The canonical template deliberately contains "auto"; isolated stacks bypass setup.
    password = "Toolbox-" + secrets.token_hex(24) + "!a1"
    services = {name: copy.deepcopy(rendered["services"][name]) for name in ("mssql", "service-bus")}
    for name, service in services.items():
        service["container_name"] = project + "-" + name
        service["networks"] = {"default": {"aliases": [name]}}
        service["restart"] = "no"
        service.pop("build", None)
        service.pop("depends_on", None)
        service["ports"] = []
    sql = services["mssql"]
    sql["image"] = sql_image
    sql["pull_policy"] = "never"
    sql["volumes"] = [{"type": "volume", "source": "mssql-data", "target": "/var/opt/mssql"}]
    sql["environment"].update({"MSSQL_SA_PASSWORD": password, "SA_PASSWORD": password})
    broker = services["service-bus"]
    broker["ports"] = ["127.0.0.1:0:5672", "127.0.0.1:0:5300"]
    # Both providers can render mounts differently; use the one canonical config path explicitly.
    broker["volumes"] = [{"type": "bind", "source": str(ROOT / "containers/service-bus/Config.json"),
                           "target": "/ServiceBus_Emulator/ConfigFiles/Config.json", "read_only": True,
                           "bind": {"selinux": "z"}}]
    broker["environment"].update({"SQL_SERVER": "mssql", "MSSQL_SA_PASSWORD": password})
    return {"services": services, "networks": {"default": {"name": project + "-network"}},
            "volumes": {"mssql-data": {"name": project + "-mssql-data"}},
            "x-podman": rendered.get("x-podman", {})}


def container_running(runtime, container):
    """Fail promptly when isolated startup exits instead of waiting through the whole readiness budget."""
    state = json.loads(run(runtime, "inspect", container, "--format", "{{json .State}}", timeout=10))
    if not state.get("Running"):
        raise AssertionError("An isolated Service Bus dependency exited during startup.")


def await_sql(runtime, container, timeout=180):
    """Check real SQL readiness without putting the configured password into command arguments or logs."""
    deadline = time.monotonic() + timeout
    command = 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -Q "SET NOCOUNT ON; SELECT 1" -b -l 5 >/dev/null 2>&1'
    while time.monotonic() < deadline:
        container_running(runtime, container)
        try:
            run(runtime, "exec", container, "/bin/bash", "-c", command, timeout=10)
            return
        except (RuntimeError, subprocess.TimeoutExpired):
            time.sleep(1)
    raise AssertionError("The isolated SQL Server did not become ready within its startup budget.")


def broker_port(runtime, container, target=5672):
    """Use only dynamically allocated loopback ports, never shared broker endpoints."""
    lines = run(runtime, "port", container, f"{target}/tcp", timeout=10).splitlines()
    value = next((line for line in lines if line.startswith("127.0.0.1:")), None)
    if value is None:
        raise AssertionError("The isolated AMQP endpoint was not bound to loopback.")
    return int(value.rsplit(":", 1)[1])


def await_broker(runtime, container, health_port, timeout=120):
    """Use the documented /health endpoint; a published TCP socket alone can accept before initialization."""
    # https://learn.microsoft.com/azure/service-bus-messaging/test-locally-with-service-bus-emulator
    deadline = time.monotonic() + timeout
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    while time.monotonic() < deadline:
        container_running(runtime, container)
        try:
            with opener.open(f"http://127.0.0.1:{health_port}/health", timeout=3) as response:
                if response.status == 200:
                    return
        except (OSError, urllib.error.URLError):
            time.sleep(1)
    raise AssertionError("The isolated Service Bus emulator did not become healthy within its startup budget.")


def validate_examples(assembly, port):
    """Run the real CLI and validate its machine-readable summary without printing configuration values."""
    environment = os.environ.copy()
    environment["ServiceBusConnection"] = (f"Endpoint=sb://127.0.0.1:{port};"
        "SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;")
    command = ["dotnet", str(assembly), "validate", "servicebus", "servicebus-topic", "--json", "--timeout-seconds", "30"]
    with subprocess.Popen(command, cwd=ROOT, env=environment, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          text=True, encoding="utf-8") as process:
        try:
            output, _ = process.communicate(timeout=80)
        except BaseException:
            process.kill()
            process.communicate(timeout=10)
            raise
    try:
        summary = json.loads(output)
    except (ValueError, UnicodeError):
        raise AssertionError("The live example CLI did not return its expected JSON validation summary.") from None
    if not isinstance(summary, dict):
        raise AssertionError("The live example CLI returned an invalid JSON validation summary.")
    checks = summary.get("checks", [])
    expected = ["servicebus", "servicebus-topic"]
    if (process.returncode != 0 or summary.get("passed") != 2 or summary.get("failed") != 0
        or not isinstance(checks, list) or not all(isinstance(check, dict) for check in checks)
        or [check.get("resource") for check in checks] != expected
        or any(check.get("status") != "passed" for check in checks)):
        # Type/status labels are safe diagnostics; avoid copying arbitrary provider messages or credentials.
        statuses = []
        for name in expected:
            status = next((item.get("status") for item in checks if isinstance(item, dict) and item.get("resource") == name), "missing") if isinstance(checks, list) else "invalid-summary"
            if status not in ("passed", "failed", "canceled", "timed_out", "missing", "invalid-summary"):
                status = "invalid-summary"
            statuses.append(f"{name}:{status}")
        raise AssertionError("Live Service Bus validation failed: " + ", ".join(statuses) + ".")
    return {"passed": 2, "failed": 0, "checks": [{"resource": item["resource"], "status": "passed",
                "durationMilliseconds": item.get("durationMilliseconds")} for item in checks]}


def redact_passwords(message, passwords):
    """Provider errors and logs can contain the disposable credentials too."""
    for password in filter(None, passwords):
        message = message.replace(password, "[redacted local password]")
    return message


def verify(runtime, assembly=DEFAULT_ASSEMBLY, sql_image=None):
    """Start only the disposable copies and always attempt bounded cleanup on failure or cancellation."""
    assembly = Path(assembly).resolve()
    if not assembly.is_file():
        raise AssertionError("Build the example App in Release first, or supply --example-assembly.")
    rendered = shared_definition(runtime)
    image = built_image(runtime, rendered["services"]["mssql"], "mssql", sql_image)
    project = "toolbox-servicebus-" + uuid.uuid4().hex
    with tempfile.TemporaryDirectory(prefix=project + "-") as temporary:
        path = Path(temporary) / "compose.json"
        definition = isolated_definition(rendered, project, image)
        path.write_text(json.dumps(definition), encoding="utf-8")
        path.chmod(0o600)
        compose = ("compose", "-f", str(path), "-p", project)
        passwords = [definition["services"][name]["environment"].get(setting, "")
                   for name in ("mssql", "service-bus") for setting in ("MSSQL_SA_PASSWORD", "SA_PASSWORD")]
        try:
            print("Starting isolated SQL Server and waiting for readiness.", flush=True)
            run(runtime, *compose, "up", "-d", "mssql", timeout=180)
            await_sql(runtime, project + "-mssql", timeout=180)
            print("Starting isolated Service Bus emulator and waiting for its health endpoint.", flush=True)
            run(runtime, *compose, "up", "-d", "service-bus", timeout=120)
            port = broker_port(runtime, project + "-service-bus")
            await_broker(runtime, project + "-service-bus", broker_port(runtime, project + "-service-bus", 5300))
            summary = validate_examples(assembly, port)
            print(json.dumps(summary))
            print(f"{runtime}: isolated Service Bus queue and topic CLI validation passed.")
        except KeyboardInterrupt:
            print("Interrupted; removing only the isolated Service Bus test resources.", file=sys.stderr)
            raise
        except Exception as error:
            try:
                logs = run(runtime, *compose, "logs", "--tail", "40", timeout=20)
                print(redact_passwords(logs, passwords), file=sys.stderr)
            except (RuntimeError, subprocess.TimeoutExpired):
                print("Isolated validation logs could not be read.", file=sys.stderr)
            raise RuntimeError(redact_passwords(str(error), passwords)) from None
        finally:
            failed = sys.exc_info()[0] is not None
            try:
                run(runtime, *compose, "down", "--volumes", timeout=60)
            except (RuntimeError, subprocess.TimeoutExpired) as error:
                if not failed:
                    raise RuntimeError(redact_passwords(str(error), passwords)) from None
                print("Isolated validation cleanup also failed.", file=sys.stderr)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", required=True, choices=("docker", "podman"))
    parser.add_argument("--example-assembly", type=Path, default=DEFAULT_ASSEMBLY)
    parser.add_argument("--sql-image", help="Override the already-built Azure mssql image name.")
    arguments = parser.parse_args()
    verify(arguments.runtime, arguments.example_assembly, arguments.sql_image)
