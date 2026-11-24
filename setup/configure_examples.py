#!/usr/bin/env python3
"""Generate ignored local sample settings from the toolbox dotenv files.

This local-development helper reads data only: it never sources a shell, starts
Docker, or exports the parent's complete environment. Shell variables override
dotenv assignments as they do for Compose interpolation.
"""

import argparse
import json
import os
import re
import tempfile
from pathlib import Path
from typing import Mapping
from urllib.parse import urlsplit, urlunsplit


ROOT = Path(__file__).resolve().parents[1]
KEY = re.compile(r"[A-Za-z_][A-Za-z0-9_]*\Z")
REFERENCE = re.compile(r"\$(\$|\{([^{}]+)\}|([A-Za-z_][A-Za-z0-9_]*))")
EXPRESSION = re.compile(r"([A-Za-z_][A-Za-z0-9_]*)(?:(:-|-|:\+|\+|:\?|\?)(.*))?\Z")
EXACT_SETTINGS = frozenset({
    "ServiceBusConnection", "RABBITMQ_DEFAULT_USER", "RABBITMQ_DEFAULT_PASS",
    "RABBITMQ_HOST", "RABBITMQ_PORT",
    "TOOLBOX_AWS_ENDPOINT", "ToolboxAzureStorage", "AzureWebJobsStorage"
})


def parse_dotenv(text: str) -> dict[str, tuple[str, bool]]:
    """Parse one-line Compose-style assignments, retaining single-quote literal semantics."""
    assignments = {}
    for number, raw in enumerate(text.splitlines(), 1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("export "):
            line = line[7:].lstrip()
        name, separator, raw_value = line.partition("=")
        name = name.strip()
        if not separator or not KEY.fullmatch(name):
            raise ValueError(f"Invalid dotenv assignment at line {number}.")
        value = raw_value.strip()
        interpolate = True
        if value.startswith(("'", '"')):
            quote = value[0]
            interpolate = quote == '"'
            characters = []
            position = 1
            while position < len(value):
                character = value[position]
                if character == quote:
                    tail = value[position + 1:].strip()
                    if tail and not tail.startswith("#"):
                        raise ValueError(f"Unexpected text after quoted value at line {number}.")
                    break
                if character == "\\" and position + 1 < len(value):
                    following = value[position + 1]
                    escapes = {"n": "\n", "r": "\r", "t": "\t", "\\": "\\", '"': '"', "$": "\0"}
                    if following == quote or (interpolate and following in escapes):
                        characters.append(escapes.get(following, following) if interpolate else following)
                        position += 2
                        continue
                characters.append(character)
                position += 1
            else:
                raise ValueError(f"Unterminated quoted value at line {number}; use one-line values.")
            value = "".join(characters)
        else:
            # A # embedded in a connection string is data; whitespace followed by # starts a comment.
            value = re.split(r"\s+#", value, maxsplit=1)[0].rstrip()
        if "\0" in raw_value:
            raise ValueError(f"NUL is not allowed in dotenv values at line {number}.")
        assignments[name] = (value, interpolate)
    return assignments


def resolve_settings(assignments: Mapping[str, tuple[str, bool]], environment: Mapping[str, str],
                     selected: set[str]) -> dict[str, str]:
    """Expand selected settings only, detecting reference cycles without logging secret values."""
    resolved = {}

    def resolve(name, active=()):
        if name == "LOCALSTACK_AUTH_TOKEN":
            raise ValueError("LocalStack licensing tokens cannot be copied into example settings.")
        if name in environment:
            # Shell values are already literal values, not another shell program to evaluate.
            return environment[name]
        if name in resolved:
            return resolved[name]
        if name in active:
            raise ValueError("Cyclic dotenv variable reference; check the selected sample settings.")
        if name not in assignments:
            return ""
        raw, interpolate = assignments[name]
        result = expand(raw, active + (name,)) if interpolate else raw
        resolved[name] = result.replace("\0", "$")
        return resolved[name]

    def expand(value, active):
        def replace(match):
            if match.group(1) == "$":
                return "\0"
            expression = match.group(2) or match.group(3)
            parsed = EXPRESSION.fullmatch(expression)
            if not parsed:
                raise ValueError("Unsupported dotenv interpolation; use simple variable references.")
            name, operator, fallback = parsed.groups()
            exists = name in environment or name in assignments
            current = resolve(name, active)
            present = exists and (bool(current) if operator and operator.startswith(":") else True)
            if operator in ("-", ":-"):
                current = current if present else fallback
            elif operator in ("+", ":+"):
                current = fallback if present else ""
            if operator in ("?", ":?") and not present:
                raise ValueError(f"Required dotenv variable {name} is not set.")
            # Quote a whole interpolated connection component using the common ADO.NET grammar.
            # JSON escaping alone cannot protect a password containing semicolons or double quotes.
            if active[-1].startswith("ConnectionStrings__") and re.search(r"(?:^|;)\s*[^=;]+\s*=\s*$", value[:match.start()]) and re.match(r"\s*(?:;|$)", value[match.end():]):
                current = '"' + current.replace('"', '""') + '"'
            return current

        if "${" in REFERENCE.sub("", value):
            raise ValueError("Unsupported nested dotenv interpolation; use separate assignments.")
        return REFERENCE.sub(replace, value)

    return {name: resolve(name) for name in sorted(selected)}


def is_sample_setting(name: str) -> bool:
    """Select only settings declared for examples, excluding LocalStack credentials and unrelated secrets."""
    return name.startswith(("ConnectionStrings__", "Resources__")) or name in EXACT_SETTINGS


def nested_settings(values: Mapping[str, str]) -> dict:
    """Map environment-style double underscores to a nested .NET appsettings object."""
    result = {}
    for name, value in values.items():
        cursor = result
        parts = name.split("__")
        for part in parts[:-1]:
            child = cursor.setdefault(part, {})
            if not isinstance(child, dict):
                raise ValueError("Sample settings contain a conflicting configuration path.")
            cursor = child
        if isinstance(cursor.get(parts[-1]), dict):
            raise ValueError("Sample settings contain a conflicting configuration path.")
        cursor[parts[-1]] = value
    return result


def write_settings(path: Path, content: dict, force: bool) -> bool:
    """Preserve existing developer configuration unless replacement was explicitly requested."""
    if path.exists() and not force:
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    serialized = json.dumps(content, indent=2) + "\n"
    if not force:
        # Exclusive creation protects concurrent developer files; POSIX credentials stay owner-readable.
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w", encoding="utf-8") as output:
            output.write(serialized)
        return True
    temporary_path = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent,
                                         prefix=f".{path.name}-", delete=False) as output:
            temporary_path = Path(output.name)
            output.write(serialized)
            output.flush()
            os.fsync(output.fileno())
        # Replace the named configuration file, without following a destination symlink or truncating on failure.
        os.replace(temporary_path, path)
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)
    return True


def configure(root: Path = ROOT, force: bool = False, environment: Mapping[str, str] | None = None) -> list[tuple[Path, bool]]:
    """Generate .NET and language-neutral settings from canonical defaults and the ignored .env."""
    template_path = root / "containers" / ".env.example"
    dotenv_path = root / "containers" / ".env"
    if not dotenv_path.exists():
        raise ValueError("Create containers/.env from containers/.env.example before configuring examples.")
    template = parse_dotenv(template_path.read_text(encoding="utf-8-sig"))
    assignments = template | parse_dotenv(dotenv_path.read_text(encoding="utf-8-sig"))
    selected = {name for name in template if is_sample_setting(name)}
    effective_environment = os.environ if environment is None else environment
    values = resolve_settings(assignments, effective_environment, selected)
    app = root / "src" / "DeveloperToolbox.Example" / "DeveloperToolbox.Example.App" / "appsettings.json"
    functions = root / "src" / "DeveloperToolbox.Example" / "DeveloperToolbox.Example.Functions" / "local.settings.json"
    shared = root / "containers" / "examples.settings.json"
    shared_values = dict(values)
    storage_name = "Resources__AzureStorage__ConnectionString"
    if shared_values.get(storage_name, values.get("ToolboxAzureStorage", "")).lower() == "usedevelopmentstorage=true":
        container_name = "TOOLBOX_CONTAINER_AZURE_STORAGE_CONNECTION"
        connection = resolve_settings(assignments, effective_environment, {container_name})[container_name]
        if not connection:
            connection = resolve_settings(template, {}, {storage_name})[storage_name]
        if connection and connection.lower() != "usedevelopmentstorage=true":
            # Python's SDK does not implement the .NET development-storage shortcut.
            # Preserve the canonical emulator key; translate only known local emulator hosts.
            parts = []
            for component in connection.split(";"):
                name, separator, value = component.partition("=")
                if name in {"BlobEndpoint", "QueueEndpoint", "TableEndpoint"}:
                    endpoint = urlsplit(value)
                    if endpoint.hostname not in {"azurite", "localhost", "127.0.0.1"} or endpoint.username or endpoint.password:
                        raise ValueError("The canonical storage connection must use local emulator hosts.")
                    value = urlunsplit((endpoint.scheme, f"localhost:{endpoint.port}", endpoint.path, "", ""))
                if separator:
                    parts.append(f"{name}={value}")
            shared_values[storage_name] = ";".join(parts) + ";"
    contents = [nested_settings(values), {
        "IsEncrypted": False,
        "Values": values | {"FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
                            "AzureFunctionsWebHost__hostid": "toolbox-csharp"},
        "Host": {"LocalHttpPort": 7071}
    }, shared_values]
    outputs = list(zip((app, functions, shared), contents))
    language_projects = (
        ("java", root / "src/DeveloperToolbox.Java.Example/functions", 7072),
        ("node", root / "src/DeveloperToolbox.Example/DeveloperToolbox.Example.Node", 7073),
        ("python", root / "src/DeveloperToolbox.Example/DeveloperToolbox.Example.Python", 7074),
    )
    java_names = {name for name in template if name.startswith("JAVA_") or name in
                  {"MSSQL_SA_PASSWORD", "POSTGRES_USER", "POSTGRES_PASSWORD"}}
    java_values = resolve_settings(assignments, effective_environment, java_names)
    for language, directory, port in language_projects:
        language_values = dict(shared_values)
        if language == "java":
            language_values.update(java_values)
        language_values.update({"FUNCTIONS_WORKER_RUNTIME": language,
                                "AzureFunctionsWebHost__hostid": "toolbox-" + language})
        outputs.append((directory / "local.settings.json", {
            "IsEncrypted": False, "Values": language_values,
            "Host": {"LocalHttpPort": port},
        }))
    return [(path, write_settings(path, content, force)) for path, content in outputs]


def main(argv=None) -> int:
    """Run local configuration without printing values that may contain credentials."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--force", action="store_true", help="Replace existing ignored example settings.")
    args = parser.parse_args(argv)
    try:
        for path, written in configure(force=args.force):
            print(f"{'Created' if written else 'Preserved existing'} {path.relative_to(ROOT)}")
    except (OSError, ValueError) as error:
        # Never render the source line or underlying file contents on failure.
        print(str(error) if isinstance(error, ValueError) else "Could not read or write local example settings.")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
