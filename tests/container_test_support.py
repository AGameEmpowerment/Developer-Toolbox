"""Shared infrastructure fixture helpers; validators execute on the host."""

import json
from pathlib import Path
import subprocess


ROOT = Path(__file__).resolve().parents[1]
SHARED_GROUPS = ("common", "azure", "aws")


def run(runtime, *arguments, timeout=120):
    result = subprocess.run([runtime, *arguments], capture_output=True, text=True,
                            encoding="utf-8", timeout=timeout)
    if result.returncode:
        raise RuntimeError("The infrastructure container command failed.")
    return result.stdout.strip()


def shared_definition(runtime):
    """Combine separately rendered groups for disposable validation fixtures."""
    supports_json = "--format" in run(runtime, "compose", "config", "--help")
    definition = {"services": {}, "volumes": {}, "networks": {}}
    for group in SHARED_GROUPS:
        arguments = ("compose", "--env-file", str(ROOT / "containers/.env.example"),
                     "-f", str(ROOT / f"containers/docker-compose-{group}.yml"),
                     "-p", f"dev_{group}_shared", "config")
        if supports_json:
            rendered = json.loads(run(runtime, *arguments, "--format", "json"))
        else:
            import yaml  # Supplied by the podman-compose provider.
            rendered = yaml.safe_load(run(runtime, *arguments))
        for section in ("services", "volumes", "networks"):
            definition[section].update(rendered.get(section, {}))
        definition["x-podman"] = rendered.get("x-podman", {})
    return definition


def built_image(runtime, service, name, explicit):
    project = {"mssql": "dev_azure_shared", "aws-emulator": "dev_aws_shared"}.get(name, "dev_common_shared")
    candidates = [explicit] if explicit else [service.get("image"),
        project + "-" + name, project + "_" + name]
    for candidate in filter(None, candidates):
        try:
            run(runtime, "image", "inspect", candidate, timeout=30)
            return candidate
        except RuntimeError:
            pass
    raise RuntimeError(f"Build the {project} {name} infrastructure image first.")
