---
post_title: C# Lambda examples
author1: Michael Carey
post_slug: csharp-lambda-examples
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [aws, lambda, dotnet, local-development]
ai_note: AI assisted
summary: Validate HTTP and event input and persist SQS messages through EF Core.
post_date: 2026-10-12
---

## Build and test on your computer

These .NET 10 class-library handlers are local developer examples. Build and test
them independently with the .NET 10 SDK; no Java, Node.js, Python, AWS account or
Lambda runtime is required for their unit tests. The shared Docker/Podman toolbox
contains infrastructure and does not package or start sample Lambda applications.

From the repository root:

```powershell
dotnet build src/DeveloperToolbox.Example/DeveloperToolbox.Example.Lambda --configuration Release
dotnet test tests/DeveloperToolbox.Example.Lambda.Tests --configuration Release
```

The tests invoke handlers directly using typed AWS event objects, fake invocation
contexts and an injected database implementation. They cover valid and invalid
HTTP/EventBridge input, SQS partial failures, duplicate IDs, cancellation and
remaining-time budgets. They do not require running infrastructure.

## Handler examples

| Handler | Input | Demonstration |
| --- | --- | --- |
| `HandleHttp` | API Gateway HTTP API v2 | JSON/base64 validation and HTTP response |
| `HandleEvent` | EventBridge envelope | Validate `detail.message` |
| `HandleSqs` | SQS batch | EF Core write to PostgreSQL with per-record retry |

See [the handler implementation](Function.cs) and
[its unit tests](../../../tests/DeveloperToolbox.Example.Lambda.Tests/FunctionTests.cs).
Example envelopes are [HTTP](events/http.json),
[EventBridge](events/eventbridge.json), and [SQS](events/sqs.json).
Each contains `{ "message": "Hello toolbox" }` in the corresponding event body
or detail, with nonempty text of at most 4096 characters. Valid HTTP input returns
status 200; missing, malformed, oversized or invalid UTF-8 input returns status 400.

`HandleSqs` uses each SQS `messageId` as the database primary key. It writes to the
local `ToolboxSamples` PostgreSQL database through `IDatabaseSamples`. Repeated
or concurrent delivery preserves the first committed text and creates no extra
row. A failed record appears in `batchItemFailures`; successful records are absent
from that response. Payloads, provider exception messages and credentials are
not logged. Configure `ReportBatchItemFailures` when integrating the handler with
an AWS SQS event-source mapping.

The handler caps database work at 25 seconds and respects the invocation's remaining
time, reserving one second to return partial failures. Expiration reports in-flight
and unstarted records for retry while retaining successful earlier results.
Database operations propagate cancellation and transaction failures.

For real local database calls, supply `ConnectionStrings__Postgres` from your
ignored toolbox settings. The EF example creates only `ToolboxSamples` and its
simple `Messages` schema; the local role needs database creation permission.
Use the console project's `postgres` or `validate postgres` command to exercise
that resource independently. `TOOLBOX_ENV_FILE` is a console configuration loader;
a separate handler application supplies its own configuration and invocation host.

[The AWS resources guide](../../../containers/aws/README.md) describes free Moto
APIs and local queue configuration. Sending to SQS stores a message in that
resource. Host unit tests invoke handlers explicitly; connecting a consumer or
cloud event-source mapping is an application concern. Shared setup does not deploy
handlers or start a dispatcher.

The existing `template.yaml` is an optional local SAM example for application
adaptation. This guide makes no SAM or Runtime Interface Emulator execution
requirement. These handlers and templates are not production deployment artifacts.
The [AWS C# runtime reference](https://docs.aws.amazon.com/lambda/latest/dg/lambda-csharp.html)
describes the managed .NET runtime and event handler model.
