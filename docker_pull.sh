#!/bin/bash
# Pull Docker images used in the setup

set -euo pipefail

if ! command -v docker >/dev/null 2>&1; then
    echo "Docker is not installed or not in PATH." >&2
    exit 1
fi

echo "Docker images and container setup started."

images=(
    "datalust/seq"
    "mcr.microsoft.com/azure-messaging/servicebus-emulator"
    "mcr.microsoft.com/azure-sql-edge"
    "mcr.microsoft.com/azure-storage/azurite"
    "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator"
    "mcr.microsoft.com/dotnet/aspnet"
    "mcr.microsoft.com/dotnet/sdk"
    "mcr.microsoft.com/mssql/server"
    "redis"
    "redis/redisinsight"
    "rnwood/smtp4dev"
    "wiremock/wiremock"
)

mapfile -t registry_mirrors < <(docker info 2>/dev/null | awk '/Registry Mirrors:/ { in_mirrors=1; next } in_mirrors && $0 ~ /^[[:space:]]+https?:\/\// { gsub(/^[[:space:]]+/, "", $0); print; next } in_mirrors { in_mirrors=0 }')

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
