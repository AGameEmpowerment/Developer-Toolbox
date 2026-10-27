"""Keep toolbox provisioning on the local AWS emulator with test credentials."""

import os
from urllib.parse import urlparse


def validate_endpoint(endpoint):
    """Reject remote endpoints, URL credentials, and unexpected URL components."""
    url = urlparse(endpoint)
    if (url.scheme != "http" or url.hostname not in
            ("localhost", "127.0.0.1", "::1", "aws-emulator") or
            url.port is None or not 1 <= url.port <= 65535 or url.username or url.password or
            url.path not in ("", "/") or url.query or url.fragment):
        raise ValueError("Toolbox provisioning is restricted to the local emulator.")
    return endpoint


def create_session(endpoint=None):
    """Never use a developer's installed AWS profile or its real credentials."""
    endpoint = validate_endpoint(
        endpoint or os.environ.get("TOOLBOX_AWS_ENDPOINT", "http://localhost:5000")
    )
    import boto3
    from botocore.config import Config

    session = boto3.Session(aws_access_key_id="test", aws_secret_access_key="test",
                            region_name="us-east-1")
    config = Config(proxies={}, connect_timeout=2, read_timeout=10,
                    retries={"mode": "standard", "total_max_attempts": 2},
                    s3={"addressing_style": "path"})

    class LocalSession:
        def client(self, name):
            return session.client(name, endpoint_url=endpoint, config=config)

    return LocalSession()


def error_code(error):
    """Read a service error without hiding connection or authentication failures."""
    return getattr(error, "response", {}).get("Error", {}).get("Code")
