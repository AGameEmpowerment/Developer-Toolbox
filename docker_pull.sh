#!/bin/bash
# Pull Docker images required by the shared development stack.
#
# Most services are declared as image-based services in
# containers/docker-compose-common.yml, so we let docker compose pull those
# directly. The local SQL Server service is built from containers/mssql, so we
# also pull its base image explicitly.

set -euo pipefail

if ! command -v docker >/dev/null 2>&1; then
    echo "Docker is not installed or not in PATH." >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="${SCRIPT_DIR}/containers/docker-compose-common.yml"
SQL_BASE_IMAGE="mcr.microsoft.com/mssql/server:latest"

echo "Docker images and container setup started."

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

echo "Pulling compose-managed images from ${COMPOSE_FILE}..."
if ! docker compose -f "${COMPOSE_FILE}" pull; then
    echo "Failed to pull compose-managed images." >&2

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

echo "Pulling SQL Server base image for containers/mssql/Dockerfile: ${SQL_BASE_IMAGE}..."
if ! docker pull "${SQL_BASE_IMAGE}"; then
    echo "Failed to pull '${SQL_BASE_IMAGE}'." >&2

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

echo "Docker images and container setup completed."
