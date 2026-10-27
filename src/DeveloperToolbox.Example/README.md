---
post_title: C# developer resource examples
author1: Michael Carey
post_slug: csharp-developer-resource-examples
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [dotnet, azure, aws, local-development]
ai_note: AI assisted
summary: Run C# SDK, Entity Framework, Azure Functions, and Lambda examples against the toolbox.
post_date: 2026-10-12
---


## Prepare the shared resources

These .NET 10 projects are local developer examples: a console app, C# SDK
samples, two EF Core database contexts, Azure Functions, and AWS Lambda handlers.
Build and run them independently on your computer. The shared Docker/Podman
stack supplies infrastructure and contains no sample application code.

Starting infrastructure requires Docker or Podman and the setup script's tools;
it does not require a .NET, Java, Node.js, or Python SDK. From the repository root:

```powershell
$runtime = 'podman' # Use 'docker' for Docker.
./docker_setup.ps1 -ContainerRuntime $runtime
```

For Bash, use `./docker_setup.sh --runtime podman` or `--runtime docker`.
Podman needs a reachable Linux server version 6 or later and a compatible Compose
provider. Start its configured machine first on Windows or macOS. All common
infrastructure services start by default, without profiles. Setup generates local
credentials in ignored `containers/.env` and preserves explicit values. Moto
needs no account or license token. See [AWS resource setup](../../containers/aws/README.md).

Services use `unless-stopped`. Stop and resume an individual service with
`& $runtime stop RabbitMq` and `& $runtime start RabbitMq`; an explicit rerun of
setup starts the whole infrastructure stack. Validation never changes service state.

## Build and run with .NET only

Install the .NET 10 SDK. These commands require no Java, Node.js, or Python SDK:

```powershell
$env:TOOLBOX_ENV_FILE = (Resolve-Path containers/.env).Path
dotnet restore src/DeveloperToolbox.Example/DeveloperToolbox.Example.App --source https://api.nuget.org/v3/index.json
dotnet build src/DeveloperToolbox.Example/DeveloperToolbox.Example.App --configuration Release --no-restore
dotnet run --project src/DeveloperToolbox.Example/DeveloperToolbox.Example.App --configuration Release --no-build -- --help
dotnet run --project src/DeveloperToolbox.Example/DeveloperToolbox.Example.App --configuration Release --no-build -- postgres
```

On Linux or macOS, set `export TOOLBOX_ENV_FILE="$PWD/containers/.env"`.
The CLI reads that file as data, with sibling `.env.example` supplying missing
defaults. It selects sample settings, expands variable references, and never
executes shell code. Single-quoted values and escaped dollars remain literal;
interpolated database components are quoted to preserve semicolons in passwords.
Configuration precedence is dotenv, optional `appsettings.json`, then process
environment variables. Keep credential files local and untracked.

Alternatively set `ConnectionStrings__*`, `Resources__*`, `ServiceBusConnection`
and messaging variables directly in your environment. Python 3 is required only
for the optional `python setup/configure_examples.py` convenience helper, which
writes ignored application and Functions JSON settings. Existing JSON is retained;
use `--force` deliberately to replace it after changing `.env`.

## Resource examples

Replace `postgres` in the run command with one of these names:

| Command | Behavior | Local resource |
| --- | --- | --- |
| `sqlserver` | EF database/schema creation, transactional insert and query | SQL Server, 10433 |
| `postgres` | Separate EF context, transactional insert and query | PostgreSQL, 10432 |
| `cosmos` | Create container and round trip a document | Cosmos emulator, 10081 |
| `dynamodb` | Create table and round trip an item | DynamoDB Local, 10180 |
| `redis` | Write/read an expiring key | Redis, 10120 |
| `mail` | Send a message visible in smtp4dev | SMTP, 10130; UI, 10140 |
| `seq` | Send a structured event | Seq, 10150 |
| `wiremock` | Register, call, and remove a temporary stub | WireMock, 10080 |
| `blob`, `storage-queue`, `table` | Azure Storage SDK round trips | Azurite, 10000–10002 |
| `rabbitmq` | Confirm publish, read and acknowledge work | RabbitMQ, 10190 |
| `sqs` | Publish and consume correlated work | Moto, 10195 |
| `sns` | Publish to a topic and read the subscribed queue | Moto, 10195 |
| `eventbridge` | Publish an event and read the rule's target queue | Moto, 10195 |
| `s3` | Upload, read, and delete a sample object | Moto, 10195 |
| `servicebus`, `servicebus-topic` | Send JSON to `queue.1` or `topic.1` | Service Bus, 5672 |

EF creates the named `ToolboxSamples` database and `Messages` table. The configured
local user needs database creation permission. `EnsureCreatedAsync` creates a
simple initial schema; it does not migrate an existing schema. Samples are local
development resources, not production deployment or migration templates.

## Live resource validation

`validate [resource ...] [--json] [--timeout-seconds 45]` runs real SDK operations
against infrastructure. With no names it selects all 18 resources above. Names
are case-insensitive and deduplicated in request order. Function and Lambda
handler execution is covered separately by their project tests.

```powershell
dotnet run --project src/DeveloperToolbox.Example/DeveloperToolbox.Example.App --configuration Release --no-build -- validate
dotnet run --project src/DeveloperToolbox.Example/DeveloperToolbox.Example.App --configuration Release --no-build -- validate sqlserver postgres storage-queue
dotnet run --project src/DeveloperToolbox.Example/DeveloperToolbox.Example.App --configuration Release --no-build -- validate redis s3 --json --timeout-seconds 45
```

Build Release first and use `--no-build` to keep build logs out of JSON. Reports
include check names, statuses, elapsed milliseconds and exception types, excluding
provider outputs, logs, payloads and secrets. Exit `0` means all checks passed,
`1` means a failed, timed-out or canceled check, and `2` means invalid arguments.
Missing or manually stopped services fail without being skipped or restarted.

The default 45-second per-operation timeout accepts whole numbers from 1 to 300.
Cancellation is cooperative. Cleanup has a separate budget, normally five seconds,
and is awaited before the next check; elapsed time may exceed the operation budget.

Validation creates synthetic or temporary data. Database messages, captured SMTP
mail and Seq events remain. Helpers remove their own temporary SDK objects where
possible. Mail verifies SMTP acceptance and Seq verifies ingestion, without
querying management UIs. RedisInsight and Cosmos Explorer are not separate checks.

Service Bus validation sends and receives correlated messages through
`queue.validation` and `topic.validation`/`subscription.validation`. Ordinary
Service Bus commands still send to `queue.1` and `topic.1`. After changing emulator
entity configuration, explicitly restart that emulator to load it; validation
does not restart services.

## Independent serverless examples and tests

The [C# Functions project](DeveloperToolbox.Example.Functions/README.md) exposes SDK samples
over HTTP and consumes native Service Bus and Storage queues into EF databases.
Run it independently using Azure Functions Core Tools 4 with .NET 10 support and
local settings. It uses dedicated broker entities so normal CLI checks can run
alongside it. The shared stack does not build or launch this application.

The [Lambda handler project](DeveloperToolbox.Example.Lambda/README.md) demonstrates API
Gateway HTTP input, EventBridge envelopes, and idempotent SQS writes to PostgreSQL
with partial batch retry responses. Its host unit tests directly invoke handlers;
no Lambda runtime, cloud deployment or event-source dispatcher is started by setup.

Run any .NET test project independently, or all .NET projects in this solution:

```powershell
dotnet test tests/DeveloperToolbox.Example.App.Tests --configuration Release
dotnet test tests/DeveloperToolbox.Example.Functions.Tests --configuration Release
dotnet test tests/DeveloperToolbox.Example.Lambda.Tests --configuration Release
dotnet test DeveloperToolbox.Example.slnx --configuration Release
```

Unit tests use isolated SDK substitutes and require no containers or credentials.
The SQL Server/PostgreSQL integration test is opt-in via
`TOOLBOX_RUN_DATABASE_INTEGRATION=1` and explicit local connection strings. Java,
Node.js and Python examples have their own builds and tests; none is required to
build or run these C# projects.
