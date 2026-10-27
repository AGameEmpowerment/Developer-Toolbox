---
post_title: Local infrastructure containers
author1: Michael Carey
post_slug: local-infrastructure-containers
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [containers, docker, podman, local-development]
ai_note: AI assisted
summary: Resource-only Docker and Podman infrastructure with independent host examples.
post_date: 2026-10-12
---

This folder contains developer-focused Compose configurations and helper files
for local emulators, with Docker and Podman runtime selection.

## Project-owned local identity

The [Keycloak image sources and startup contract](oidc/keycloak/README.md)
provide a local base for independently owned identity containers. From the
repository root, run
`./Build-ToolboxImage.ps1` to create
`localhost/toolbox-oidc-keycloak:latest` for use in other projects. Add
`-ContainerRuntime podman` to pin Podman's store. The
[implementation plan](../docs/local-oidc-plan.md) separates current startup
verification from pending protocol and application qualification.

## Floating image policy

Follow the complete [container image policy](../docs/guides/container-image-policy.md)
for matching-tool exceptions, production pins, approved mirrors and explicit refreshes.

This developer toolbox intentionally uses floating current tags such as
`latest` or `vnext-latest` for local service images. Setup reuses local tags,
prepares only missing images, and falls back to approved registry mirrors.
`pull_policy: never` prevents Compose from implicitly refreshing `latest`.
Run the setup wrappers to supply the SQL build's matching version metadata.

Cosmos DB specifically follows `vnext-latest`; generic `latest` selects the
legacy Linux emulator and is incompatible with this stack's vNext HTTPS,
Explorer, port, and architecture configuration.

Use these files only for local development. Consumer projects and production
deployments should replace `latest` with tested immutable tags or digests.
Images come directly from official Docker Hub publishers, Microsoft Container
Registry, or GitHub Container Registry where available; registry is a fallback,
not the default container-image source.

Before direct host-example validation, run
`bash tests/prepare_host_images.sh docker` (or `podman`). This prepares only
those validators' required images, reuses local tags, and builds their SQL/AWS
images with the matching SQL publisher metadata. It does not start shared services.

## Local SQL Server volume

A persistent Docker named volume is used for the local SQL Server service in `docker-compose-azure.yml`:

- Volume name: `mssql-data`
- Container path: `/var/opt/mssql`

This keeps the database files persisted across container restarts and recreations.

The SQL Server base image uses `latest`. Setup inspects its local
`com.microsoft.version` publisher label and passes it as `MSSQL_ENGINE_VERSION`.
The Dockerfile extracts that exact FTS package without installing
the engine package a second time, avoiding roughly 2.6 GB of additional
uncompressed package content. Missing or malformed metadata, an unknown engine
major, or an unavailable exact FTS package fails the build.

For a direct build, prepare the base first, then pass its inspected label:

```powershell
$version = docker image inspect --format '{{ index .Config.Labels "com.microsoft.version" }}' mcr.microsoft.com/mssql/server:latest
docker build --build-arg "MSSQL_ENGINE_VERSION=$version" -t localhost/toolbox-sql:latest containers/mssql
```

## Local PostgreSQL and DynamoDB volumes

PostgreSQL persists its database under the `postgres-data` named volume, and
DynamoDB Local persists its shared database under the `dynamodb-data` named
volume. Normal setup and teardown retain both volumes; pass the explicit volume
cleanup and force flags to `docker_down.ps1` or `docker_down.sh` to remove them.

Local endpoints and defaults are:

- PostgreSQL: `localhost:10432`, database `ProjectExample`, with credentials
  from `containers/.env`.
- DynamoDB Local: `http://localhost:10180`. SDKs still require placeholder AWS
  credentials and a region, but DynamoDB Local does not validate them.

## Local Azurite volume

A persistent Docker named volume is used for the local Azurite service in `docker-compose-azure.yml`:

- Volume name: `azurite-data`
- Container path: `/data`

This keeps Azurite blob, queue, and table data persisted across container restarts and recreations.

## Run compose (development)

The setup scripts build and start three Compose projects in one run:

| Project | Compose file | Services |
| --- | --- | --- |
| `dev_common_shared` | `docker-compose-common.yml` | SeqOtlp, Smtp4Dev, RedisCache, RedisInsight, Wiremock |
| `dev_azure_shared` | `docker-compose-azure.yml` | SqlServer, CosmoDb, Azurite (Azure Storage emulator), ServiceBus |
| `dev_aws_shared` | `docker-compose-aws.yml` | PostgreSql, DynamoDb, RabbitMq, ToolboxAwsEmulator |

The AWS group also contains the required `dynamodb-volume-init` and `aws-init`
one-shot initialization jobs. Each group has its own network; dependencies
stay within their group. SQL Server and Service Bus share the Azure group's
`sb-emulator` network.

Setup automatically removes cloud-resource containers still labeled
`dev_common_shared` before recreating them in their new groups. Existing common
services and unrelated containers are retained. Azure and AWS volumes explicitly
keep their original `dev_common_shared_*` names so existing data survives the
split. These volumes are external to Compose because their original project
labels remain. Setup reuses existing volumes unchanged and creates missing
ones with project labels for explicit teardown cleanup. Run setup before direct
Azure or AWS `compose up` commands on a fresh installation. Normal teardown
retains all data; `docker_down.ps1 -CleanVolumes -Force` or
`docker_down.sh --clean-volumes --force` removes it. Repeated setup runs retain
data and start all three groups.

Sample C#, Java, Node.js, and Python applications run
independently on the developer host; they are not bundled into shared images.
Setup creates `.env`, adds missing keys, generates managed `auto` credentials,
and preserves explicit developer values. Moto is free and open source
(Apache 2.0); no license token, account, or engine socket is required.
See [AWS resources](aws/README.md) for topology and endpoint details.

Run `python setup/configure_examples.py` to generate ignored C# settings,
per-language Functions settings, and `containers/examples.settings.json` for
Node.js/Python SDK validation. Java reads `containers/.env` through its local-only
parser. Host examples use published `localhost` ports. POSIX-generated settings
and credentials are readable only by their owner. See the
[language guides](../README.md#primary-language-examples) for host commands.

The shared stack includes no Functions hosts, Lambda runtimes, SQS dispatchers,
or application key initializer. Run optional Functions hosts with Core Tools in
the relevant sample directory. Moto does not execute the example Lambda code;
invoke the handlers from host code or unit tests.

SQL Server requires Linux AMD64; its service selects that platform explicitly.
ARM64 developers need an engine capable of running the AMD64 SQL Server image.

Deleting `.env` regenerates local credentials; existing database and RabbitMQ
volumes keep their initialized passwords, and Seq encrypted storage keeps its
original key. Retain `.env` when retaining data, or explicitly remove
the disposable volumes with `docker_down -CleanVolumes -Force` (Bash:
`--clean-volumes --force`) before rebuilding from regenerated credentials.

### Validate already-running resources

The C# console has `validate [resource ...] [--json] [--timeout-seconds 45]`.
Without names it checks the resource SDK examples; names are case-insensitive and deduplicated. It never changes container
lifecycle. A stopped or missing service fails rather than being skipped.

From the repository root, generate settings and build Release, then run selected
checks without build output:

```powershell
python setup/configure_examples.py
dotnet build DeveloperToolbox.Example.slnx --configuration Release
dotnet run --project src/DeveloperToolbox.Example/DeveloperToolbox.Example.App --configuration Release --no-build -- validate sqlserver postgres storage-queue
```

The settings generator preserves existing JSON unless `--force` is explicitly
selected. Exit codes are `0` for all passed, `1` for failures, timeouts, or
cancellation, and `2` for bad arguments. The per-operation timeout is cooperative,
defaults to 45 seconds, and accepts integers from 1 to 300; cleanup normally gets
its own five-second budget and may extend elapsed time. JSON excludes provider
diagnostics, outputs, and secrets.

These checks perform SDK sample operations and write synthetic data. Temporary
objects are cleaned up where possible; database messages, SMTP mail, and Seq
events remain. SMTP acceptance and Seq ingestion do not query their web UIs.
Management UIs are not separate checks in the C# CLI.

Service Bus uses dedicated `queue.validation`, `topic.validation`, and
`subscription.validation` entities for correlated publish/receive checks.
Ordinary send commands retain `queue.1`/`topic.1`, and native Functions retain
their dedicated entities. If the emulator is already running when
`service-bus/Config.json` changes, explicitly restart that emulator to load the
new entities before validation. Validation does not perform that restart or
repair preexisting database state. See the
[C# guide](../src/DeveloperToolbox.Example/README.md#live-resource-validation) for all names.

### Start the configured services

From the repository root you can start the configured services with:

```pwsh
# Preferred: use the helper script which sets up environment and runs compose
./docker_setup.ps1

# Select a runtime explicitly when both are installed
./docker_setup.ps1 -ContainerRuntime podman

# After setup has generated .env credentials and certificates, start Compose directly
$containerRuntime = 'docker' # Use 'podman' for Podman.
foreach ($group in @('common', 'azure', 'aws')) {
    & $containerRuntime compose --env-file containers/.env -f "containers/docker-compose-$group.yml" -p "dev_${group}_shared" up --build -d
}

# Refresh the Docker images used by the development stack
./docker_pull.ps1

# Tail logs for SQL server
docker compose --env-file containers/.env -f containers/docker-compose-azure.yml -p dev_azure_shared logs -f mssql

# Tail logs for RedisInsight
docker compose --env-file containers/.env -f containers/docker-compose-common.yml -p dev_common_shared logs -f redisinsight
```

```bash
# Linux/macOS/WSL equivalents
./docker_setup.sh
./docker_setup.sh --runtime podman
./docker_pull.sh
```

The helper scripts default to `auto`, preferring the first reachable Docker or Podman engine with Compose support. Set `CONTAINER_RUNTIME` or pass `-ContainerRuntime`/`--runtime` to select one explicitly.

Both setup and image-pull scripts create `containers/.env` from `.env.example`
when needed and add missing template settings to existing files before invoking
Compose. Image refreshes preserve existing values, including blanks. Setup also
resolves managed `auto` password/key markers;
explicit credentials are preserved. Image refreshes do not start containers or
require AWS credentials or license tokens.

Podman requires server version 6 or later to preserve manual stops across a
machine reboot. Setup verifies the engine version, rather than only the client
version. Install a compatible Compose provider; `podman compose` delegates to
Docker Compose or `podman-compose`. The selected provider must connect to the
same engine as `podman info`. On Windows, make sure the machine's API forwarding
and the selected Podman connection refer to the same rootful or rootless engine.
All three files disable shared Podman pods so services retain their own network
namespaces; Service Bus and RabbitMQ both listen internally on port 5672.

If native Podman commands work but Compose reports an EOF from the Windows
named pipe, compare the selected connection with the machine's forwarding mode:

```pwsh
podman system connection list
podman machine inspect podman-machine-default --format '{{.Rootful}}'
podman info --format '{{.Host.Security.Rootless}}'
```

Choose the engine whose existing data you intend to use. Changing only the
default connection does not change API forwarding. For a rootful machine:

```pwsh
podman machine stop podman-machine-default
podman machine set --rootful=true podman-machine-default
podman machine start podman-machine-default
```

Use `--rootful=false` for rootless operation and select the matching connection
shown by `podman system connection list`. The two engines have separate stores;
switching modes preserves their data. Verify the selected provider with
`podman compose --env-file containers/.env -f containers/docker-compose-common.yml -p dev_common_shared ps`.
Alternatively, use an already installed native provider for the current
PowerShell session with
`$env:PODMAN_COMPOSE_PROVIDER = (Get-Command podman-compose).Source`.
See [machine forwarding](https://docs.podman.io/en/latest/markdown/podman-machine-set.1.html)
and [Compose provider selection](https://docs.podman.io/en/latest/markdown/podman-compose.1.html).

Moto uses normal container networking and does not mount the Docker/Podman
engine socket. Repository bind mounts retain
shared SELinux labels for portable Podman access.

### Stop and restart an individual service

Long-running services use `restart: unless-stopped`. A manual stop persists
across engine or machine restarts until you explicitly start the container.
Running `docker_setup` or Compose `up` is an explicit request to start the whole
stack. Initialization jobs use `restart: "no"` and exit when their work completes.

```pwsh
$containerRuntime = 'podman' # Use 'docker' for Docker.
& $containerRuntime compose --env-file containers/.env -f containers/docker-compose-aws.yml -p dev_aws_shared stop rabbitmq
& $containerRuntime compose --env-file containers/.env -f containers/docker-compose-aws.yml -p dev_aws_shared start rabbitmq
```

```bash
container_runtime=podman # Use docker for Docker.
"$container_runtime" compose --env-file containers/.env -f containers/docker-compose-aws.yml -p dev_aws_shared stop rabbitmq
"$container_runtime" compose --env-file containers/.env -f containers/docker-compose-aws.yml -p dev_aws_shared start rabbitmq
```

The lifecycle check uses an isolated copy of the common Redis service, with
unique container, network, and volume names. It verifies startup, a retained
manual stop, and an explicit restart without changing the shared stack:

```bash
python tests/test_container_lifecycle.py --runtime docker
python tests/test_container_lifecycle.py --runtime podman
```

When using `podman-compose`, run the check with the Python environment containing
that provider and its PyYAML dependency. The AWS emulator uses a standard
Compose service and requires no separate container-engine integration.

To stop all three shared groups without deleting persistent data, run `./docker_down.ps1` on Windows or `./docker_down.sh` on Linux/macOS/WSL. Deleting named volumes now requires an explicit second confirmation flag: `-CleanVolumes -Force` or `--clean-volumes --force`.

Redis and RedisInsight endpoints for local development:

- Redis: `localhost:10120`
- RedisInsight Web UI: `http://localhost:10121`

RedisInsight is preconfigured to connect to the Redis service in Docker (`redis:6379`, DB `0`).
If your app writes to another logical database (for example, DB `1`), switch the selected DB in RedisInsight after connecting.

## Backup and restore persistent database volumes

Stop the affected service before copying its files. The same archive workflow
works for all three database volumes. The Azure and AWS files retain the original volume names, including for direct
Compose startup:

- `dev_common_shared_mssql-data`
- `dev_common_shared_postgres-data`
- `dev_common_shared_dynamodb-data`

Use `docker volume ls` or `podman volume ls` to confirm the volume names before
running a backup or restore command.

To back up the SQL Server database files from the named volume to a tar file on the host:

```pwsh
# Create a temporary container to tar the volume contents
docker run --rm \
  -v dev_common_shared_mssql-data:/volume \
  -v ${PWD}:/backup \
  alpine \
  sh -c "cd /volume && tar czf /backup/mssql-data-backup-$(date +%Y%m%d%H%M%S).tgz ."
```

To restore from a backup tar into the named volume (overwrite volume contents):

```pwsh
# Restore into a temporary container (dangerous: will overwrite existing volume contents)
docker run --rm \
  -v dev_common_shared_mssql-data:/volume \
  -v ${PWD}:/backup \
  alpine \
  sh -c "cd /volume && tar xzf /backup/mssql-data-backup.tgz"
```

Notes:

- The Azure and AWS volume names keep their `dev_common_shared_` prefix
  independently of their new Compose project names. Confirm the exact name
  before copying data.
- To archive or restore PostgreSQL or DynamoDB Local, substitute the confirmed
  `postgres-data` or `dynamodb-data` volume name and a matching backup filename
  in the commands above.
- If you prefer direct host access to database files for debugging, consider switching the volume to a bind mount (e.g., `./mssql-data:/var/opt/mssql`) — be aware of permission differences between host and container filesystems.

## Troubleshooting

- If Podman reaches its engine but container startup fails with unavailable
  cgroup controllers or an OCI runtime error, repair the Podman machine/runtime
  configuration before running setup again. Compose validation alone cannot
  verify container execution. Rerun the
  [lifecycle check](../tests/test_container_lifecycle.py) once the engine can
  start containers.
- If a reused SQL Server volume makes Service Bus fail with error 5170 for
  `SbGatewayDatabase.mdf`, existing emulator database files may be orphaned from
  SQL Server metadata. Inspect the databases and back up the affected files
  before repairing that local state. Setup preserves existing database data;
  do not delete the shared SQL volume to repair only the emulator. Validate the broker with the host SDK examples against a clean local state.
- If SQL Server connections to `localhost` time out on a host that resolves it
  to IPv6, use `Server=127.0.0.1,10433`. The canonical sample template uses that
  address to match the Compose IPv4 loopback binding.

- Run `./docker_setup.ps1` or `./docker_setup.sh` from the repository root to
  create `containers/.env` and generate local credentials before starting
  Compose. Copying `.env.example` alone leaves `auto` markers unresolved;
  SQL Server requires the complex password generated by setup. Existing
  database volumes retain their initialized credentials, so preserve their
  corresponding explicit `.env` values.
- If RedisInsight appears empty while cache is active, verify the selected Redis DB index in RedisInsight (the default preconfigured DB is `0`).
- If you need to inspect the volume, use a temporary container and shell into it:

```pwsh
docker run --rm -it -v dev_common_shared_mssql-data:/volume alpine sh
ls -la /volume
```
