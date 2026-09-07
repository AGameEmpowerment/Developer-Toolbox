#!/usr/bin/env bash
# Get the Developer Toolbox and start its shared local service stack.
set -euo pipefail

readonly TOOLBOX_REPOSITORY="AGameEmpowerment/Developer-Toolbox"
readonly TOOLBOX_HTTPS_URL="https://github.com/${TOOLBOX_REPOSITORY}.git"
readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
readonly CONSUMER_REPOSITORY_ROOT="$(dirname "$SCRIPT_DIR")"

TOOLBOX_PATH="${DEVELOPER_TOOLBOX_ROOT:-}"
BRANCH="main"
CONTAINER_RUNTIME="auto"
SKIP_SETUP=0
NO_UPDATE=0

usage() {
    cat <<'EOF'
Usage: ./setup/_toolbox.sh [OPTIONS]

Options:
  --path DIR        Toolbox checkout path. Defaults to $DEVELOPER_TOOLBOX_ROOT, then
                    an Developer-Toolbox directory beside this repository.
  --branch NAME     Toolbox branch to clone or fast-forward. Default: main.
  --runtime VALUE   Container runtime: auto, docker, or podman. Default: auto.
  --skip-setup      Clone or update without starting the service stack.
  --no-update       Do not fetch updates for an existing checkout.
  -h, --help        Show this help.
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
        --path)
            require_value "$1" "${2:-}"
            TOOLBOX_PATH="$2"
            shift 2
            ;;
        --path=*)
            TOOLBOX_PATH="${1#*=}"
            shift
            ;;
        --branch)
            require_value "$1" "${2:-}"
            BRANCH="$2"
            shift 2
            ;;
        --branch=*)
            BRANCH="${1#*=}"
            shift
            ;;
        --runtime)
            require_value "$1" "${2:-}"
            CONTAINER_RUNTIME="$2"
            shift 2
            ;;
        --runtime=*)
            CONTAINER_RUNTIME="${1#*=}"
            shift
            ;;
        --skip-setup)
            SKIP_SETUP=1
            shift
            ;;
        --no-update)
            NO_UPDATE=1
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

case "$CONTAINER_RUNTIME" in
    auto|docker|podman) ;;
    *)
        echo "error: --runtime must be auto, docker, or podman" >&2
        exit 2
        ;;
esac

if ! command -v git >/dev/null 2>&1; then
    echo "error: git is required but was not found on PATH" >&2
    exit 1
fi

if [[ -z "$TOOLBOX_PATH" ]]; then
    TOOLBOX_PATH="$(dirname "$CONSUMER_REPOSITORY_ROOT")/Developer-Toolbox"
fi

gh_authenticated() {
    command -v gh >/dev/null 2>&1 && gh auth status --hostname github.com >/dev/null 2>&1
}

toolbox_origin_is_valid() {
    local origin_url="$1"
    local normalized_url=""

    normalized_url="$(printf '%s' "$origin_url" | tr '[:upper:]' '[:lower:]')"
    normalized_url="${normalized_url%/}"
    normalized_url="${normalized_url%.git}"

    case "$normalized_url" in
        https://github.com/agameempowerment/developer-toolbox|\
        git@github.com:agameempowerment/developer-toolbox|\
        ssh://git@github.com/agameempowerment/developer-toolbox)
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

GIT_AUTHENTICATION_ARGUMENTS=()
if gh_authenticated; then
    GIT_AUTHENTICATION_ARGUMENTS=(-c 'credential.helper=' -c 'credential.helper=!gh auth git-credential')
fi

if [[ -d "$TOOLBOX_PATH" ]] && git -C "$TOOLBOX_PATH" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    toolbox_root="$(git -C "$TOOLBOX_PATH" rev-parse --show-toplevel)"
    toolbox_root="$(cd "$toolbox_root" && pwd -P)"
    requested_toolbox_path="$(cd "$TOOLBOX_PATH" && pwd -P)"
    if [[ "$toolbox_root" != "$requested_toolbox_path" ]]; then
        echo "error: Toolbox path '$requested_toolbox_path' is not the repository root '$toolbox_root'" >&2
        exit 1
    fi

    origin_url="$(git -C "$TOOLBOX_PATH" remote get-url origin 2>/dev/null || true)"
    if [[ -z "$origin_url" ]]; then
        echo "error: Toolbox checkout '$requested_toolbox_path' does not have an origin remote" >&2
        exit 1
    fi
    if ! toolbox_origin_is_valid "$origin_url"; then
        echo "error: Toolbox checkout '$requested_toolbox_path' has unexpected origin '$origin_url'; expected $TOOLBOX_HTTPS_URL" >&2
        exit 1
    fi

    echo "Using existing Toolbox checkout at $TOOLBOX_PATH"
    if [[ "$NO_UPDATE" -eq 0 ]]; then
        if git ${GIT_AUTHENTICATION_ARGUMENTS[@]+"${GIT_AUTHENTICATION_ARGUMENTS[@]}"} -C "$TOOLBOX_PATH" fetch origin --prune; then
            current_branch="$(git -C "$TOOLBOX_PATH" rev-parse --abbrev-ref HEAD)"
            if [[ "$current_branch" == "$BRANCH" ]]; then
                if ! git -C "$TOOLBOX_PATH" merge --ff-only "origin/$BRANCH"; then
                    echo "warning: could not fast-forward '$BRANCH'; continuing with the existing checkout" >&2
                fi
            else
                echo "warning: Toolbox is on '$current_branch', not '$BRANCH'; continuing without changing branches" >&2
            fi
        else
            echo "warning: could not fetch Toolbox updates; continuing with the existing checkout" >&2
        fi
    fi
elif [[ -e "$TOOLBOX_PATH" ]]; then
    echo "error: Toolbox path '$TOOLBOX_PATH' exists but is not a Git repository" >&2
    exit 1
else
    echo "Cloning $TOOLBOX_REPOSITORY ($BRANCH) to $TOOLBOX_PATH..."
    clone_succeeded=0
    if gh_authenticated && gh repo clone "$TOOLBOX_REPOSITORY" "$TOOLBOX_PATH" -- --branch "$BRANCH"; then
        clone_succeeded=1
    fi
    if [[ "$clone_succeeded" -eq 0 ]]; then
        git ${GIT_AUTHENTICATION_ARGUMENTS[@]+"${GIT_AUTHENTICATION_ARGUMENTS[@]}"} clone \
            --branch "$BRANCH" "$TOOLBOX_HTTPS_URL" "$TOOLBOX_PATH"
    fi
fi

if [[ "$SKIP_SETUP" -eq 1 ]]; then
    echo "Toolbox is ready at $TOOLBOX_PATH. Service startup was skipped."
    exit 0
fi

setup_script="${TOOLBOX_PATH}/docker_setup.sh"
if [[ ! -f "$setup_script" ]]; then
    echo "error: Toolbox setup script '$setup_script' was not found" >&2
    exit 1
fi

chmod +x "$setup_script" "${TOOLBOX_PATH}/docker_down.sh" 2>/dev/null || true
echo "Starting the Toolbox service stack with runtime '$CONTAINER_RUNTIME'..."
(
    cd "$TOOLBOX_PATH"
    bash ./docker_setup.sh --runtime "$CONTAINER_RUNTIME"
)

echo "Toolbox service stack is running."
echo "Stop it with: '${TOOLBOX_PATH}/docker_down.sh' --runtime '$CONTAINER_RUNTIME'"


