#!/usr/bin/env bash
# Install the developer-toolbox bootstrap files into another Git repository.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
readonly MANIFEST_PATH="${SCRIPT_DIR}/setup/toolbox-bootstrap-files.txt"

TARGET_REPOSITORY=""
TEMP_DIR=""

cleanup() {
    if [[ -n "$TEMP_DIR" && -d "$TEMP_DIR" ]]; then
        rm -rf "$TEMP_DIR"
    fi
}

trap cleanup EXIT

usage() {
    cat <<'EOF'
Usage: ./install_toolbox_setup.sh TARGET_REPOSITORY

Installs Toolbox launchers, devcontainer support, the shared repository bridge
skill, and supporting scripts into the root of an existing Git worktree.
Existing target files and directories are preserved.

Options:
  -h, --help  Show this help.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -h|--help)
            usage
            exit 0
            ;;
        -*)
            echo "error: unknown option '$1'" >&2
            usage >&2
            exit 2
            ;;
        *)
            if [[ -n "$TARGET_REPOSITORY" ]]; then
                echo "error: provide exactly one target repository path" >&2
                exit 2
            fi
            TARGET_REPOSITORY="$1"
            shift
            ;;
    esac
done

if [[ -z "$TARGET_REPOSITORY" ]]; then
    echo "error: TARGET_REPOSITORY is required" >&2
    usage >&2
    exit 2
fi

if ! command -v git >/dev/null 2>&1; then
    echo "error: git is required but was not found on PATH" >&2
    exit 1
fi

if [[ ! -d "$TARGET_REPOSITORY" ]]; then
    echo "error: target '$TARGET_REPOSITORY' does not exist or is not a directory" >&2
    exit 1
fi

target_path="$(cd "$TARGET_REPOSITORY" && pwd -P)"
if [[ "$(git -C "$target_path" rev-parse --is-inside-work-tree 2>/dev/null || true)" != "true" ]]; then
    echo "error: target '$target_path' is not a Git worktree" >&2
    exit 1
fi

repository_root="$(git -C "$target_path" rev-parse --show-toplevel 2>/dev/null)"
repository_root="$(cd "$repository_root" && pwd -P)"
if [[ "$target_path" != "$repository_root" ]]; then
    echo "error: target '$target_path' is inside a Git repository but is not its root; use '$repository_root' instead" >&2
    exit 1
fi

if [[ ! -f "$MANIFEST_PATH" ]]; then
    echo "error: Toolbox bootstrap manifest '$MANIFEST_PATH' was not found" >&2
    exit 1
fi

TEMP_DIR="$(mktemp -d)"
readonly PROJECT_NAME="$(basename "$repository_root")"

create_project_devcontainer() {
    local source_file="$1"
    local destination_file="$2"
    local json_name="$PROJECT_NAME"
    local line=""
    local line_ending=""
    local replaced=0

    json_name="${json_name//\\/\\\\}"
    json_name="${json_name//\"/\\\"}"
    json_name="${json_name//$'\t'/\\t}"
    json_name="${json_name//$'\r'/\\r}"
    json_name="${json_name//$'\n'/\\n}"

    while IFS= read -r line || [[ -n "$line" ]]; do
        line_ending=$'\n'
        if [[ "$line" == *$'\r' ]]; then
            line="${line%$'\r'}"
            line_ending=$'\r\n'
        fi

        if [[ "$replaced" -eq 0 && "$line" =~ ^([[:space:]]*)\"name\"[[:space:]]*: ]]; then
            printf '%s"name": "%s",%s' "${BASH_REMATCH[1]}" "$json_name" "$line_ending" >> "$destination_file"
            replaced=1
        else
            printf '%s%s' "$line" "$line_ending" >> "$destination_file"
        fi
    done < "$source_file"

    if [[ "$replaced" -ne 1 ]]; then
        echo "error: devcontainer template '$source_file' does not contain a name property" >&2
        return 1
    fi
}

prepare_source_file() {
    local source_file="$1"
    local relative_path="$2"
    PREPARED_SOURCE_FILE="$source_file"

    if [[ "$relative_path" == ".devcontainer/devcontainer.json" ]]; then
        PREPARED_SOURCE_FILE="${TEMP_DIR}/devcontainer.json"
        create_project_devcontainer "$source_file" "$PREPARED_SOURCE_FILE"
    fi
}

SOURCE_FILES=()
TARGET_FILES=()
while IFS= read -r manifest_entry || [[ -n "$manifest_entry" ]]; do
    manifest_entry="${manifest_entry%$'\r'}"
    if [[ -z "$manifest_entry" || "$manifest_entry" == \#* ]]; then
        continue
    fi

    source_path="${SCRIPT_DIR}/${manifest_entry}"
    if [[ ! -e "$source_path" && "$manifest_entry" != */* && -f "${SCRIPT_DIR}/setup/${manifest_entry}_" ]]; then
        source_path="${SCRIPT_DIR}/setup/${manifest_entry}_"
    fi

    if [[ -f "$source_path" ]]; then
        prepare_source_file "$source_path" "$manifest_entry"
        SOURCE_FILES+=("$PREPARED_SOURCE_FILE")
        TARGET_FILES+=("${repository_root}/${manifest_entry}")
    elif [[ -d "$source_path" ]]; then
        target_entry_path="${repository_root}/${manifest_entry}"
        if [[ -e "$target_entry_path" || -L "$target_entry_path" ]]; then
            continue
        fi
        while IFS= read -r -d '' source_file; do
            relative_path="${source_file#"${SCRIPT_DIR}/"}"
            prepare_source_file "$source_file" "$relative_path"
            SOURCE_FILES+=("$PREPARED_SOURCE_FILE")
            TARGET_FILES+=("${repository_root}/${relative_path}")
        done < <(find "$source_path" -type f -print0)
    else
        echo "error: Toolbox bootstrap source '$source_path' was not found" >&2
        exit 1
    fi
done < "$MANIFEST_PATH"

for index in "${!SOURCE_FILES[@]}"; do
    source_file="${SOURCE_FILES[$index]}"
    target_file="${TARGET_FILES[$index]}"
    if [[ -e "$target_file" || -L "$target_file" ]]; then
        continue
    fi
    mkdir -p "$(dirname "$target_file")"
    cp "$source_file" "$target_file"
    if [[ "$target_file" == *.sh ]]; then
        chmod +x "$target_file"
    fi
    echo "Installed $target_file"
done

echo "Toolbox bootstrap files are ready in '$repository_root'."
echo "Primary AI skill: toolbox-repository-bridge"
echo "Run './setup_toolbox.sh' or 'pwsh -File ./setup_toolbox.ps1' from the target repository."


