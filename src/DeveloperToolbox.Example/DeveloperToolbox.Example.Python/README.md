---
post_title: Local Python resource and serverless examples
author1: Michael Carey
post_slug: local-python-resource-examples
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [python, azure, aws, local-development]
ai_note: AI assisted
summary: Validate running toolbox resources from the host with Python SDKs and optional Functions and Lambda handlers.
post_date: 2026-10-12
---

## Run the SDK examples

These examples support local development and are not production deployment
templates. Python 3.14 matches the supported
[Azure Functions versions](https://learn.microsoft.com/azure/azure-functions/functions-versions)
and [AWS Lambda runtime](https://docs.aws.amazon.com/lambda/latest/dg/lambda-runtimes.html).
Start the required shared resources first. Use an isolated host environment and
the public PyPI dependency lock:

```powershell
python setup/configure_examples.py
Set-Location src/DeveloperToolbox.Example/DeveloperToolbox.Example.Python
python -m venv .venv
./.venv/Scripts/python.exe -m pip install --index-url https://pypi.org/simple -r requirements.txt
./.venv/Scripts/python.exe -m unittest discover -s tests -v
./.venv/Scripts/python.exe -m toolbox_samples.cli rabbitmq servicebus sqs sns eventbridge s3 blob storage-queue table
```

On Linux/macOS use `.venv/bin/python` instead of `.venv/Scripts/python.exe`.
The CLI reads ignored `containers/examples.settings.json`; environment variables
override its flat settings. `--settings <path>` selects a different generated
file; `--text "Sample text"` changes the payload. Each resource produces a JSON
record; any failure returns exit code 1. With no names, all 15 SDK examples run:
`rabbitmq`, `servicebus`, `sqs`, `sns`, `eventbridge`, `s3`, `blob`, `storage-queue`,
`table`, `dynamodb`, `cosmos`, `redis`, `seq`, `wiremock`, `mail`.

The canonical values live in `containers/.env.example`, with developer overrides
in ignored `containers/.env`. The generator expands the .NET development-storage
shortcut to explicit local endpoints for Python without duplicating the public
Azurite key in source. AWS clients use explicit local endpoints and emulator
credentials, never ambient cloud credentials. Unsafe endpoints fail before an
SDK client is constructed. Network requests have finite connection/read timeouts
and retry limits; the synchronous CLI awaits each operation and cleanup.

The Blob and Queue SDKs select `2026-06-06`, listed in
[Azurite's supported API versions](https://github.com/Azure/Azurite/blob/main/src/blob/utils/constants.ts),
so a newer SDK can run against the emulator's current protocol support.

Samples compare the value written with the value read and delete their own
temporary objects. SQS/SNS/EventBridge queues, topics, and rules are disposable.
Service Bus uses `queue.python-validation`; overlapping validations can share
its receive counts. DynamoDB and Cosmos retain their small reusable table and
database/container. SMTP messages and Seq events remain in the service tools for
inspection. Cosmos accepts its untrusted local emulator certificate only when
the canonical opt-in is enabled and the endpoint passes the local guard.

## Host Functions and Lambda examples

The shared stack runs infrastructure only. Python application source runs on
your developer host, independently of its resource containers. The native
[Python v2 Functions decorators](https://learn.microsoft.com/azure/azure-functions/functions-reference-python)
register the HTTP and queue handlers in `function_app.py`.

Install [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local).
After installing the locked dependencies and generating settings, activate the
Python environment and start Functions from this example directory:

```powershell
./.venv/Scripts/Activate.ps1
func start --port 7074
```

On Linux/macOS, activate it with `source .venv/bin/activate` first. The generator
creates this project's ignored `local.settings.json` with Python worker settings,
a unique host ID, and host-side Service Bus/Azurite endpoints. Core Tools follows
its [local authorization behavior](https://learn.microsoft.com/azure/azure-functions/functions-run-local);
use its `--enableAuth` option when testing function-key authorization. The source
retains Function authorization metadata.

From another host terminal, invoke a resource sample or native queue producer:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:7074/api/samples/s3 -ContentType application/json -Body '{"text":"Hello Python"}'
$work = @{ id = [guid]::NewGuid().ToString(); language = 'python'; text = 'Queue work' } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:7074/api/work/storage-queue -ContentType application/json -Body $work
```

For an auth-enabled host, add its configured `x-functions-key` header.
`/api/work/servicebus` targets `queue.python-work`; `/api/work/storage-queue`
targets `toolbox-python-work` with native-binding base64 encoding. While the host
is running, the consumers write `receipts/python/<id>.json` to `toolbox-samples`
Blob storage. These queues are separate from the other languages' consumers.
Stop the host independently when you finish.

`lambda_handler.handler` accepts standard SQS batches with partial failures and
API Gateway v2 POST events for `/samples/<resource>` or `/work`. The same handler
logic can run directly on the host, using generated settings and explicit cleanup:

```powershell
@"
import json
from toolbox_samples.config import load_settings
from toolbox_samples.handlers import lambda_handler
from toolbox_samples.resources import ResourceSamples
samples = ResourceSamples(load_settings())
try:
    event = {
        "version": "2.0", "rawPath": "/samples/s3",
        "requestContext": {"http": {"method": "POST"}},
        "body": json.dumps({"text": "Hello Python Lambda"}),
        "isBase64Encoded": False,
    }
    print(lambda_handler(event, samples))
finally:
    samples.close()
"@ | ./.venv/Scripts/python.exe
```

SQS records need `messageId`, `eventSource: "aws:sqs"`, and a JSON `body` with
`id`, `language: "python"`, and `text`. `/api/work/sqs` only sends work to
`toolbox-python-lambda-work`; no shared dispatcher runs. Moto does not execute the
handler. An explicitly started host consumer must receive records, invoke the
handler, inspect `batchItemFailures`, and acknowledge successful work.

## Validate running resources

From this directory, run all fifteen SDK checks on the host:

```powershell
./.venv/Scripts/python.exe -m toolbox_samples.cli
```

Use `.venv/bin/python` on Linux/macOS. The CLI validates already-running resource
containers and does not change their lifecycle. Missing or stopped resources
fail. Seq/SMTP verify accepted writes; document/cache examples also verify reads
and remove their own entries. Unit tests need no container engine. Functions and
Lambda handler tests run independently of shared infrastructure images.

For disposable resource validation, build the two common infrastructure images
and run the fixture from the repository root with this project's Python
interpreter:

```powershell
$runtime = 'docker' # Or Podman 6+ with a working Compose provider.
& $runtime compose --env-file containers/.env -f containers/docker-compose-azure.yml -p dev_azure_shared build mssql
& $runtime compose --env-file containers/.env -f containers/docker-compose-aws.yml -p dev_aws_shared build aws-emulator
& ./src/DeveloperToolbox.Example/DeveloperToolbox.Example.Python/.venv/Scripts/python.exe tests/test_host_examples.py --runtime $runtime --language python
```

On Linux/macOS use the same project's `.venv/bin/python` path. The fixture runs
only the selected language's host SDK, allocates fresh credentials and loopback
ports, then removes its own private containers and volumes. Defaults cover
fourteen checks; add `--include-cosmos` for the fifteenth with explicit local
emulator TLS opt-in, or use `--resources s3 sqs` for selected checks. It does not
change the shared stack or require another language's SDK. Provider output and
credentials are withheld.

## Dependency sources and licenses

`pyproject.toml` pins the directly used libraries and `requirements.txt` locks the
complete installed tree. Installed wheel metadata declares these licenses; none
requires a paid license for these local examples. Keep dependency notices when
redistributing images, including licenses for the operating-system components.

| Dependency/source | License |
| --- | --- |
| [boto3](https://github.com/boto/boto3) 1.43.111 and its botocore SDK dependency | Apache-2.0 |
| [Azure SDK clients](https://github.com/Azure/azure-sdk-for-python) for Cosmos, Tables, Service Bus, Blob and Queue | MIT |
| [Azure Functions Python library](https://github.com/Azure/azure-functions-python-library) 2.3.0 | MIT |
| [Pika](https://github.com/pika/pika) 1.4.4 | BSD-3-Clause |
| [redis-py](https://github.com/redis/redis-py) 8.1.0 | MIT |
| [Requests](https://github.com/psf/requests) 2.34.2 | Apache-2.0 |
| [certifi](https://github.com/certifi/python-certifi) 2026.7.22 (CA certificate dependency) | MPL-2.0 |
| [Python](https://docs.python.org/3.14/license.html) 3.14 | PSF license, with bundled-component notices |

[Azure Functions Core Tools](https://github.com/Azure/azure-functions-core-tools)
and the [Functions host](https://github.com/Azure/azure-functions-host/blob/dev/LICENSE.txt)
use MIT. The optional host runtime is installed separately. Local emulation does
not require an Azure or AWS account and does not reproduce every cloud behavior.
