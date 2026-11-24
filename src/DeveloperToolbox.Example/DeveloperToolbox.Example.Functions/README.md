---
post_title: Azure Functions toolbox examples
author1: Edu Developer Toolbox maintainers
post_slug: azure-functions-toolbox-examples
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [dotnet, azure-functions, local-development]
ai_note: AI assisted
summary: C# HTTP resource samples and native Azure queue consumers using EF Core.
post_date: 2026-10-12
---

These .NET 10 isolated-worker functions are local developer examples. They reuse
the C# SDK samples and demonstrate native Service Bus and Azure Storage queue
bindings with EF Core. Run this project on your computer independently of the
other language examples. Shared infrastructure contains no sample application code.

## Run with Core Tools

Install the .NET 10 SDK and Azure Functions Core Tools 4 with .NET 10 support.
Core Tools can be installed through an OS package or Windows MSI without Node.js;
see [Microsoft's local development guide](https://learn.microsoft.com/azure/azure-functions/functions-run-local).
Start toolbox infrastructure separately with `docker_setup.ps1` or
`docker_setup.sh`. This project uses host-side loopback endpoints.

Create ignored `local.settings.json` in this project. One minimal configuration
for native queue examples is below. Replace database password placeholders with
the local values generated in `containers/.env`; add `Resources__*` and messaging
settings from that file for other SDK routes.

```json
{
  "IsEncrypted": false,
  "Values": {
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AzureFunctionsWebHost__hostid": "toolbox-csharp",
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "ToolboxAzureStorage": "UseDevelopmentStorage=true",
    "ServiceBusConnection": "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
    "ConnectionStrings__SqlServer": "Server=127.0.0.1,10433;Database=ToolboxSamples;User ID=sa;Password=<local SQL password>;Encrypt=True;TrustServerCertificate=True",
    "ConnectionStrings__Postgres": "Host=localhost;Port=10432;Database=ToolboxSamples;Username=postgres;Password=<local PostgreSQL password>"
  },
  "Host": { "LocalHttpPort": 7071 }
}
```

Use ADO.NET connection-string quoting when a password contains semicolons or
quotes. Preserve your configured `POSTGRES_USER`. Keep settings files untracked.
A .NET-only developer can set these values manually or through process environment
variables. Core Tools reads its own settings; the console app's `TOOLBOX_ENV_FILE`
loader does not configure this host.

If Python 3 is installed, the optional repository helper generates all local
settings from canonical `.env` values, including properly quoted database fields.
From this project directory:

```powershell
python ../../../setup/configure_examples.py
```

Existing JSON files are retained; use `--force` deliberately after changing `.env`
when replacement is wanted. Python is a convenience for settings generation,
not a dependency of the C# application or its unit tests.

From this project directory, build and start the host:

```powershell
dotnet build --source https://api.nuget.org/v3/index.json
func start --no-build --script-root bin/Debug/net10.0
```

The native Service Bus binding uses port `5672` and requires
`UseDevelopmentEmulator=true`. Compose exposes standard AMQP alongside toolbox
port `10170`. Azurite uses `10000`, `10001` and `10002`, so the development-storage
shortcut works. Both EF connections target `ToolboxSamples`; the first database
operation creates it and its `Messages` table.

## HTTP examples

In a second terminal, send an empty POST to run a sample:

```powershell
Invoke-RestMethod -Method Post http://localhost:7071/api/samples/sqlserver
Invoke-RestMethod -Method Post http://localhost:7071/api/samples/postgres
Invoke-RestMethod -Method Post http://localhost:7071/api/samples/s3
```

Routes are `sqlserver`, `postgres`, `cosmos`, `dynamodb`, `redis`, `mail`, `seq`,
`wiremock`, `blob`, `storage-queue`, `table`, `s3`, `rabbitmq`, `sqs`, `sns`, and
`eventbridge`. Each invokes its shared SDK example; unknown routes return HTTP 404.
Core Tools skips function-key authorization locally even though the source uses
`AuthorizationLevel.Function`. Keep this development host on your local computer.

## Native queue consumers

| POST route | Published resource | Function consumer | EF target |
| --- | --- | --- | --- |
| `/api/samples/servicebus-queue` | `queue.functions` | `ServiceBusQueueToSqlServer` | SQL Server |
| `/api/samples/servicebus-topic` | `topic.functions` / `subscription.functions` | `ServiceBusTopicToPostgres` | PostgreSQL |
| `/api/samples/function-storage-queue` | `toolbox-function-work` | `StorageQueueToPostgres` | PostgreSQL |

The producer returns an `{ id, text }` envelope. The host acknowledges only after
the database write succeeds. The ID primary key handles concurrent redelivery,
preserving the original committed text. EF parameterizes inserts and queries;
database HTTP examples demonstrate an explicit transaction and return recent rows.

```powershell
Invoke-RestMethod -Method Post http://localhost:7071/api/samples/servicebus-queue
Invoke-RestMethod -Method Post http://localhost:7071/api/samples/servicebus-topic
Invoke-RestMethod -Method Post http://localhost:7071/api/samples/function-storage-queue
```

Dedicated Service Bus entities are declared in `containers/service-bus/Config.json`.
The Storage queue producer creates its queue and uses Base64 encoding for the
binding contract. Invalid envelopes and database failures propagate for retries.
Exhausted Storage retries move to `toolbox-function-work-poison`; Service Bus uses
its configured `MaxDeliveryCount` and dead-letter behavior.

`EnsureCreatedAsync` creates a disposable example schema; it does not update an
existing schema. Reset only your `ToolboxSamples` database when changing the model.
These examples are not a production schema or migration strategy.

## Independent unit tests

From the repository root:

```powershell
dotnet test tests/DeveloperToolbox.Example.Functions.Tests --configuration Release
```

Tests directly exercise HTTP routes, native consumer persistence and retry errors,
producer envelopes, local endpoint protections, cancellation and service registration.
They use dependency and Azure SDK fakes, requiring only the .NET 10 SDK and no
running host, containers, cloud account, or other language SDK.

References: [isolated C# worker](https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide),
[Service Bus emulator](https://learn.microsoft.com/azure/service-bus-messaging/test-locally-with-service-bus-emulator),
[EF create APIs](https://learn.microsoft.com/ef/core/managing-schemas/ensure-created),
and [EF transactions](https://learn.microsoft.com/ef/core/saving/transactions).
