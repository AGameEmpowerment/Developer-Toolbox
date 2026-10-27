#!/usr/bin/env bash
# Isolated image-pull tests; no runtime resources or developer settings are changed.
set -euo pipefail

readonly REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
TEST_ROOT="$(mktemp -d)"
cleanup() {
    if [[ -d "$TEST_ROOT" && "$(basename "$TEST_ROOT")" == tmp.* ]]; then
        rm -rf "$TEST_ROOT"
    fi
}
trap cleanup EXIT

fail() {
    echo "Assertion failed: $1" >&2
    exit 1
}

new_fixture() {
    local fixture="${TEST_ROOT}/$1"
    mkdir -p "${fixture}/.devcontainer"
    cp "${REPO_ROOT}/.devcontainer/ensure_image.sh" "${fixture}/.devcontainer/"
    mkdir -p "${fixture}/containers"
    cp "${REPO_ROOT}/docker_pull.sh" "$fixture/"
    cp "${REPO_ROOT}/containers/.env.example" "${fixture}/containers/"
    for group in common azure aws; do
        cp "${REPO_ROOT}/containers/docker-compose-${group}.yml" "${fixture}/containers/"
    done
    printf '%s\n' 'TOOLBOX_FUTURE_SETTING="${POSTGRES_USER}/literal#value"' >> "${fixture}/containers/.env.example"
    printf '%s' "$fixture"
}

mock_bin="${TEST_ROOT}/mock-bin"
mkdir -p "$mock_bin"
cat > "${mock_bin}/runtime" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
printf '%s %s\n' "$(basename "$0")" "$*" >> "$TOOLBOX_TEST_RUNTIME_LOG"
for argument in "$@"; do
    case "$argument" in
        up|start|restart|run|create) echo 'Pull must not start or create services.' >&2; exit 3 ;;
    esac
done
if [[ "${1:-}" == compose && "${!#}" == pull ]]; then
    shift
    env_file=""
    while [[ $# -gt 0 ]]; do
        if [[ "$1" == --env-file ]]; then env_file="$2"; break; fi
        shift
    done
    [[ "$env_file" == "${TOOLBOX_TEST_FIXTURE}/containers/.env" ]] || {
        echo 'Compose pull must use the repaired developer .env.' >&2; exit 3;
    }
    [[ -f "$env_file" ]] || { echo 'Pull must create .env before Compose.' >&2; exit 3; }
    # One scan checks every future template setting, including case-sensitive exported assignments.
    awk '
        FNR == NR {
            line = $0
            if (line ~ /^[ \t]*[A-Za-z_][A-Za-z0-9_]*[ \t]*=/) {
                sub(/=.*/, "", line); gsub(/^[ \t]+|[ \t]+$/, "", line); expected[line] = 1
            }
            next
        }
        {
            line = $0; sub(/^[ \t]*export[ \t]+/, "", line)
            if (line ~ /^[ \t]*[A-Za-z_][A-Za-z0-9_]*[ \t]*=/) {
                sub(/=.*/, "", line); gsub(/^[ \t]+|[ \t]+$/, "", line); counts[line]++
            }
        }
        END { for (key in expected) if (counts[key] != 1) { print "Missing or duplicated template setting: " key > "/dev/stderr"; exit 3 } }
    ' "${TOOLBOX_TEST_FIXTURE}/containers/.env.example" "$env_file"
    printf '%s\n' template-ready-before-pull >> "$TOOLBOX_TEST_RUNTIME_LOG"
fi
MOCK
cp "${mock_bin}/runtime" "${mock_bin}/docker"
cp "${mock_bin}/runtime" "${mock_bin}/podman"
chmod +x "${mock_bin}/docker" "${mock_bin}/podman"

run_pull() {
    local fixture="$1" runtime="$2"
    : > "${fixture}/runtime.log"
    (cd "$fixture" && TOOLBOX_TEST_FIXTURE="$fixture" TOOLBOX_TEST_RUNTIME_LOG="${fixture}/runtime.log" PATH="${mock_bin}:${PATH}" \
        bash "${fixture}/docker_pull.sh" --runtime "$runtime") > "${fixture}/pull.log" 2>&1 || {
        cat "${fixture}/pull.log" >&2; fail "Pull failed its pre-pull configuration assertion."
    }
    ! grep -q ' pull ' "${fixture}/runtime.log" || fail 'Cached images must never be pulled.'
    grep -Fq "$runtime image inspect mcr.microsoft.com/mssql/server:latest" "${fixture}/runtime.log" || fail 'Missing SQL base cache check.'
    ! grep -Eq 'fixture-secret-never-log|custom-connection-never-log|custom-key-never-log' "${fixture}/pull.log" || fail "Pull printed developer values."
}

for runtime in docker podman; do
    fresh="$(new_fixture "$runtime fresh with spaces")"
    run_pull "$fresh" "$runtime"
    cmp -s "${fresh}/containers/.env.example" "${fresh}/containers/.env" || fail "Fresh .env creation changed template contents."

    stale="$(new_fixture "$runtime stale without final newline")"
    cat > "${stale}/original-lines" <<'ENV'
# Retain this developer comment.
  export POSTGRES_PASSWORD = 'fixture-secret-never-log;# $password'
ConnectionStrings__SqlServer="custom-connection-never-log"
export Resources__Seq__ApiKey="fixture-secret-never-log"
FUNCTIONS_HOST_KEY="custom-key-never-log"
FUNCTIONS_MASTER_KEY=
TOOLBOX_CUSTOM_VALUE="$(touch must-not-execute)"
functions_secrets_path="unrelated-case-value"
ENV
    cat "${stale}/original-lines" > "${stale}/containers/.env"
    printf '%s' 'Resources__S3__Endpoint="${TOOLBOX_AWS_ENDPOINT}/custom#literal"' >> "${stale}/containers/.env"
    printf '%s\n' 'Resources__S3__Endpoint="${TOOLBOX_AWS_ENDPOINT}/custom#literal"' >> "${stale}/original-lines"
    run_pull "$stale" "$runtime"
    while IFS= read -r line; do
        grep -Fxq "$line" "${stale}/containers/.env" || fail "Migration overwrote a developer assignment or joined the final line."
    done < "${stale}/original-lines"
    grep -Fxq 'TOOLBOX_FUTURE_SETTING="${POSTGRES_USER}/literal#value"' "${stale}/containers/.env" || fail "Migration expanded raw template interpolation."
    [[ ! -e "${stale}/must-not-execute" ]] || fail "Migration evaluated developer configuration."
    cp "${stale}/containers/.env" "${stale}/before-repeat"
    run_pull "$stale" "$runtime"
    cmp -s "${stale}/before-repeat" "${stale}/containers/.env" || fail "Repeated pull changed complete developer configuration."
done

echo 'Bash pull tests passed: fresh/stale configuration, Docker/Podman, pre-pull repair, idempotence, and secret preservation.'
