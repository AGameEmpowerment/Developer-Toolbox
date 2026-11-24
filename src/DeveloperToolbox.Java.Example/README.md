---
post_title: "Java toolbox SDK and serverless examples"
author1: "Developer Toolbox maintainers"
post_slug: "java-toolbox-examples"
microsoft_alias: ""
featured_image: ""
categories: [Development]
tags: [Java, Azure Functions, AWS Lambda, Hibernate]
ai_note: "Created with AI assistance and verified with automated tests."
summary: "Java 25 host examples for local toolbox SDKs, ORM transactions and serverless queues."
post_date: "2026-10-12"
---

## Independent Java build and CLI

These local developer examples require JDK 25 and Maven 3.10 or a compatible
Maven 3 release. Build and run them on your computer independently of the C#,
Node.js and Python projects; their SDKs are not needed. Shared Docker/Podman
setup supplies infrastructure and does not build or package Java sample code.
All dependency versions are declared in the POMs. No cloud account or paid
emulator license is required.

Verify the host toolchain with `java -version`, `javac -version` and `mvn --version`.
The 2026-10-27 verification used Microsoft JDK 25.0.5.0.2 and Maven 3.10.0; all
117 expanded JUnit cases and packaging passed. See the
[verification report](../../docs/parity/verification.md) for module totals and evidence.

Start infrastructure separately with `docker_setup.ps1` or `docker_setup.sh`.
This generates local credentials in ignored `containers/.env`. From the
repository root:

```powershell
mvn -f src/DeveloperToolbox.Java.Example/pom.xml verify
$env:TOOLBOX_ENV_FILE = (Resolve-Path containers/.env).Path
java -jar src/DeveloperToolbox.Java.Example/core/target/core-1.0-SNAPSHOT-cli.jar postgres
java -jar src/DeveloperToolbox.Java.Example/core/target/core-1.0-SNAPSHOT-cli.jar s3
```

On Linux or macOS, use `export TOOLBOX_ENV_FILE="$PWD/containers/.env"`.
The optional dotenv reader treats the file as data and expands `${NAME}` from
earlier assignments or the environment. It never executes shell code.
Environment variables override file assignments. Direct environment
configuration is also supported. Python is not needed for the CLI or Maven tests.

The CLI supports `sqlserver`, `postgres`, `cosmos`, `dynamodb`, `redis`, `mail`,
`seq`, `wiremock`, `blob`, `storagequeue`, `table`, `s3`, `rabbitmq`, `sqs`, `sns`,
`eventbridge`, `servicebus` and `servicebus-topic`, ignoring case. Exit 2 means
invalid arguments and exit 1 means a failed SDK operation. Each command exercises
the service API rather than a management UI.

## Local configuration and transactional databases

Configuration uses `Resources__*`, `TOOLBOX_AWS_ENDPOINT`, `RABBITMQ_*` and
`ServiceBusConnection` from the shared `.env`. Network destinations must be
loopback or the exact service's Compose DNS name. Host examples use loopback
and published toolbox ports. Seq and WireMock disable redirects and proxies.
AWS samples use dummy `test` credentials against Moto or DynamoDB Local.
Real cloud destinations are rejected; adapt endpoint policy and authentication
in a separate application when using these SDKs against managed services.

| ORM setting | Host default |
| --- | --- |
| `JAVA_SQLSERVER_HOST` | `127.0.0.1` |
| `JAVA_SQLSERVER_PORT` | `10433` |
| `JAVA_SQLSERVER_USER` | `sa` |
| `JAVA_POSTGRES_HOST` | `localhost` |
| `JAVA_POSTGRES_PORT` | `10432` |

Credentials come from `MSSQL_SA_PASSWORD`, `POSTGRES_USER` and
`POSTGRES_PASSWORD`. Hibernate creates the separate `ToolboxJavaSamples`
database and `JavaMessages` table. The database name is fixed; inserts, reads
and duplicate checks use ORM parameters. Examples demonstrate a commit, a query
and an explicit rollback. The development connection pool and automatic schema
update are local examples, not production pooling or migration strategies.
Identifiers and text use Hibernate's [nationalized character mapping](https://docs.hibernate.org/orm/7.2/introduction/html_single/), preserving Unicode with SQL Server's `nvarchar` and PostgreSQL's `varchar` types.

The SQL login needs database creation permission; the toolbox uses SA.
PostgreSQL uses the configured local role. Native consumers preserve the first
committed text for duplicate IDs, including concurrent delivery. Failed
transactions roll back and propagate so an invocation host can retry.
Synthetic database messages, SMTP mail and Seq events remain for inspection.
Temporary SDK queues, buckets, tables, mappings and documents are removed.
SNS and EventBridge demonstrate API acceptance without claiming downstream
delivery. RabbitMQ demonstrates AMQP interoperability with Amazon MQ's
RabbitMQ engine.

## Cosmos TLS on the host

The Java Cosmos SDK validates TLS with the JVM trust store. Import the emulator's
public certificate into a dedicated local trust store; the C# setting
`AllowUntrustedLocalCertificate` has no effect on Java. Microsoft's
[Java emulator certificate guide](https://learn.microsoft.com/azure/cosmos-db/emulator-linux)
describes this requirement. The host endpoint uses `localhost`, matching the
default emulator certificate.

The CLI can fetch the public certificate from the validated local Cosmos
endpoint into a new dedicated store. It preserves an existing destination
and leaves the global Java trust store unchanged:

```powershell
$env:JAVA_TRUSTSTORE_PASSWORD = [guid]::NewGuid().ToString()
java -jar src/DeveloperToolbox.Java.Example/core/target/core-1.0-SNAPSHOT-cli.jar trust-cosmos .tmp/java-cosmos.jks
$trustStore = (Resolve-Path .tmp/java-cosmos.jks).Path
$env:JAVA_TOOL_OPTIONS = '-Djavax.net.ssl.trustStore="' + $trustStore + '" -Djavax.net.ssl.trustStoreType=JKS'
java -jar src/DeveloperToolbox.Java.Example/core/target/core-1.0-SNAPSHOT-cli.jar cosmos
```

The store contains public certificates only. JKS certificate entries load
without adding a password to JVM options echoed during startup. Restrict
write access to your local user and do not reuse the creation password for
an account. Cosmos calls require this trust configuration.

## Azure Functions on your computer

Java 25 is supported by [Azure Functions runtime 4](https://learn.microsoft.com/azure/azure-functions/functions-versions).
Install Core Tools 4 with Java 25 support when running the Functions app;
see [Microsoft's local development guide](https://learn.microsoft.com/azure/azure-functions/functions-run-local).
OS installers are available without installing Node.js. CLI and Lambda unit
tests do not require Core Tools.

Create ignored `functions/local.settings.json` with `IsEncrypted: false`, a
`Values` object and `Host: { "LocalHttpPort": 7072 }`. Native bindings need
`FUNCTIONS_WORKER_RUNTIME=java`, `AzureFunctionsWebHost__hostid=toolbox-java`,
`AzureWebJobsStorage=UseDevelopmentStorage=true`,
`ToolboxAzureStorage=UseDevelopmentStorage=true` and the host
`ServiceBusConnection` copied from `containers/.env`. Add the database and SDK
settings for the resources you use. Values are strings; keep credentials local.
`TOOLBOX_ENV_FILE` supplies Java SDK/ORM configuration, but Core Tools native
bindings read their own local settings or process environment.

If Python 3 is already installed, the optional
`python setup/configure_examples.py` helper generates these settings.
It retains existing JSON unless `--force` is requested. Python is optional;
Java developers can populate settings directly.

From the repository root, package and run the generated app:

```powershell
$env:TOOLBOX_ENV_FILE = (Resolve-Path containers/.env).Path
$env:FUNCTIONS_WORKER_JAVA_LOAD_APP_LIBS = 'true'
mvn -f src/DeveloperToolbox.Java.Example/pom.xml -pl functions -am package
func start --script-root src/DeveloperToolbox.Java.Example/functions/target/azure-functions/toolbox-java-functions --port 7072
```

Application library loading follows Microsoft's
[Azure SDK dependency conflict guidance](https://learn.microsoft.com/azure/developer/java/sdk/troubleshooting-dependency-version-conflict).
RabbitMQ uses blocking AMQP sockets and excludes optional Netty 4.2 transport,
preserving Azure's supported Netty 4.1 modules.

Use `POST http://localhost:7072/api/java/samples/{resource}` for CLI-equivalent
SDK examples. Core Tools skips function-key authorization locally; keep the
development host local. `host.json` selects extension bundle 4.

| Trigger/publisher route | Entity | Database |
| --- | --- | --- |
| `servicebus` | `queue.java.functions` | SQL Server |
| `servicebus-topic` | `topic.java.functions` / `subscription.java.functions` | PostgreSQL |
| `storagequeue-trigger` | `toolbox-java-function-work` | PostgreSQL |

Azure messages use `{"id":"stable-id","text":"hello"}`: IDs allow 128
characters and text allows 4096. Storage producers use Base64 to match the
binding. Dedicated entities isolate independently running language examples.
The Functions host supplies retries and poison handling; failures propagate.

## AWS Lambda handler examples

Build and test handlers on the host through Maven. JUnit directly invokes AWS
`RequestHandler` implementations with fake contexts and database services.
Shared setup does not deploy handlers, start a Lambda runtime, or connect an
SQS event-source dispatcher. See the [AWS Java handler model](https://docs.aws.amazon.com/lambda/latest/dg/java-handler.html).

| Handler | Example input | Behavior |
| --- | --- | --- |
| `developertoolbox.lambda.HttpFunction::handleRequest` | [HTTP event](lambda/events/http.json) | API Gateway v2 JSON/base64 validation |
| `developertoolbox.lambda.EventFunction::handleRequest` | [EventBridge event](lambda/events/eventbridge.json) | Validate `detail.message` |
| `developertoolbox.lambda.SqsFunction::handleRequest` | [SQS event](lambda/events/sqs.json) | Commit valid records to PostgreSQL |

HTTP messages use JSON `body` containing `message`; malformed, missing or
oversized text and invalid base64 UTF-8 return status 400. SQS records use
`{"message":"hello"}` and their `messageId` as the database ID. Responses
contain `batchItemFailures` for invalid or failed records. Configure
`ReportBatchItemFailures` when adapting this standard-queue example to an
AWS event-source mapping. Sending to the local SQS resource alone stores
work; the handler tests invoke it explicitly.

SQS leaves a response reserve and declines new work with 25 seconds or less
remaining. JDBC and ORM timeouts are cooperative and cannot guarantee immediate
cancellation during driver initialization or schema creation. Idempotent IDs
make retries safe when a commit completes near timeout. Logs exclude payloads,
connection strings and provider exception messages.

## Tests and licenses

`mvn -f src/DeveloperToolbox.Java.Example/pom.xml verify` builds and tests Java modules only.
JUnit tests require no containers or cloud credentials. H2-backed Hibernate
tests exercise schema creation, rollback, first-value preservation and
concurrent duplicate delivery. They do not replace live SQL Server/PostgreSQL
checks. Run CLI operations against started infrastructure for actual SDK
round trips; stopped resources fail without being restarted by sample code.

Maven, AWS SDKs and most Azure SDKs use Apache-2.0 or MIT. Hibernate ORM 7.4 uses
[Apache-2.0](https://hibernate.org/community/license/), JUnit uses EPL-2.0, and
H2 uses MPL-2.0/EPL-1.0. Service Bus emulator and SQL Server require their
development EULAs. These examples add no paid AWS emulator license. Review
licenses and production concerns when adapting sample code.
