---
post_title: Local Node.js resource and serverless examples
author1: Michael Carey
post_slug: local-node-resource-examples
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [nodejs, azure, aws, local-development]
ai_note: AI assisted
summary: Validate running toolbox resources from the host with Node.js SDKs and optional Functions and Lambda handlers.
post_date: 2026-10-12
---

## Run the SDK examples

These are local developer support examples, not production deployment templates.
Node.js 24 matches the supported
[Azure Functions runtime](https://learn.microsoft.com/azure/azure-functions/functions-versions)
and [AWS Lambda runtime](https://docs.aws.amazon.com/lambda/latest/dg/lambda-nodejs.html).
The SDKs are installed from the public npm registry; this project needs no private
registry credentials.

Start the required shared resources first. From the repository root, configure
the host examples using the canonical values in
`containers/.env.example` and the developer's ignored `containers/.env`:

```powershell
python setup/configure_examples.py
Set-Location src/DeveloperToolbox.Example/DeveloperToolbox.Example.Node
npm ci
npm test
npm run validate -- rabbitmq servicebus sqs sns eventbridge s3 blob storage-queue table
```

The CLI reads ignored `containers/examples.settings.json`; environment variables
override its flat settings. `--settings <path>` selects another generated file.
`--text "Sample text"` changes the sample payload. Each resource produces a JSON
record and a failure produces exit code 1. With no resource names, it checks all
15 examples: `rabbitmq`, `servicebus`, `sqs`, `sns`, `eventbridge`, `s3`, `blob`,
`storage-queue`, `table`, `dynamodb`, `cosmos`, `redis`, `seq`, `wiremock`, `mail`.

Storage, document, cache, and mock examples write, read, compare, and remove their
own sample data. SQS, SNS, and EventBridge use disposable queues/rules/topics so
they do not consume another developer's messages. Service Bus uses the configured
`queue.node-validation`; overlapping validations can share its receive counts.
The small DynamoDB table and Cosmos database/container remain for reuse. Seq
events and SMTP messages remain available in the service tools for inspection.
AWS clients always use an explicit local endpoint, region, and emulator
credentials. Cloud endpoints and ambient credential discovery are rejected.

The Blob SDK sends the `2026-06-06` protocol version listed in
[Azurite's supported API versions](https://github.com/Azure/Azurite/blob/main/src/blob/utils/constants.ts).
This keeps the current SDK usable while Azurite catches up with newer APIs.

## Host Functions and Lambda examples

Application source runs independently on your developer host. The shared
resource stack contains no Functions hosts, Lambda runtimes, dispatchers, or
sample code. The Node.js v4
[Functions programming model](https://learn.microsoft.com/azure/azure-functions/functions-reference-node)
registers HTTP and native queue handlers under `src/functions`.

Install [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local).
After `npm ci` and generating settings from the repository root, start the host
from this Node example directory:

```powershell
func start --port 7073
```

The generator creates this project's ignored `local.settings.json` with Node
worker settings, a unique host ID, and host-side Service Bus/Azurite endpoints.
Keep the required resource containers running. Core Tools local authorization
behavior follows its [local development guide](https://learn.microsoft.com/azure/azure-functions/functions-run-local);
use its `--enableAuth` option when testing function-key authorization. The source
handlers retain Function authorization metadata.

From a second host terminal, invoke a resource sample or enqueue native work:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:7073/api/samples/s3 -ContentType application/json -Body '{"text":"Hello Node"}'
$work = @{ id = [guid]::NewGuid().ToString(); language = 'node'; text = 'Queue work' } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:7073/api/work/servicebus -ContentType application/json -Body $work
```

For an auth-enabled local host, add its configured `x-functions-key` header.
`/api/work/storage-queue` sends base64-encoded JSON for the native Azure Storage
binding. While the host is running, its consumers process `queue.node-work` and
`toolbox-node-work` and write `receipts/node/<id>.json` to `toolbox-samples` Blob
storage. Stop the host when you finish; resource containers have their own
independent lifecycle.

The Lambda handler accepts standard SQS batches with partial failure responses,
or API Gateway v2 POST events. `/samples/<resource>` calls the SDK sample; `/work`
writes a validated Node receipt to S3. Invoke the same handler factory on the
host from this directory, with generated settings and explicit client cleanup:

```powershell
@"
import { loadSettings } from './src/config.mjs';
import { createResourceSamples } from './src/resources.mjs';
import { createLambdaHandler } from './src/handlers.mjs';
const samples = createResourceSamples(await loadSettings());
try {
  const event = {
    version: '2.0', rawPath: '/samples/s3',
    requestContext: { http: { method: 'POST' } },
    body: JSON.stringify({ text: 'Hello Node Lambda' }),
    isBase64Encoded: false
  };
  console.log(await createLambdaHandler(samples)(event));
} finally { await samples.close(); }
"@ | node --input-type=module
```

`src/lambda.mjs` exposes the conventional exported `handler` entry point. SQS
records need `messageId`, `eventSource: "aws:sqs"`, and a JSON `body` containing
`id`, `language: "node"`, and `text`. `/api/work/sqs` only enqueues that work;
Moto does not invoke the handler. An explicitly started host consumer must receive
and invoke records, inspect `batchItemFailures`, and acknowledge successful work.

## Validate running resources

From this directory, run all fifteen SDK checks on your host:

```powershell
npm run validate
```

The CLI connects to the already-running shared resources. Missing or stopped
resources fail; validation does not start or stop containers. Unit tests need no
engine. Seq/SMTP verify accepted writes; document/cache examples also verify
reads and remove their own entries. Host Functions and Lambda event tests remain
independent from the shared infrastructure images.

For an isolated host check, build only the two common infrastructure images and
run the fixture from the repository root after installing this Node example's
locked dependencies:

```powershell
$runtime = 'docker' # Or Podman 6+ with a working Compose provider.
& $runtime compose --env-file containers/.env -f containers/docker-compose-azure.yml -p dev_azure_shared build mssql
& $runtime compose --env-file containers/.env -f containers/docker-compose-aws.yml -p dev_aws_shared build aws-emulator
python tests/test_host_examples.py --runtime $runtime --language node
```

The fixture runs the Node CLI on the host against fresh resource containers,
credentials, loopback ports, and private settings. It requires no other language
SDK and removes only its own resources. Defaults cover fourteen checks; add
`--include-cosmos` for the fifteenth with explicit local emulator TLS opt-in.
Use `--resources s3 sqs` to narrow a run. Provider diagnostics are withheld.

## Dependency sources and licenses

The exact package versions are in `package-lock.json`; their installed package
metadata declares the following licenses. These SDKs and tools require no paid
license for the intended local examples. Preserve their notices when distributing
an image or package; included operating-system components keep their own licenses.

| Dependency/source | License |
| --- | --- |
| [AWS SDK v3 clients](https://github.com/aws/aws-sdk-js-v3) 3.1149.0 | Apache-2.0 |
| [Azure SDK clients](https://github.com/Azure/azure-sdk-for-js) for Cosmos, Tables, Service Bus, Blob and Queue | MIT |
| [Azure Functions Node library](https://github.com/Azure/azure-functions-nodejs-library) 4.16.5 | MIT |
| [amqplib](https://github.com/amqp-node/amqplib) 2.2.0 | MIT |
| [node-redis](https://github.com/redis/node-redis) 6.3.0 | MIT |
| [Nodemailer](https://github.com/nodemailer/nodemailer/blob/master/LICENSE) 10.1.0 | MIT-0 |
| [Vitest](https://github.com/vitest-dev/vitest) 5.0.3 (tests) | MIT |
| [Node.js](https://github.com/nodejs/node/blob/main/LICENSE) 24 | MIT, with bundled-component notices |

On 2026-10-12 the newly published AWS clients `3.1150.0` could not install with
normal dependency resolution: their
[official npm manifest](https://registry.npmjs.org/@aws-sdk/client-s3/3.1150.0)
requires `@aws-sdk/credential-provider-node` `^3.972.86`, while its
[published latest version](https://registry.npmjs.org/@aws-sdk/credential-provider-node/latest)
was `3.972.85`. The lock retains installable `3.1149.0` without overrides or forced
peer resolution; update it after the required upstream package is published.

Vitest remains on `5.0.3`, matching the client: the new `5.0.4` package family was
blocked by the existing registry 48-hour maturity rule in
[the client CI run](https://github.com/AGameEmpowerment/Developer-Toolbox/actions/runs/38291742908).
This example retains the same test version rather than bypassing that control;
revisit it after the registry permits the release.

[Azure Functions Core Tools](https://github.com/Azure/azure-functions-core-tools)
and the [Functions host](https://github.com/Azure/azure-functions-host/blob/dev/LICENSE.txt)
use MIT. The optional host runtime is installed separately. Local emulation does
not require an Azure or AWS account and does not reproduce every cloud behavior.

## CLI test timing

CLI tests start independent Node processes and import the cloud SDK tree.
The runner allows 60 seconds per test for cold Windows startup; each child
process is bounded to 30 seconds. The test cases remain the same.
