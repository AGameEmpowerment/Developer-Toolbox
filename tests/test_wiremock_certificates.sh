#!/usr/bin/env bash
# Isolated tests; runtime/keytool stubs create only fixture files.
set -euo pipefail
readonly REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
TEST_ROOT="$(mktemp -d)"
cleanup() {
    if [[ -d "$TEST_ROOT" && "$(basename "$TEST_ROOT")" == tmp.* ]]; then rm -rf "$TEST_ROOT"; fi
}
trap cleanup EXIT
fail() { echo "Assertion failed: $1" >&2; exit 1; }

mkdir -p "${TEST_ROOT}/bin"
cat > "${TEST_ROOT}/bin/docker" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
runtime="$(basename "$0")"
if [[ "${1:-}" == image && "${2:-}" == inspect ]]; then
    printf 'cache-inspect\n' >> "$TOOLBOX_CERT_TEST_LOG"
    exit 0
fi
printf '%s\n' "$runtime" >> "$TOOLBOX_CERT_TEST_LOG"
printf '<%s>\n' "$@" >> "$TOOLBOX_CERT_TEST_LOG"
[[ "${TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD:-}" == "$TOOLBOX_CERT_EXPECTED_PASSWORD" ]] || exit 9
for argument in "$@"; do [[ "$argument" != "$TOOLBOX_CERT_EXPECTED_PASSWORD" ]] || exit 8; done
if [[ "${TOOLBOX_CERT_FAIL:-false}" == true ]]; then
    # Even accidental tool output containing a secret must be suppressed by the generator.
    printf '%s\n' "$TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD"
    exit 7
fi
[[ "${TOOLBOX_CERT_SKIP_OUTPUT:-false}" != true ]] || exit 0
mount="" destination="" last=""
for argument in "$@"; do
    case "$last" in --volume) mount="$argument" ;; -keystore|-file) destination="$argument" ;; esac
    last="$argument"
done
if [[ "$runtime" != keytool ]]; then
    source="${mount%:/toolbox/certs:*}"
    case "$source" in [A-Za-z]:/*) source="$(cygpath -u "$source")" ;; esac
    destination="${source}/$(basename "$destination")"
fi
printf 'fixture output\n' > "$destination"
MOCK
cp "${TEST_ROOT}/bin/docker" "${TEST_ROOT}/bin/podman"
cp "${TEST_ROOT}/bin/docker" "${TEST_ROOT}/bin/keytool"
chmod +x "${TEST_ROOT}/bin/"*
export PATH="${TEST_ROOT}/bin:${PATH}"
export JAVA_HOME=""
export TOOLBOX_CERT_EXPECTED_PASSWORD='fixture;# secret $ with spaces'
export TOOLBOX_CERT_HOST_KEYTOOL=false
export TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD='previous-secret'

# Hide host JDK installations even when these tests run on a Java-equipped CI agent.
command() {
    if [[ "$#" == 2 && "$1" == -v && "$2" == keytool && "$TOOLBOX_CERT_HOST_KEYTOOL" != true ]]; then return 1; fi
    builtin command "$@"
}
compgen() {
    if [[ "$1" == -G && "$2" == *'/bin/keytool' ]]; then return 1; fi
    builtin compgen "$@"
}
export -f command compgen

new_fixture() {
    local repository="${TEST_ROOT}/$1"
    local fixture="${repository}/containers/certs"
    mkdir -p "$fixture" "${repository}/.devcontainer"
    cp "${REPO_ROOT}/.devcontainer/ensure_image.sh" "${repository}/.devcontainer/"
    cp "${REPO_ROOT}/containers/certs/generate-wiremock-cert.sh" "$fixture/"
    printf '%s' "$fixture"
}

run_generator() {
    local fixture="$1" runtime="$2"
    export TOOLBOX_CERT_TEST_LOG="${fixture}/runtime.log"
    : > "$TOOLBOX_CERT_TEST_LOG"
    TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD="$TOOLBOX_CERT_EXPECTED_PASSWORD" \
        bash "${fixture}/generate-wiremock-cert.sh" --runtime "$runtime" -f > "${fixture}/output.log" 2>&1
}

for runtime in docker podman; do
    fixture="$(new_fixture "$runtime path with spaces")"
    run_generator "$fixture" "$runtime" || fail 'Certificate generation failed.'
    [[ -f "${fixture}/wiremock.jks" && -f "${fixture}/wiremock.crt" ]] || fail 'Certificate outputs were missing.'
    [[ "$(grep -Fxc "$runtime" "$TOOLBOX_CERT_TEST_LOG")" == 2 ]] || fail 'Expected two selected-engine invocations.'
    [[ "$(grep -Fxc 'cache-inspect' "$TOOLBOX_CERT_TEST_LOG")" == 1 ]] || fail 'Generator did not inspect its cached helper image.'
    [[ "$(grep -Fc '<--rm>' "$TOOLBOX_CERT_TEST_LOG")" == 2 ]] || fail 'Helper containers were not ephemeral.'
    [[ "$(grep -Fc '<wiremock/wiremock:latest>' "$TOOLBOX_CERT_TEST_LOG")" == 2 ]] || fail 'Generator used an unrelated image.'
    grep -Fq '<-storepass:env>' "$TOOLBOX_CERT_TEST_LOG" || fail 'Password was not read through the environment.'
    grep -Fq '<none>' "$TOOLBOX_CERT_TEST_LOG" || fail 'Container networking was enabled.'
    ! grep -Fq "$TOOLBOX_CERT_EXPECTED_PASSWORD" "$TOOLBOX_CERT_TEST_LOG" || fail 'Password appeared in native arguments.'
    ! grep -Fq "$TOOLBOX_CERT_EXPECTED_PASSWORD" "${fixture}/output.log" || fail 'Password appeared in generator output.'
    if [[ "$runtime" == podman ]]; then
        grep -Fq '<keep-id>' "$TOOLBOX_CERT_TEST_LOG" || fail 'Podman did not preserve writable bind ownership.'
        grep -Fq ':/toolbox/certs:rw,z>' "$TOOLBOX_CERT_TEST_LOG" || fail 'Podman did not share its SELinux label.'
    fi
done

fixture="$(new_fixture 'host preferred')"
TOOLBOX_CERT_HOST_KEYTOOL=true run_generator "$fixture" podman || fail 'Host keytool generation failed.'
[[ "$(grep -Fxc 'keytool' "$TOOLBOX_CERT_TEST_LOG")" == 2 ]] || fail 'Host keytool was not preferred.'
! grep -Fq '<run>' "$TOOLBOX_CERT_TEST_LOG" || fail 'Host keytool unexpectedly started a helper container.'

fixture="$(new_fixture 'engine failure')"
if TOOLBOX_CERT_FAIL=true run_generator "$fixture" podman; then fail 'Engine failure was accepted.'; fi
[[ "$(grep -Fxc 'podman' "$TOOLBOX_CERT_TEST_LOG")" == 1 ]] || fail 'Generator continued after keytool failed.'
! grep -Fq "$TOOLBOX_CERT_EXPECTED_PASSWORD" "${fixture}/output.log" || fail 'Failure output exposed a password.'
fixture="$(new_fixture 'missing output')"
if TOOLBOX_CERT_SKIP_OUTPUT=true run_generator "$fixture" docker; then fail 'Success without certificate output was accepted.'; fi
[[ "$TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD" == previous-secret ]] || fail 'Generator modified its caller environment.'

# Exercise setup's credential/runtime delegation without involving any engine or trust store.
fixture="${TEST_ROOT}/setup custom password"
mkdir -p "${fixture}/containers/certs" "${fixture}/.devcontainer"
cp "${REPO_ROOT}/docker_setup.sh" "$fixture/"
cp "${REPO_ROOT}/docker_pull.sh" "$fixture/"
cp "${REPO_ROOT}/.devcontainer/ensure_image.sh" "${fixture}/.devcontainer/"
for group in common azure aws; do
    cp "${REPO_ROOT}/containers/docker-compose-${group}.yml" "${fixture}/containers/"
done
cp "${REPO_ROOT}/containers/environment.sh" "${fixture}/containers/"
cp "${REPO_ROOT}/containers/.env.example" "${fixture}/containers/"
cp "${REPO_ROOT}/containers/.env.example" "${fixture}/containers/.env"
printf "TOOLBOX_CERT_PASSWORD='%s'\n" "$TOOLBOX_CERT_EXPECTED_PASSWORD" >> "${fixture}/containers/.env"
assignment='export WIREMOCK_KEYSTORE_PASSWORD = "${TOOLBOX_CERT_PASSWORD}"'
printf '%s\n' "$assignment" >> "${fixture}/containers/.env"
cat > "${fixture}/containers/certs/generate-wiremock-cert.sh" <<'GENERATOR'
#!/usr/bin/env bash
set -euo pipefail
[[ "$*" == '--runtime podman -f' ]] || exit 8
if [[ "${TOOLBOX_CERT_GENERATED_MODE:-false}" == true ]]; then
    [[ "$TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD" =~ ^Tb!1[0-9a-f]{64}$ ]] || exit 9
else
    [[ "$TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD" == "$TOOLBOX_CERT_EXPECTED_PASSWORD" ]] || exit 9
fi
printf 'generated\n' >> "$TOOLBOX_CERT_GENERATOR_LOG"
printf 'fixture\n' > "$(dirname "${BASH_SOURCE[0]}")/wiremock.jks"
GENERATOR
cat > "${TEST_ROOT}/bin/podman" <<'SETUP_MOCK'
#!/usr/bin/env bash
set -euo pipefail
if [[ "${1:-}" == image && "${2:-}" == inspect ]]; then
    if [[ "$*" == *--format* ]]; then printf '17.0.5035.5\n'; fi
    exit 0
fi
if [[ "$*" == *'run --rm -T password' ]]; then
    if [[ "${TOOLBOX_CERT_PASSWORD_UTILITY_FAIL:-false}" == true ]]; then
        previous=''
        for argument in "$@"; do
            if [[ "$previous" == -f ]]; then printf '%s' "$TOOLBOX_CERT_EXPECTED_PASSWORD" > "$(dirname "$argument")/value"; fi
            previous="$argument"
        done
        printf '%s\n' "$TOOLBOX_CERT_EXPECTED_PASSWORD"
        exit 7
    fi
    compose_file='' environment_file='' previous=''
    for argument in "$@"; do
        case "$previous" in -f) compose_file="$argument" ;; --env-file) environment_file="$argument" ;; esac
        previous="$argument"
    done
    grep -Fq '"network_mode":"none"' "$compose_file" || exit 8
    grep -Fq '"TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD":"${WIREMOCK_KEYSTORE_PASSWORD:-changeit}"' "$compose_file" || exit 8
    grep -Fq '"userns_mode":"keep-id"' "$compose_file" || exit 8
    ! grep -Fq '"ports"' "$compose_file" || exit 8
    if [[ "${TOOLBOX_CERT_GENERATED_MODE:-false}" == true ]]; then
        value="$(sed -n 's/^WIREMOCK_KEYSTORE_PASSWORD="\(Tb!1[0-9a-f]*\)"$/\1/p' "$environment_file" | tail -1)"
    else value="$TOOLBOX_CERT_EXPECTED_PASSWORD"; fi
    printf '%s' "$value" > "$(dirname "$compose_file")/value"
elif [[ "$1" == ps ]]; then
    [[ "$3" == --filter && ("$4" == label=com.docker.compose.project=dev_common_shared || "$4" == label=io.podman.compose.project=dev_common_shared) ]] || exit 8
    if [[ "$TOOLBOX_CERT_MIGRATION_MODE" == invalid ]]; then printf '%s\n' '--all'; exit 0; fi
    printf '%s\n' aaaaaaaaaaaa bbbbbbbbbbbb cccccccccccc dddddddddddd eeeeeeeeeeee ffffffffffff 111111111111
elif [[ "$1" == inspect ]]; then
    [[ "$3" == *'if eq (index .Config.Labels "com.docker.compose.project") "dev_common_shared"'* && "$3" == *'else if eq (index .Config.Labels "io.podman.compose.project") "dev_common_shared"'* ]] || exit 8
    printf 'inspect %s\n' "$4" >> "$TOOLBOX_CERT_MIGRATION_LOG"
    [[ "$TOOLBOX_CERT_MIGRATION_MODE" != inspect-failure ]] || exit 1
    case "$4" in
        aaaaaaaaaaaa) printf 'function-app\n' ;;
        bbbbbbbbbbbb) printf 'postgres\n' ;;
        cccccccccccc) printf '\n' ;; # Foreign project rejected by the ownership template.
        dddddddddddd) printf 'custom-development-service\n' ;;
        eeeeeeeeeeee) printf 'Function-App\n' ;;
        ffffffffffff) printf 'mssql\n' ;;
        111111111111) printf 'redis\n' ;;
    esac
elif [[ "$1" == rm ]]; then
    [[ "$#" == 3 && "$2" == -f ]] || exit 8
    printf 'rm %s\n' "$3" >> "$TOOLBOX_CERT_MIGRATION_LOG"
elif [[ "$*" == *'up --build -d' ]]; then printf 'up\n' >> "$TOOLBOX_CERT_MIGRATION_LOG"
fi
case "$*" in
    'version --format {{.Server.Version}}') printf '6.0.1\n' ;;
    *'config --services') printf 'wiremock\n' ;;
esac
exit 0
SETUP_MOCK
export TOOLBOX_CERT_MIGRATION_MODE=normal
export TOOLBOX_CERT_MIGRATION_LOG="${fixture}/migration.log"
export TOOLBOX_CERT_GENERATOR_LOG="${fixture}/generation.log"
: > "$TOOLBOX_CERT_MIGRATION_LOG"
if TOOLBOX_CERT_PASSWORD_UTILITY_FAIL=true bash "${fixture}/docker_setup.sh" --runtime podman > "${fixture}/setup.log" 2>&1; then
    fail 'Setup accepted a failed password utility.'
fi
[[ ! -f "$TOOLBOX_CERT_GENERATOR_LOG" ]] || fail 'Setup generated a certificate after its password utility failed.'
! grep -Fq "$TOOLBOX_CERT_EXPECTED_PASSWORD" "${fixture}/setup.log" || fail 'Password utility failure exposed a secret.'
bash "${fixture}/docker_setup.sh" --runtime podman > "${fixture}/setup.log" 2>&1 || fail 'Setup certificate delegation failed.'
grep -Fxq "$assignment" "${fixture}/containers/.env" || fail 'Setup rewrote a custom password assignment.'
! grep -Fq "$TOOLBOX_CERT_EXPECTED_PASSWORD" "${fixture}/setup.log" || fail 'Setup printed a custom password.'
[[ "$(grep -Fc 'rm ' "$TOOLBOX_CERT_MIGRATION_LOG")" == 3 ]] || fail 'Migration removed extra or duplicate containers.'
grep -Fxq 'rm ffffffffffff' "$TOOLBOX_CERT_MIGRATION_LOG" || fail 'Migration did not relocate SQL Server.'
grep -Fxq 'rm bbbbbbbbbbbb' "$TOOLBOX_CERT_MIGRATION_LOG" || fail 'Migration did not relocate PostgreSQL.'
grep -Fxq 'rm aaaaaaaaaaaa' "$TOOLBOX_CERT_MIGRATION_LOG" || fail 'Migration did not target the retired sample service.'
[[ "$(grep -Fc 'inspect ' "$TOOLBOX_CERT_MIGRATION_LOG")" == 7 ]] || fail 'Migration did not deduplicate identifiers.'
for mode in inspect-failure invalid; do
    : > "$TOOLBOX_CERT_MIGRATION_LOG"
    if TOOLBOX_CERT_MIGRATION_MODE="$mode" bash "${fixture}/docker_setup.sh" --runtime podman > "${fixture}/setup.log" 2>&1; then
        fail 'Migration accepted unverifiable container ownership.'
    fi
    ! grep -Fq 'rm ' "$TOOLBOX_CERT_MIGRATION_LOG" || fail 'Migration removed a container after verification failed.'
    ! grep -Fxq up "$TOOLBOX_CERT_MIGRATION_LOG" || fail 'Setup started resources after migration verification failed.'
done
export TOOLBOX_CERT_GENERATED_MODE=true
printf '%s\n' 'WIREMOCK_KEYSTORE_PASSWORD="auto"' >> "${fixture}/containers/.env"
printf 'stale keystore\n' > "${fixture}/containers/certs/wiremock.jks"
bash "${fixture}/docker_setup.sh" --runtime podman > "${fixture}/setup.log" 2>&1 || fail 'Generated-password keystore regeneration failed.'
[[ "$(grep -Fxc generated "$TOOLBOX_CERT_GENERATOR_LOG")" == 2 ]] || fail 'Existing keystore was not regenerated for the new generated password.'
bash "${fixture}/docker_setup.sh" --runtime podman > "${fixture}/setup.log" 2>&1 || fail 'Repeated setup failed.'
[[ "$(grep -Fxc generated "$TOOLBOX_CERT_GENERATOR_LOG")" == 2 ]] || fail 'Unchanged explicit password caused unnecessary keystore regeneration.'
echo 'Bash WireMock/setup tests passed: isolated certificate, provider-resolution, and targeted migration scenarios.'
