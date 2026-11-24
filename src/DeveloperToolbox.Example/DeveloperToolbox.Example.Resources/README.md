---
post_title: Local C# resource SDK samples
author1: Michael Carey
post_slug: local-csharp-resource-sdk-samples
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [dotnet, azure, aws, local-development]
ai_note: AI assisted
summary: Exercise the toolbox storage, document, cache, mail, logging, and mock services with C#.
post_date: 2026-10-12
---

## Resource samples

These C# examples are local developer support resources. They demonstrate SDK
calls against the toolbox containers and are not production deployment templates.
Each operation can run independently; starting the application does not require
credentials for optional services.

Register `ResourceSamples` once in the host and use its asynchronous methods from
the console application or Azure Functions HTTP examples:

```csharp
builder.Services.AddHttpClient(ResourceSamples.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false
    });
builder.Services.AddSingleton<ResourceSamples>();

// Resolve through constructor injection in a function or application service.
var result = await resourceSamples.S3Async(cancellationToken);
```

The project targets .NET 10. Azure SDKs provide Blob, Queue, Table, and Cosmos
access; AWS SDKs provide DynamoDB and S3 access. StackExchange.Redis shares a
multiplexer, MailKit sends captured test mail, and HTTP examples show Seq CLEF
ingestion and WireMock administration. Reusable SDK clients initialize lazily.
The named HTTP handler keeps Seq keys and sample requests on their validated
local destination. Redirect responses fail the check, and these local calls
do not use a developer's external HTTP proxy.

### Operations and developer tools

| Method | Behavior | Host endpoint or inspection tool |
| --- | --- | --- |
| `CosmosAsync` | Creates database/container, writes and reads a partitioned document | HTTPS `localhost:10081`; Cosmos Explorer at `https://localhost:10181` |
| `DynamoDbAsync` | Creates a table keyed by `Id`, writes and consistently reads one item | `http://localhost:10180`; AWS CLI with explicit endpoint |
| `RedisAsync` | Writes and reads an expiring string | `localhost:10120`; RedisInsight at `http://localhost:10121` |
| `MailAsync` | Sends synthetic SMTP mail | `localhost:10130`; smtp4dev at `http://localhost:10140` |
| `SeqAsync` | Posts a structured CLEF event with a unique `SampleId` | `http://localhost:10150`; search for returned `SampleId` |
| `WireMockAsync` | Creates a temporary mapping, calls it, removes it | `http://localhost:10080`; HTTPS at `https://localhost:10443` after trusting the toolbox certificate |
| `BlobAsync` | Uploads and downloads a blob | Azurite Blob at `http://localhost:10000`; Azure Storage Explorer |
| `StorageQueueAsync` | Creates a temporary queue, sends/receives/deletes a message | Azurite Queue at `http://localhost:10001`; Azure Storage Explorer |
| `TableAsync` | Writes and reads a partitioned table entity | Azurite Table at `http://localhost:10002`; Azure Storage Explorer |
| `S3Async` | Uploads and downloads an object from initialized `toolbox-samples` bucket | Moto at `http://localhost:10195`; AWS CLI with explicit endpoint |

Databases, DynamoDB tables, Blob containers, and Azure Tables remain available
after initialization. Sample documents, keys, blobs, queue resources, table rows,
S3 objects, and WireMock mappings are removed after read verification. Mail and
Seq events intentionally remain for UI inspection. Failure during a write or
cleanup can leave sample data; all operations use synthetic content and unique IDs.
Transactional EF Core examples and broker messaging examples live in the sibling
Data and Messaging projects.

Cleanup runs with a separate five-second budget, including after caller
cancellation. If the operation and cleanup both fail, both errors are retained.
Redis commands cannot be canceled through its SDK: the sample stops waiting for
cleanup after five seconds, while its underlying command remains subject to the
configured ten-second Redis async timeout.

RedisInsight connects to `redis:6379` on the Compose network. From the host, use
`localhost:10120`. smtp4dev also maps POP3 `10131` and IMAP `10132`; its web UI
shows captured messages without requiring a real mail provider.

### Configuration

All sample values live in the canonical
[`containers/.env.example`](../../../containers/.env.example). The toolbox
setup scripts copy it to the ignored `containers/.env` and add missing keys
without replacing existing developer values. Edit that file, then generate the
ignored App and Functions JSON settings from the repository root:

```powershell
python setup/configure_examples.py
```

The generator preserves existing JSON settings; pass `--force` to replace them
after editing `.env`. Shell environment variables override matching canonical
settings. Single quotes in `.env` preserve literal dollar signs, and interpolated
database password/user components are quoted using ADO.NET connection grammar.
Licensing tokens and unrelated environment variables are excluded from generated
JSON. The generator does not require a running Docker daemon.

Use .NET configuration or environment variables for explicit runtime overrides.
Environment variables replace `:` with `__`.
The canonical template includes
[Microsoft's public development emulator key](https://learn.microsoft.com/azure/cosmos-db/emulator).
The Cosmos SDK requires a configured key; the current
[vNext emulator does not enforce authentication](https://learn.microsoft.com/azure/cosmos-db/emulator-linux#authentication).
The sample was verified against the running vNext container using the public key.

| Configuration key | Purpose |
| --- | --- |
| `Resources:Cosmos:Endpoint` | Local HTTPS emulator endpoint |
| `Resources:Cosmos:Key` | Required when invoking Cosmos; local emulator key |
| `Resources:Cosmos:AllowUntrustedLocalCertificate` | Explicit local-only certificate opt-in |
| `Resources:DynamoDb:Endpoint` | DynamoDB Local endpoint |
| `Resources:Redis:ConnectionString` | Local Redis connection |
| `Resources:Mail:Host` / `Port` | smtp4dev capture server |
| `Resources:Seq:Endpoint` / `ApiKey` | Local Seq ingestion endpoint and optional key |
| `Resources:WireMock:Endpoint` | Local WireMock administration and mock routes |
| `Resources:AzureStorage:ConnectionString` | Azurite Blob/Queue/Table connection |
| `Resources:S3:Endpoint` | Moto endpoint; shared fallback is `TOOLBOX_AWS_ENDPOINT` |
| `Resources:S3:Bucket` | Local bucket initialized by the `aws-init` Compose service |
| `Resources:AWS:AccessKey` / `SecretKey` | Public local emulator credentials |

AWS clients always use explicit emulator endpoints and signing region
`us-east-1`; ambient AWS credentials and region discovery are not used.
All methods reject public-cloud destinations. For code running inside Compose,
use the corresponding service names and internal ports: `cosmosdb:8081`,
`dynamodb:8000`, `redis:6379`, `smtp4dev:25`, `otlp-seq:80`, `wiremock:8080`,
`aws-emulator:5000`, and explicit Azurite endpoints at `azurite:10000-10002`.

For Cosmos, trust the emulator certificate whenever possible. If choosing the
local development opt-in, configure `Resources__Cosmos__Key` and
`Resources__Cosmos__AllowUntrustedLocalCertificate` in `containers/.env` before
generating settings. The certificate exception is limited to the validated local
Cosmos host and port.
WireMock HTTPS uses normal certificate validation: import the generated toolbox
certificate into the application's trust store.

### Run and verify

Start the toolbox containers using its setup scripts. S3 needs the free Moto
emulator and its initialized `toolbox-samples` bucket.
See the [toolbox container instructions](../../../containers/readme.md) for
setup and licensing details.

```powershell
dotnet run --project src/DeveloperToolbox.Example/DeveloperToolbox.Example.App -- blob
dotnet run --project src/DeveloperToolbox.Example/DeveloperToolbox.Example.App -- storage-queue
dotnet run --project src/DeveloperToolbox.Example/DeveloperToolbox.Example.App -- s3
```

Successful results contain the content read from the service. A mismatched read
throws instead of reporting success. If a service fails, verify the corresponding
container, port, endpoint configuration, and (for Cosmos HTTPS) certificate trust.
Missing S3 buckets indicate `aws-init` did not complete.

The Blob and Queue samples explicitly use REST API `2026-06-06`, supported by
Azurite 3.37.0, because current stable SDK defaults can advance ahead of the
emulator. Update that version when the emulator supports newer APIs.

SDK reference: [Cosmos vNext emulator](https://learn.microsoft.com/azure/cosmos-db/emulator-linux),
[AWS S3 client configuration](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/TS3Config.html),
and [AWS SDK retries and timeouts](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/retries-timeouts.html).
