#!/bin/bash
# Pull container images required by the shared development stack.
#
# Most services are declared as image-based services in
# containers/docker-compose-common.yml, so we let the selected runtime pull those
# directly. The local SQL Server service is built from containers/mssql, so we
# also pull its base image explicitly.

if [ -z "${BASH_VERSION:-}" ]; then
    echo "Missing dependency: Bash 3.2 or later is required. Run this script with bash, not sh." >&2
    exit 1
fi

if (( BASH_VERSINFO[0] < 3 || (BASH_VERSINFO[0] == 3 && BASH_VERSINFO[1] < 2) )); then
    echo "Missing dependency: Bash 3.2 or later is required. Current version: ${BASH_VERSION}." >&2
    exit 1
fi

set -euo pipefail

CONTAINER_RUNTIME="${CONTAINER_RUNTIME:-auto}"

usage() {
    echo "Usage: $0 [OPTIONS]"
    echo ""
    echo "Options:"
    echo "  --runtime auto|docker|podman  Container runtime to use (default: auto, or CONTAINER_RUNTIME)"
    echo "  -h, --help               Show this help message"
}

while [[ $# -gt 0 ]]; do
    case $1 in
        --runtime)
            if [[ $# -lt 2 || "$2" == -* ]]; then
                echo "Error: --runtime requires auto, docker, or podman." >&2
                exit 1
            fi
            CONTAINER_RUNTIME="$2"
            shift 2
            ;;
        --runtime=*)
            CONTAINER_RUNTIME="${1#*=}"
            shift
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            echo "Use --help for usage information." >&2
            exit 1
            ;;
    esac
done

resolve_container_runtime() {
    local requested_runtime="$1"

    case "$requested_runtime" in
        auto)
            if command -v docker >/dev/null 2>&1; then
                printf '%s' "docker"
            elif command -v podman >/dev/null 2>&1; then
                printf '%s' "podman"
            else
                echo "Missing dependency: no supported container runtime CLI was found." >&2
                echo "Install Docker Desktop or Podman, make sure the CLI is available in PATH, then open a new terminal." >&2
                exit 1
            fi
            ;;
        docker|podman)
            if command -v "$requested_runtime" >/dev/null 2>&1; then
                printf '%s' "$requested_runtime"
            else
                echo "Missing dependency: '${requested_runtime}' was not found in PATH." >&2
                echo "Install ${requested_runtime}, make sure its CLI is available in PATH, then open a new terminal." >&2
                exit 1
            fi
            ;;
        *)
            echo "Invalid runtime '${requested_runtime}'. Use docker, podman, or auto." >&2
            exit 1
            ;;
    esac
}

format_runtime_display_name() {
    case "$1" in
        docker)
            printf '%s' "Docker"
            ;;
        podman)
            printf '%s' "Podman"
            ;;
        *)
            printf '%s' "$1"
            ;;
    esac
}

assert_runtime_ready() {
    local runtime="$1"
    local display_name="$2"

    if "$runtime" info >/dev/null 2>&1; then
        return
    fi

    if [[ "$runtime" == "docker" ]]; then
        echo "${display_name} CLI is installed, but the Docker engine is not reachable." >&2
        echo "Start Docker Desktop, wait until it finishes starting, then rerun this script." >&2
    else
        echo "${display_name} CLI is installed, but the Podman engine is not reachable." >&2
        echo "Start the Podman machine with 'podman machine start', then rerun this script." >&2
    fi

    exit 1
}

assert_compose_available() {
    local runtime="$1"
    local display_name="$2"

    if ! "$runtime" compose version >/dev/null 2>&1; then
        echo "Missing dependency: ${display_name} compose support is not available." >&2
        echo "Install ${display_name} with compose support, or choose another runtime with --runtime." >&2
        exit 1
    fi
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="${SCRIPT_DIR}/containers/docker-compose-common.yml"
SQL_BASE_IMAGE="mcr.microsoft.com/mssql/server:latest"
CONTAINER_CLI="$(resolve_container_runtime "$CONTAINER_RUNTIME")"
RUNTIME_DISPLAY_NAME="$(format_runtime_display_name "$CONTAINER_CLI")"
assert_runtime_ready "$CONTAINER_CLI" "$RUNTIME_DISPLAY_NAME"
assert_compose_available "$CONTAINER_CLI" "$RUNTIME_DISPLAY_NAME"

echo "${RUNTIME_DISPLAY_NAME} images and container setup started."

registry_mirrors=()
if [[ "$CONTAINER_CLI" == "docker" ]]; then
    docker_info="$("$CONTAINER_CLI" info 2>/dev/null || true)"
    while IFS= read -r mirror; do
        registry_mirrors+=("$mirror")
    done < <(printf '%s\n' "$docker_info" | awk '/Registry Mirrors:/ { in_mirrors=1; next } in_mirrors && $0 ~ /^[[:space:]]+https?:\/\// { gsub(/^[[:space:]]+/, "", $0); print; next } in_mirrors { in_mirrors=0 }')
fi

if [[ "$CONTAINER_CLI" == "docker" && ${#registry_mirrors[@]} -gt 0 ]]; then
    echo "${RUNTIME_DISPLAY_NAME} registry mirrors detected:"
    for mirror in "${registry_mirrors[@]}"; do
        echo "  $mirror"
    done
fi

echo "Pulling compose-managed images from ${COMPOSE_FILE}..."
if ! "$CONTAINER_CLI" compose -f "${COMPOSE_FILE}" pull; then
    echo "Failed to pull compose-managed images." >&2

    if [[ "$CONTAINER_CLI" == "docker" && ${#registry_mirrors[@]} -gt 0 ]]; then
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
if ! "$CONTAINER_CLI" pull "${SQL_BASE_IMAGE}"; then
    echo "Failed to pull '${SQL_BASE_IMAGE}'." >&2

    if [[ "$CONTAINER_CLI" == "docker" && ${#registry_mirrors[@]} -gt 0 ]]; then
        echo "Docker is configured to use registry mirror(s). If one is unavailable, pulls will fail before reaching the upstream registry." >&2
        echo "Configured mirror(s):" >&2
        for mirror in "${registry_mirrors[@]}"; do
            echo "  $mirror" >&2
        done
        echo "Check Docker Desktop > Settings > Docker Engine, or %APPDATA%/Docker/daemon.json, to remove or fix the mirror." >&2
    fi

    exit 1
fi

echo "${RUNTIME_DISPLAY_NAME} images and container setup completed."
