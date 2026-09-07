#!/usr/bin/env bash
# Start this repository's devcontainer with Docker or Podman.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
readonly REPO_ROOT="$(dirname "$SCRIPT_DIR")"

container_runtime="${CONTAINER_RUNTIME:-auto}"
podman_connection=""
dry_run=0

usage() {
    cat <<'EOF'
Usage: ./.devcontainer/start_devcontainer.sh [OPTIONS]

Options:
  --runtime VALUE            Host runtime: auto, docker, or podman. Default: auto.
  --podman-connection NAME   Podman remote connection for this process.
  --dry-run                  Validate and print the command without starting it.
  -h, --help                 Show this help.
EOF
}

require_value() {
    local option_name="$1"
    local option_value="${2:-}"
    if [[ -z "$option_value" ]]; then
        echo "error: ${option_name} requires a value" >&2
        exit 2
    fi
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --runtime)
            require_value "$1" "${2:-}"
            container_runtime="$2"
            shift 2
            ;;
        --runtime=*)
            container_runtime="${1#*=}"
            shift
            ;;
        --podman-connection)
            require_value "$1" "${2:-}"
            podman_connection="$2"
            shift 2
            ;;
        --podman-connection=*)
            podman_connection="${1#*=}"
            shift
            ;;
        --dry-run)
            dry_run=1
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "error: unknown option '$1'" >&2
            usage >&2
            exit 2
            ;;
    esac
done

case "$container_runtime" in
    auto|docker|podman) ;;
    *)
        echo "error: --runtime must be auto, docker, or podman" >&2
        exit 2
        ;;
esac

if [[ -n "$podman_connection" && "$container_runtime" != "podman" ]]; then
    echo "error: --podman-connection requires --runtime podman" >&2
    exit 2
fi
if [[ -n "$podman_connection" ]]; then
    export CONTAINER_CONNECTION="$podman_connection"
fi

runtime_names=()
if [[ "$container_runtime" == "auto" ]]; then
    runtime_names=(docker podman)
else
    runtime_names=("$container_runtime")
fi

installed_runtime=""
selected_runtime=""
runtime_path=""
for runtime_name in "${runtime_names[@]}"; do
    if ! command -v "$runtime_name" >/dev/null 2>&1; then
        continue
    fi
    if [[ -z "$installed_runtime" ]]; then
        installed_runtime="$runtime_name"
    fi
    if "$runtime_name" info >/dev/null 2>&1; then
        selected_runtime="$runtime_name"
        runtime_path="$(command -v "$runtime_name")"
        break
    fi
done

if [[ -z "$selected_runtime" ]]; then
    if [[ "$container_runtime" != "auto" && -z "$installed_runtime" ]]; then
        echo "error: the '$container_runtime' command was not found in PATH" >&2
    elif [[ "$container_runtime" != "auto" ]]; then
        echo "error: the '$container_runtime' engine is not reachable" >&2
    elif [[ -z "$installed_runtime" ]]; then
        echo "error: neither Docker nor Podman was found in PATH" >&2
    else
        echo "error: neither Docker nor Podman has a reachable engine" >&2
    fi
    exit 1
fi

if command -v devcontainer >/dev/null 2>&1; then
    devcontainer_command=(devcontainer)
elif command -v npx >/dev/null 2>&1; then
    devcontainer_command=(npx --yes @devcontainers/cli)
else
    echo "error: install the Dev Container CLI or Node.js with npx, then retry" >&2
    exit 1
fi

devcontainer_command+=(
    up
    --workspace-folder "$REPO_ROOT"
    --docker-path "$runtime_path"
)

if [[ "$dry_run" -eq 1 ]]; then
    printf 'Would run:'
    printf ' %q' "${devcontainer_command[@]}"
    printf '\n'
    exit 0
fi

"${devcontainer_command[@]}"


