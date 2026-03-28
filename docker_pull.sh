#!/bin/bash
# Pull Docker images required by the shared development stack, including
# base images referenced by repo-local Dockerfiles.

set -euo pipefail

if ! command -v docker >/dev/null 2>&1; then
    echo "Docker is not installed or not in PATH." >&2
    exit 1
fi

echo "Docker images and container setup started."

images=(
    "datalust/seq:latest"
    "mcr.microsoft.com/azure-messaging/servicebus-emulator:latest"
    "mcr.microsoft.com/azure-sql-edge:latest"
    "mcr.microsoft.com/azure-storage/azurite"
    "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview"
    "mcr.microsoft.com/devcontainers/dotnet:1-10.0"
    "mcr.microsoft.com/mssql/server:latest"
    "redis:latest"
    "redis/redisinsight:latest"
    "rnwood/smtp4dev:latest"
    "wiremock/wiremock:latest"
)

registry_mirrors=()
while IFS= read -r mirror; do
    registry_mirrors+=("$mirror")
done < <(docker info 2>/dev/null | awk '/Registry Mirrors:/ { in_mirrors=1; next } in_mirrors && $0 ~ /^[[:space:]]+https?:\/\// { gsub(/^[[:space:]]+/, "", $0); print; next } in_mirrors { in_mirrors=0 }')

if [[ ${#registry_mirrors[@]} -gt 0 ]]; then
    echo "Docker registry mirrors detected:"
    for mirror in "${registry_mirrors[@]}"; do
        echo "  $mirror"
    done
fi

for image in "${images[@]}"; do
    echo "Pulling $image..."
    if ! docker pull "$image"; then
        echo "Failed to pull '$image'." >&2

        if [[ ${#registry_mirrors[@]} -gt 0 ]]; then
            echo "Docker is configured to use registry mirror(s). If one is unavailable, pulls will fail before reaching the upstream registry." >&2
            echo "Configured mirror(s):" >&2
            for mirror in "${registry_mirrors[@]}"; do
                echo "  $mirror" >&2
            done
            echo "Check Docker Desktop > Settings > Docker Engine, or %APPDATA%/Docker/daemon.json, to remove or fix the mirror." >&2
        fi

        exit 1
    fi
done

echo "Docker images and container setup completed."
