#!/usr/bin/env python3
"""Verify manual stop/start using an isolated copy of the shared Redis service.

Uses Docker or Podman with a Compose provider. The podman-compose provider needs
its PyYAML dependency available to this Python interpreter. Shared toolbox
containers, ports, and volumes are never touched. No cloud credentials are needed.
"""

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import time
import uuid
from pathlib import Path

from container_test_support import ROOT, shared_definition


def prepare_image(runtime, image):
    """Acquire only this fixture's image through the shared missing-only policy."""
    if runtime not in ("docker", "podman") or not re.fullmatch(r"[A-Za-z0-9._:/-]+", image):
        raise ValueError("A supported runtime and image tag are required.")
    if os.name == "nt":
        helper = str(ROOT / ".devcontainer/ensure_image.ps1").replace("'", "''")
        command = ["powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
                   "$ErrorActionPreference='Stop'; "
                   f". '{helper}'; Ensure-ToolboxImage -Runtime '{runtime}' -Image '{image}'; exit 0"]
    else:
        command = ["bash", "-c", '. "$1"; ensure_toolbox_image "$2" "$3"',
                   "toolbox-image", str(ROOT / ".devcontainer/ensure_image.sh"), runtime, image]
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", timeout=120)
    if result.returncode:
        raise RuntimeError("The lifecycle fixture image could not be prepared.")


def run(runtime, *arguments, timeout=120):
    result = subprocess.run([runtime, *arguments], capture_output=True, text=True,
                            encoding="utf-8", timeout=timeout)
    if result.returncode:
        raise RuntimeError(f"{runtime} {' '.join(arguments)} failed: {result.stderr.strip()}")
    return result.stdout.strip()


def inspect(runtime, container):
    return json.loads(run(runtime, "inspect", container))[0]


def await_running(runtime, container):
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        state = inspect(runtime, container)["State"]
        if state["Running"]:
            try:
                if run(runtime, "exec", container, "redis-cli", "ping") == "PONG":
                    return
            except RuntimeError:
                pass  # The process can be running before Redis accepts connections.
        time.sleep(1)
    raise AssertionError("The isolated Redis service did not become ready.")


def validate_restart_policies(rendered):
    """Check persistent manual stops while allowing every one-shot initialization job."""
    initialization_jobs = {"aws-init", "dynamodb-volume-init"}
    for name, service in rendered["services"].items():
        expected = "no" if name in initialization_jobs else "unless-stopped"
        if service.get("restart") != expected:
            raise AssertionError(f"Unexpected restart policy for {name}.")


def verify(runtime):
    if runtime == "podman":
        try:
            version = run(runtime, "version", "--format", "{{.Server.Version}}")
        except RuntimeError:
            version = ""
        if not re.fullmatch(r"\d+\.\d+\.\d+(?:[-+].+)?", version):
            if run(runtime, "info", "--format", "{{.Host.ServiceIsRemote}}") == "false":
                version = run(runtime, "info", "--format", "{{.Version.Version}}")
        if not re.fullmatch(r"\d+\.\d+\.\d+(?:[-+].+)?", version) or int(version.split(".")[0]) < 6:
            raise RuntimeError("Podman server 6 or later is required for manual stops to survive reboot.")
    project = "toolbox-lifecycle-" + uuid.uuid4().hex
    rendered = shared_definition(runtime)
    validate_restart_policies(rendered)
    service = rendered["services"]["redis"]
    prepare_image(runtime, service["image"])
    service["container_name"] = project
    service["ports"] = []
    # Set explicit unique names, including for providers that don't normalize them.
    volume = {"name": project + "_redis-data"}
    network = {"name": project + "_default"}
    definition = {"services": {"redis": service}, "volumes": {"redis-data": volume},
                  "networks": {"default": network}, "x-podman": rendered.get("x-podman", {})}
    with tempfile.TemporaryDirectory(prefix="toolbox-lifecycle-") as directory:
        path = Path(directory) / "compose.json"
        path.write_text(json.dumps(definition), encoding="utf-8")
        compose = ("compose", "-f", str(path), "-p", project)
        try:
            run(runtime, *compose, "up", "-d")
            await_running(runtime, project)
            run(runtime, "stop", project)
            time.sleep(2)
            if inspect(runtime, project)["State"]["Running"]:
                raise AssertionError("A manually stopped service restarted itself.")
            if runtime == "podman" and run(runtime, "ps", "-a", "--filter", "name=^" + project + "$",
                                           "--filter", "should-start-on-boot=true", "--format", "{{.Names}}"):
                raise AssertionError("Podman marked a manually stopped service for boot startup.")
            run(runtime, "start", project)
            await_running(runtime, project)
            print(f"{runtime}: default startup, retained manual stop, and explicit start passed.")
        finally:
            failed = sys.exc_info()[0] is not None
            try:
                run(runtime, *compose, "down", "--volumes")
            except RuntimeError as error:
                if not failed:
                    raise
                print(f"Isolated validation cleanup also failed: {error}", file=sys.stderr)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", choices=("docker", "podman"), required=True)
    verify(parser.parse_args().runtime)
