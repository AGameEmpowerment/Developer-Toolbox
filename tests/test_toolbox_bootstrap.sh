#!/usr/bin/env bash
set -euo pipefail

readonly REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
TEST_ROOT="$(mktemp -d)"

cleanup() {
    if [[ -n "${TEST_ROOT:-}" && -d "$TEST_ROOT" && "$(basename "$TEST_ROOT")" == tmp.* ]]; then
        rm -rf "$TEST_ROOT"
    fi
}

trap cleanup EXIT

assert_file() {
    local path="$1"
    if [[ ! -f "$path" ]]; then
        echo "Assertion failed: expected file '$path'" >&2
        exit 1
    fi
}

new_test_repository() {
    local path="$1"
    local origin_url="${2:-}"

    mkdir -p "$path"
    git -C "$path" init --quiet
    if [[ -n "$origin_url" ]]; then
        git -C "$path" remote add origin "$origin_url"
    fi
}

mock_bin="${TEST_ROOT}/mock-bin"
mkdir -p "$mock_bin"
printf '%s\n' '#!/usr/bin/env bash' 'exit 1' > "${mock_bin}/docker"
printf '%s\n' \
    '#!/usr/bin/env bash' \
    'if [[ -n "${EXPECTED_CONNECTION:-}" && "${CONTAINER_CONNECTION:-}" != "$EXPECTED_CONNECTION" ]]; then exit 1; fi' \
    '[[ "$1" == "info" ]]' > "${mock_bin}/podman"
printf '%s\n' \
    '#!/usr/bin/env bash' \
    'printf "%s\n" "$*"' > "${mock_bin}/devcontainer"
chmod +x "${mock_bin}/docker" "${mock_bin}/podman" "${mock_bin}/devcontainer"

host_launcher_output="$(PATH="${mock_bin}:${PATH}" bash "${REPO_ROOT}/.devcontainer/start_devcontainer.sh" --dry-run)"
if [[ "$host_launcher_output" != *"--docker-path ${mock_bin}/podman"* ]]; then
    echo "Assertion failed: Bash host launcher did not fall back from unreachable Docker to Podman" >&2
    exit 1
fi

connection_output="$(EXPECTED_CONNECTION=expected CONTAINER_CONNECTION=wrong PATH="${mock_bin}:${PATH}" \
    bash "${REPO_ROOT}/.devcontainer/start_devcontainer.sh" \
    --runtime podman --podman-connection expected --dry-run)"
if [[ "$connection_output" != *"--docker-path ${mock_bin}/podman"* ]]; then
    echo "Assertion failed: Bash host launcher did not honor the selected Podman connection" >&2
    exit 1
fi

consumer_path="${TEST_ROOT}/Consumer Project"
new_test_repository "$consumer_path"
bash "${REPO_ROOT}/install_toolbox_setup.sh" "$consumer_path" >/dev/null

expected_files=(
    "setup_toolbox.ps1"
    "setup_toolbox.sh"
    "setup/_toolbox.ps1"
    "setup/_toolbox.sh"
    "setup/toolbox-bootstrap-files.txt"
    ".devcontainer/devcontainer.json"
    ".devcontainer/post_devcontainer.ps1"
    ".devcontainer/start_devcontainer.ps1"
    ".devcontainer/start_devcontainer.sh"
    ".devcontainer/start_developer_toolkit.ps1"
    ".devcontainer/start_developer_toolkit.sh"
    ".agents/skills/toolbox-repository-bridge/SKILL.md"
    ".claude/skills/toolbox-repository-bridge/SKILL.md"
)
for relative_path in "${expected_files[@]}"; do
    assert_file "${consumer_path}/${relative_path}"
done
for relative_path in start_developer_toolkit.ps1 start_developer_toolkit.sh; do
    if [[ -e "${consumer_path}/${relative_path}" ]]; then
        echo "Assertion failed: installer created redundant root launcher '${relative_path}'" >&2
        exit 1
    fi
done

if [[ ! -x "${consumer_path}/setup_toolbox.sh" ||
    ! -x "${consumer_path}/setup/_toolbox.sh" ||
    ! -x "${consumer_path}/.devcontainer/start_devcontainer.sh" ||
    ! -x "${consumer_path}/.devcontainer/start_developer_toolkit.sh" ]]; then
    echo "Assertion failed: installed Bash launchers are not executable" >&2
    exit 1
fi

if ! grep -Eq '^[[:space:]]*"name"[[:space:]]*:[[:space:]]*"Consumer Project",' \
    "${consumer_path}/.devcontainer/devcontainer.json"; then
    echo "Assertion failed: installed devcontainer name does not match the consumer repository directory" >&2
    exit 1
fi
if ! grep -Fq '${containerWorkspaceFolder}/.devcontainer/post_devcontainer.ps1' \
    "${consumer_path}/.devcontainer/devcontainer.json"; then
    echo "Assertion failed: installed devcontainer post-create command is not workspace-relative" >&2
    exit 1
fi
if ! grep -Eq '^[[:space:]]*"DEVCONTAINER"[[:space:]]*:[[:space:]]*"true",' \
    "${consumer_path}/.devcontainer/devcontainer.json"; then
    echo "Assertion failed: installed devcontainer does not set its explicit environment marker" >&2
    exit 1
fi
if ! grep -Fq './.devcontainer/start_developer_toolkit.ps1' \
    "${consumer_path}/.devcontainer/post_devcontainer.ps1"; then
    echo "Assertion failed: installed post-create guidance does not use the devcontainer developer toolkit command" >&2
    exit 1
fi

if (unset DEVCONTAINER REMOTE_CONTAINERS CODESPACES; \
    bash "${consumer_path}/.devcontainer/start_developer_toolkit.sh" --dry-run) \
    >"${TEST_ROOT}/outside-devcontainer.log" 2>&1; then
    echo "Assertion failed: devcontainer developer toolkit command ran outside a devcontainer" >&2
    exit 1
fi
if ! grep -q "must be run from inside a devcontainer" "${TEST_ROOT}/outside-devcontainer.log"; then
    echo "Assertion failed: devcontainer developer toolkit command did not explain the devcontainer requirement" >&2
    exit 1
fi

if ! DEVCONTAINER=true bash "${consumer_path}/.devcontainer/start_developer_toolkit.sh" --dry-run | \
    grep -q "Would start the local emulator stack"; then
    echo "Assertion failed: devcontainer developer toolkit command did not run inside a devcontainer" >&2
    exit 1
fi

collision_path="${TEST_ROOT}/Consumer With Docker Setup"
new_test_repository "$collision_path"
bash "${REPO_ROOT}/install_toolbox_setup.sh" "$collision_path" >/dev/null
printf '%s\n' \
    '#!/usr/bin/env bash' \
    'echo "toolbox-stub:$2"' > "${collision_path}/setup_toolbox.sh"
printf '%s\n' \
    '#!/usr/bin/env bash' \
    'echo "consumer docker setup must not be selected" >&2' \
    'exit 99' > "${collision_path}/docker_setup.sh"
collision_output="$(DEVCONTAINER=true bash "${collision_path}/.devcontainer/start_developer_toolkit.sh" --runtime podman)"
if [[ "$collision_output" != *"toolbox-stub:podman"* ]]; then
    echo "Assertion failed: consumer launcher did not prefer setup_toolbox.sh" >&2
    exit 1
fi

preserve_path="${TEST_ROOT}/Preserve Existing"
new_test_repository "$preserve_path"
mkdir -p "${preserve_path}/.devcontainer"
printf '%s' 'preserve-me' > "${preserve_path}/.devcontainer/consumer-owned.txt"
printf '%s' 'preserve-me' > "${preserve_path}/setup_toolbox.sh"
bash "${REPO_ROOT}/install_toolbox_setup.sh" "$preserve_path" >/dev/null
if [[ "$(cat "${preserve_path}/setup_toolbox.sh")" != "preserve-me" ]]; then
    echo "Assertion failed: installer replaced an existing target file" >&2
    exit 1
fi
if [[ ! -f "${preserve_path}/.devcontainer/consumer-owned.txt" ]]; then
    echo "Assertion failed: installer replaced an existing target directory" >&2
    exit 1
fi
if [[ -e "${preserve_path}/.devcontainer/devcontainer.json" ]]; then
    echo "Assertion failed: installer added files to an existing target directory" >&2
    exit 1
fi

wrong_toolbox_path="${TEST_ROOT}/Wrong Toolbox"
new_test_repository "$wrong_toolbox_path" "https://github.com/example/not-the-toolbox.git"
if bash "${REPO_ROOT}/setup/_toolbox.sh" \
    --path "$wrong_toolbox_path" --no-update --skip-setup >"${TEST_ROOT}/wrong-origin.log" 2>&1; then
    echo "Assertion failed: launcher accepted a Git repository with an unexpected origin" >&2
    exit 1
fi
if ! grep -q "unexpected origin" "${TEST_ROOT}/wrong-origin.log"; then
    echo "Assertion failed: launcher did not explain the unexpected origin" >&2
    exit 1
fi

valid_toolbox_path="${TEST_ROOT}/Valid Toolbox"
new_test_repository "$valid_toolbox_path" "https://github.com/AGameEmpowerment/Developer-Toolbox.git"
launcher_output="$(bash "${consumer_path}/setup_toolbox.sh" \
    --path "$valid_toolbox_path" --no-update --skip-setup)"
if [[ "$launcher_output" != *"Service startup was skipped"* ]]; then
    echo "Assertion failed: installed root wrapper did not forward launcher arguments" >&2
    exit 1
fi

echo "Bash Toolbox bootstrap tests passed."
