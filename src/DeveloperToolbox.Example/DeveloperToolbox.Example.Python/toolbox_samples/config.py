"""Read generated flat settings and validate local-only endpoints before SDK creation."""
import ipaddress
import json
import os
import uuid
from pathlib import Path
from urllib.parse import urlsplit


def load_settings(path=None, environment=None):
    if path:
        path = Path(path)
    else:
        # Lambda's shallow /var/task layout has fewer ancestors than the source checkout.
        repository = next((parent for parent in Path(__file__).resolve().parents if (parent / "containers/.env.example").is_file()), None)
        path = repository / "containers/examples.settings.json" if repository else Path("/containers/examples.settings.json")
    settings = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}
    if not isinstance(settings, dict):
        raise ValueError("Settings must be a JSON object.")
    return settings | dict(os.environ if environment is None else environment)


def local_url(value, names=(), schemes=("http", "https"), allow_path=False):
    try:
        endpoint = urlsplit(value)
        host = endpoint.hostname or ""
        endpoint.port
        try:
            loopback = ipaddress.ip_address(host).is_loopback
        except ValueError:
            loopback = host == "localhost"
        if endpoint.scheme not in schemes or not (loopback or host in names) or endpoint.username or endpoint.password or endpoint.query or endpoint.fragment or (not allow_path and endpoint.path not in ("", "/")):
            raise ValueError()
    except (ValueError, TypeError, AttributeError):
        raise ValueError("Only local toolbox emulator endpoints are allowed.") from None
    return endpoint


def connection_fields(value, allowed):
    fields = {}
    seen = set()
    for part in filter(None, value.split(";")):
        key, separator, data = part.partition("=")
        if not separator or key not in allowed or key.lower() in seen:
            raise ValueError("Ambiguous or unsupported local connection setting.")
        seen.add(key.lower())
        fields[key] = data
    return fields


def storage_connection(settings):
    value = settings.get("Resources__AzureStorage__ConnectionString") or settings.get("ToolboxAzureStorage", "")
    if not value or value.lower().startswith("usedevelopmentstorage="):
        raise ValueError("Run python setup/configure_examples.py to generate the explicit local Azure Storage connection.")
    fields = connection_fields(value, ("DefaultEndpointsProtocol", "AccountName", "AccountKey", "BlobEndpoint", "QueueEndpoint", "TableEndpoint"))
    for key in ("BlobEndpoint", "QueueEndpoint", "TableEndpoint"):
        local_url(fields.get(key), ("azurite",), allow_path=True)
    if not fields.get("AccountName") or not fields.get("AccountKey") or fields.get("SharedAccessSignature"):
        raise ValueError("An explicit local Azure Storage account and key are required.")
    return value


def service_bus_connection(settings):
    value = settings.get("ServiceBusConnection", "")
    fields = connection_fields(value, ("Endpoint", "SharedAccessKeyName", "SharedAccessKey", "UseDevelopmentEmulator"))
    local_url(fields.get("Endpoint"), ("service-bus",), schemes=("sb",))
    if fields.get("UseDevelopmentEmulator", "").lower() != "true":
        raise ValueError("Service Bus requires UseDevelopmentEmulator=true.")
    return value


def work_message(value, language="python"):
    if not isinstance(value, dict) or not isinstance(value.get("id"), str):
        raise ValueError("Work requires a UUID id, the matching language, and text of 1–1024 characters.")
    try:
        if str(uuid.UUID(value["id"])) != value["id"].lower():
            raise ValueError()
    except ValueError:
        raise ValueError("Work requires a UUID id, the matching language, and text of 1–1024 characters.") from None
    text = value.get("text")
    if value.get("language") != language or not isinstance(text, str) or not text.strip() or len(text) > 1024:
        raise ValueError("Work requires a UUID id, the matching language, and text of 1–1024 characters.")
    return {"id": value["id"], "language": language, "text": text}
