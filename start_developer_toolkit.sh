#!/usr/bin/env bash
# Repository-root entry point for the devcontainer's local service stack.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
readonly LAUNCHER="${SCRIPT_DIR}/.devcontainer/start_developer_toolkit.sh"

if [[ ! -f "$LAUNCHER" ]]; then
    echo "error: developer toolkit launcher was not found at '$LAUNCHER'" >&2
    exit 1
fi

exec bash "$LAUNCHER" "$@"


