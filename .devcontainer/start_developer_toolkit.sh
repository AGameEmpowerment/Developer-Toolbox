#!/usr/bin/env bash
# Start the shared local service stack from inside a devcontainer.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
readonly REPO_ROOT="$(dirname "$SCRIPT_DIR")"

container_runtime="${CONTAINER_RUNTIME:-auto}"
dry_run=0

usage() {
    cat <<'EOF'
Usage: ./.devcontainer/start_developer_toolkit.sh [OPTIONS]

Options:
  --runtime VALUE  Container runtime: auto, docker, or podman. Default: auto.
  --dry-run        Validate the environment without starting containers.
  -h, --help       Show this help.
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

is_devcontainer() {
    local value
    for value in "${DEVCONTAINER:-}" "${REMOTE_CONTAINERS:-}" "${CODESPACES:-}"; do
        case "$value" in
            1|true|TRUE|True)
                return 0
                ;;
        esac
    done
    return 1
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

if ! is_devcontainer; then
    echo "error: this command must be run from inside a devcontainer or GitHub Codespace" >&2
    echo "Open the repository in its devcontainer, then retry." >&2
    exit 1
fi

if [[ -f "${REPO_ROOT}/setup_toolbox.sh" ]]; then
    setup_command=(bash "${REPO_ROOT}/setup_toolbox.sh" --runtime "$container_runtime")
elif [[ -f "${REPO_ROOT}/docker_setup.sh" ]]; then
    setup_command=(bash "${REPO_ROOT}/docker_setup.sh" --runtime "$container_runtime")
else
    echo "error: neither '${REPO_ROOT}/docker_setup.sh' nor '${REPO_ROOT}/setup_toolbox.sh' was found" >&2
    exit 1
fi

if [[ "$dry_run" -eq 1 ]]; then
    echo "Devcontainer detected. Would start the local emulator stack with runtime '$container_runtime'."
    exit 0
fi

cd "$REPO_ROOT"
"${setup_command[@]}"


