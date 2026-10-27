#!/usr/bin/env bash
# Local development only: prepare the images required by isolated host-example tests.
set -euo pipefail

runtime="${1:-docker}"
case "$runtime" in docker|podman) ;; *) echo 'Use docker or podman.' >&2; exit 1 ;; esac
repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
source .devcontainer/ensure_image.sh

images=(
    mcr.microsoft.com/mssql/server:latest
    docker.io/library/python:latest
    docker.io/wiremock/wiremock:latest
    docker.io/library/redis:latest
    docker.io/rnwood/smtp4dev:latest
    docker.io/datalust/seq:latest
    mcr.microsoft.com/azure-storage/azurite:latest
    mcr.microsoft.com/azure-messaging/servicebus-emulator:latest
    # The floating management variant provides the broker UI exercised by these tests.
    docker.io/library/rabbitmq:management
    docker.io/amazon/dynamodb-local:latest
    docker.io/library/postgres:latest
)
for image in "${images[@]}"; do ensure_toolbox_image "$runtime" "$image"; done

# FTS must match the engine in the locally selected SQL base.
TOOLBOX_MSSQL_ENGINE_VERSION="$("$runtime" image inspect --format '{{ index .Config.Labels "com.microsoft.version" }}' mcr.microsoft.com/mssql/server:latest)"
[[ "$TOOLBOX_MSSQL_ENGINE_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || {
    echo 'The local SQL Server base is missing a valid publisher version label.' >&2
    exit 1
}
export TOOLBOX_MSSQL_ENGINE_VERSION
"$runtime" compose --env-file containers/.env.example -f containers/docker-compose-azure.yml -p dev_azure_shared build mssql
"$runtime" compose --env-file containers/.env.example -f containers/docker-compose-aws.yml -p dev_aws_shared build aws-emulator
