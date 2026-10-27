#!/bin/bash
# Pull container images required by the shared development stack.
#
# Image-based services, including PostgreSQL and DynamoDB Local, are declared in
# containers/docker-compose-{common,azure,aws}.yml, so the selected runtime pulls them
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
            local installed_runtime=false
            local runtime
            for runtime in docker podman; do
                if command -v "$runtime" >/dev/null 2>&1; then
                    installed_runtime=true
                    if "$runtime" info >/dev/null 2>&1 && "$runtime" compose version >/dev/null 2>&1; then
                        printf '%s' "$runtime"
                        return
                    fi
                fi
            done

            if [[ "$installed_runtime" == false ]]; then
                echo "Missing dependency: no supported container runtime CLI was found." >&2
                echo "Install Docker Desktop or Podman, make sure the CLI is available in PATH, then open a new terminal." >&2
            else
                echo "No installed container runtime is reachable with Compose support." >&2
                echo "Start Docker or Podman, or select one explicitly with --runtime." >&2
            fi
            exit 1
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
ENV_FILE="${SCRIPT_DIR}/containers/.env"
ENV_EXAMPLE_FILE="${SCRIPT_DIR}/containers/.env.example"
CONTAINER_CLI="$(resolve_container_runtime "$CONTAINER_RUNTIME")"
RUNTIME_DISPLAY_NAME="$(format_runtime_display_name "$CONTAINER_CLI")"
assert_runtime_ready "$CONTAINER_CLI" "$RUNTIME_DISPLAY_NAME"
assert_compose_available "$CONTAINER_CLI" "$RUNTIME_DISPLAY_NAME"

echo "${RUNTIME_DISPLAY_NAME} missing-image preparation started."

# Match setup's additive environment preparation before Compose parses any services.
# Read assignments as data; never source or evaluate developer configuration.
if [[ ! -f "$ENV_EXAMPLE_FILE" ]]; then
    echo ".env.example not found. Cannot prepare the image-pull environment." >&2
    exit 1
fi
if [[ ! -f "$ENV_FILE" ]]; then
    cp "$ENV_EXAMPLE_FILE" "$ENV_FILE"
    echo "Created containers/.env from .env.example."
fi
missing_settings=0
existing_keys=$'\n'
while IFS= read -r existing_assignment || [[ -n "$existing_assignment" ]]; do
    existing_assignment="${existing_assignment%$'\r'}"
    if [[ "$existing_assignment" =~ ^[[:space:]]*(export[[:space:]]+)?([A-Za-z_][A-Za-z0-9_]*)[[:space:]]*= ]]; then
        existing_keys+="${BASH_REMATCH[2]}"$'\n'
    fi
done < "$ENV_FILE"
while IFS= read -r assignment || [[ -n "$assignment" ]]; do
    assignment="${assignment%$'\r'}"
    if [[ "$assignment" =~ ^[[:space:]]*([A-Za-z_][A-Za-z0-9_]*)[[:space:]]*= ]]; then
        key="${BASH_REMATCH[1]}"
        if [[ "$existing_keys" != *$'\n'"${key}"$'\n'* ]]; then
            # A leading newline also protects files without a final newline.
            printf '\n%s\n' "$assignment" >> "$ENV_FILE"
            existing_keys+="${key}"$'\n'
            missing_settings=$((missing_settings + 1))
        fi
    fi
done < "$ENV_EXAMPLE_FILE"
if (( missing_settings > 0 )); then
    echo "Added ${missing_settings} missing settings to containers/.env."
fi

source "${SCRIPT_DIR}/.devcontainer/ensure_image.sh"
images=()
for group in common azure aws; do
    while IFS= read -r image; do images+=("$image"); done < <(sed -nE 's/^[[:space:]]+image:[[:space:]]*([^[:space:]]+).*/\1/p' "${SCRIPT_DIR}/containers/docker-compose-${group}.yml")
done
images+=(mcr.microsoft.com/mssql/server:latest docker.io/library/python:latest)
while IFS= read -r image; do ensure_toolbox_image "$CONTAINER_CLI" "$image" || exit 1; done < <(printf '%s\n' "${images[@]}" | sort -u)
echo "${RUNTIME_DISPLAY_NAME} missing-image preparation completed."