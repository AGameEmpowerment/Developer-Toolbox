#!/usr/bin/env bash
# Isolated startup tests; no containers, certificates, or developer configuration are changed.
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
    mkdir -p "${fixture}/containers/certs"
    cp "${REPO_ROOT}/docker_setup.sh" "$fixture/"
    cp "${REPO_ROOT}/docker_pull.sh" "$fixture/"
    cp "${REPO_ROOT}/docker_down.sh" "$fixture/"
    cp "${REPO_ROOT}/containers/.env.example" "${fixture}/containers/"
    cp "${REPO_ROOT}/containers/environment.sh" "${fixture}/containers/"
    for group in common azure aws; do
        cp "${REPO_ROOT}/containers/docker-compose-${group}.yml" "${fixture}/containers/"
    done
    # Deliberately omit the CRT/generator to keep certificate stores and keytool untouched.
    printf 'fake-keystore\n' > "${fixture}/containers/certs/wiremock.jks"
    printf '%s\n' 'TOOLBOX_FUTURE_SETTING="${RABBITMQ_DEFAULT_USER}/literal#value"' >> "${fixture}/containers/.env.example"
    printf '%s' "$fixture"
}

assert_template_settings() {
    local fixture="$1" line key count
    while IFS= read -r line || [[ -n "$line" ]]; do
        line="${line%$'\r'}"
        if [[ "$line" =~ ^[[:space:]]*([A-Za-z_][A-Za-z0-9_]*)[[:space:]]*= ]]; then
            key="${BASH_REMATCH[1]}"
            count="$(grep -Ec "^[[:space:]]*(export[[:space:]]+)?${key}[[:space:]]*=" "${fixture}/containers/.env" || true)"
            [[ "$count" == 1 ]] || fail "Expected exactly one assignment for ${key}."
        fi
    done < "${fixture}/containers/.env.example"
}

mock_bin="${TEST_ROOT}/mock-bin"
mkdir -p "$mock_bin"
cat > "${mock_bin}/docker" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$TOOLBOX_TEST_RUNTIME_LOG"
if [[ "${1:-}" == image && "$*" == *--format* ]]; then echo 17.0.5035.5; exit 0; fi
if [[ "$1" == volume && "$2" == inspect && "${TOOLBOX_TEST_VOLUMES_EXIST:-false}" != true ]]; then exit 1; fi
if [[ "$1" == volume && "$2" == create && "${!#}" == "${TOOLBOX_TEST_FAILED_VOLUME:-}" ]]; then exit 23; fi
if [[ " $* " == *" config --services "* ]]; then
    if [[ -n "${TOOLBOX_TEST_INVALID_GROUP:-}" && " $* " == *" -p dev_${TOOLBOX_TEST_INVALID_GROUP}_shared "* ]]; then exit 17; fi
    printf '%s\n' mssql postgres azurite service-bus dynamodb rabbitmq aws-emulator aws-init
fi
MOCK
cat > "${mock_bin}/podman" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$TOOLBOX_TEST_RUNTIME_LOG"
if [[ "${1:-}" == image && "$*" == *--format* ]]; then echo 17.0.5035.5; exit 0; fi
if [[ "$1" == volume && "$2" == inspect && "${TOOLBOX_TEST_VOLUMES_EXIST:-false}" != true ]]; then exit 1; fi
if [[ "$1" == volume && "$2" == create && "${!#}" == "${TOOLBOX_TEST_FAILED_VOLUME:-}" ]]; then exit 23; fi
case "$*" in
    "version --format {{.Server.Version}}") printf '%s\n' "${TOOLBOX_TEST_PODMAN_VERSION:-6.0.2}" ;;
    "info --format {{.Host.ServiceIsRemote}}") printf '%s\n' "${TOOLBOX_TEST_PODMAN_REMOTE:-true}" ;;
    "info --format {{.Version.Version}}") printf '%s\n' "${TOOLBOX_TEST_PODMAN_HOST_VERSION:-6.0.2}" ;;
    "info --format {{.Host.RemoteSocket.Path}}") echo "Moto must not request a Podman API socket." >&2; exit 3 ;;
esac
if [[ " $* " == *" config --services "* ]]; then
    printf '%s\n' mssql postgres azurite service-bus dynamodb rabbitmq aws-emulator aws-init
fi
MOCK
chmod +x "${mock_bin}/docker" "${mock_bin}/podman"

run_setup() {
    local fixture="$1" runtime="${2:-docker}"
    : > "${fixture}/runtime.log"
    TOOLBOX_TEST_RUNTIME_LOG="${fixture}/runtime.log" PATH="${mock_bin}:${PATH}" \
        bash "${fixture}/docker_setup.sh" --runtime "$runtime" > "${fixture}/setup.log" 2>&1
}

create="$(new_fixture 'create with spaces')"
run_setup "$create" || fail "Creating .env failed."
assert_template_settings "$create"
grep -Ev '^(MSSQL_SA_PASSWORD|POSTGRES_PASSWORD|RABBITMQ_DEFAULT_PASS|SEQ_STORAGE_SECRETKEY|WIREMOCK_KEYSTORE_PASSWORD)=' "${create}/containers/.env.example" > "${create}/expected-settings"
grep -Ev '^(MSSQL_SA_PASSWORD|POSTGRES_PASSWORD|RABBITMQ_DEFAULT_PASS|SEQ_STORAGE_SECRETKEY|WIREMOCK_KEYSTORE_PASSWORD)=' "${create}/containers/.env" > "${create}/actual-settings"
cmp -s "${create}/expected-settings" "${create}/actual-settings" || fail "Creating .env changed template assignments."
for key in MSSQL_SA_PASSWORD POSTGRES_PASSWORD RABBITMQ_DEFAULT_PASS SEQ_STORAGE_SECRETKEY WIREMOCK_KEYSTORE_PASSWORD; do
    value="$(sed -n "s/^${key}=\"\([^\"]*\)\"\r\{0,1\}$/\1/p" "${create}/containers/.env")"
    [[ -n "$value" && "$value" != auto ]] || fail "A managed local credential remained blank or auto."
    ! grep -Fq "$value" "${create}/setup.log" || fail "Setup printed a generated credential."
done
grep -Fq 'config --services' "${create}/runtime.log" || fail "Setup did not resolve default services."
grep -Fq 'Moto SQS/SNS/EventBridge/S3' "${create}/setup.log" || fail "Setup did not report the free AWS emulator endpoint."
grep -Fq 'up --build -d' "${create}/runtime.log" || fail "Successful preflight did not reach Compose up."
for group in common azure aws; do
    expected="docker-compose-${group}.yml -p dev_${group}_shared"
    grep -Fq "$expected config --services" "${create}/runtime.log" || fail "Setup did not validate the ${group} group."
    grep -Fq "$expected up --build -d" "${create}/runtime.log" || fail "Setup did not build and start the ${group} group."
done
[[ "$(grep -c ' up --build -d$' "${create}/runtime.log")" == 3 ]] || fail 'Setup must start exactly three groups.'
! grep -Fq -- '--profile' "${create}/runtime.log" || fail "Default startup unexpectedly requires profiles."
for runtime in docker podman; do
    fresh_volumes="$(new_fixture "${runtime} fresh volumes")"
    run_setup "$fresh_volumes" "$runtime" || fail 'Fresh volume setup failed.'
    for group in azure aws; do
        if [[ "$group" == azure ]]; then volumes=(cosmos-data mssql-data azurite-data); else volumes=(postgres-data dynamodb-data rabbitmq-data); fi
        for volume in "${volumes[@]}"; do
            expected="volume create --driver local --label com.docker.compose.project=dev_${group}_shared --label com.docker.compose.volume=${volume} dev_common_shared_${volume}"
            grep -Fq "$expected" "${fresh_volumes}/runtime.log" || fail "Setup did not provision ${volume} with a cleanup label."
        done
    done
    [[ "$(grep -c '^volume create ' "${fresh_volumes}/runtime.log")" == 6 ]] || fail 'Setup must create six cloud data volumes.'
    last_create="$(grep -n '^volume create ' "${fresh_volumes}/runtime.log" | tail -1 | cut -d: -f1)"
    first_up="$(grep -n ' up --build -d$' "${fresh_volumes}/runtime.log" | head -1 | cut -d: -f1)"
    (( last_create < first_up )) || fail 'Volume provisioning happened after startup.'
    TOOLBOX_TEST_VOLUMES_EXIST=true run_setup "$fresh_volumes" "$runtime" || fail 'Retained volume setup failed.'
    ! grep -Eq '^volume (create|rm) ' "${fresh_volumes}/runtime.log" || fail 'Setup modified retained data volumes.'
    [[ "$(grep -c '^volume inspect ' "${fresh_volumes}/runtime.log")" == 6 ]] || fail 'Setup did not inspect all retained cloud volumes.'
    failed_volumes="$(new_fixture "${runtime} failed volume creation")"
    if TOOLBOX_TEST_FAILED_VOLUME=dev_common_shared_mssql-data run_setup "$failed_volumes" "$runtime"; then fail 'Setup ignored a volume creation failure.'; fi
    grep -Fq 'Unable to create shared data volume' "${failed_volumes}/setup.log" || fail 'Volume creation failure was unclear.'
    ! grep -Eq 'up --build -d|ps -a --filter' "${failed_volumes}/runtime.log" || fail 'Setup changed containers after volume provisioning failed.'
done
for group in azure aws; do
    invalid="$(new_fixture "invalid ${group} group")"
    if TOOLBOX_TEST_INVALID_GROUP="$group" run_setup "$invalid"; then fail 'Setup accepted an invalid group.'; fi
    grep -Fq "Unable to resolve the ${group} Compose services" "${invalid}/setup.log" || fail 'Invalid group error was unclear.'
    ! grep -Eq 'up --build -d|ps -a --filter' "${invalid}/runtime.log" || fail 'Setup must validate every group before migration or startup.'
done

migrate="$(new_fixture migrate)"
cat > "${migrate}/containers/.env" <<'ENV'
  export POSTGRES_PASSWORD = 'local;# $password'
ConnectionStrings__SqlServer="local custom SQL connection"
export Resources__Seq__ApiKey="fixture-file-secret-never-log"
WIREMOCK_KEYSTORE_PASSWORD="local-keystore-password"
TOOLBOX_CUSTOM_VALUE="$(this-must-not-execute)"
resources__s3__bucket="unrelated-case-value"
ENV
cp "${migrate}/containers/.env" "${migrate}/preserved.env"
run_setup "$migrate" || fail "Migrating .env failed."
assert_template_settings "$migrate"
while IFS= read -r assignment; do
    grep -Fxq "$assignment" "${migrate}/containers/.env" || fail "Migration overwrote a quoted local assignment."
done < "${migrate}/preserved.env"
grep -Fxq 'TOOLBOX_FUTURE_SETTING="${RABBITMQ_DEFAULT_USER}/literal#value"' \
    "${migrate}/containers/.env" || fail "Migration did not copy a future setting as raw data."
grep -Fxq 'Resources__S3__Bucket="toolbox-samples"' "${migrate}/containers/.env" || \
    fail "A differently cased variable prevented the canonical assignment from being added."
! grep -Fq 'fixture-file-secret-never-log' "${migrate}/setup.log" || fail "Setup printed a local API key."
cp "${migrate}/containers/.env" "${migrate}/first-migration.env"
run_setup "$migrate" || fail "Repeating setup failed."
cmp -s "${migrate}/first-migration.env" "${migrate}/containers/.env" || fail "A second setup changed migrated .env."

node_certificate="$(new_fixture 'custom Node certificate')"
mkdir -p "${node_certificate}/trusted-certs"
printf 'fixture certificate\n' > "${node_certificate}/trusted-certs/wiremock.crt"
printf 'fixture certificate\n' > "${node_certificate}/containers/certs/wiremock.crt"
# Redirect the operating-system trust directory into the disposable fixture.
sed "s|/usr/local/share/ca-certificates|${node_certificate}/trusted-certs|g" \
    "${node_certificate}/docker_setup.sh" > "${node_certificate}/setup-with-fixture-trust.sh"
printf '%s\n' 'WIREMOCK_KEYSTORE_PASSWORD="local-keystore-password"' \
    'NODE_EXTRA_CA_CERTS=""' 'export NODE_EXTRA_CA_CERTS = '\''/developer/custom-ca.crt'\''' \
    > "${node_certificate}/containers/.env"
mv "${node_certificate}/setup-with-fixture-trust.sh" "${node_certificate}/docker_setup.sh"
run_setup "$node_certificate" || fail 'Custom Node certificate setup failed.'
[[ "$(grep -E '^[[:space:]]*(export[[:space:]]+)?NODE_EXTRA_CA_CERTS[[:space:]]*=' \
    "${node_certificate}/containers/.env" | tail -1)" == "export NODE_EXTRA_CA_CERTS = '/developer/custom-ca.crt'" ]] || \
    fail 'Setup replaced the effective custom Node certificate path.'

# Compose preserves an unquoted leading hash as part of the value.
printf '%s\n' 'WIREMOCK_KEYSTORE_PASSWORD="local-keystore-password"' \
    'NODE_EXTRA_CA_CERTS=#developer-ca.crt' > "${node_certificate}/containers/.env"
run_setup "$node_certificate" || fail 'Leading-hash Node certificate setup failed.'
grep -Fxq 'NODE_EXTRA_CA_CERTS=#developer-ca.crt' "${node_certificate}/containers/.env" || \
    fail 'Setup replaced a leading-hash custom Node certificate path.'

legacy_endpoint="$(new_fixture 'legacy AWS default endpoint')"
printf '%s\n' 'export TOOLBOX_CONTAINER_AWS_ENDPOINT="http://localstack:4566"' > "${legacy_endpoint}/containers/.env"
run_setup "$legacy_endpoint" || fail "Legacy default endpoint setup failed."
[[ "$(grep -Ec '^[[:space:]]*(export[[:space:]]+)?TOOLBOX_CONTAINER_AWS_ENDPOINT[[:space:]]*=' "${legacy_endpoint}/containers/.env")" == 1 ]] || \
    fail "The endpoint migration introduced duplicate settings."
grep -Fq 'http://aws-emulator:5000' "${legacy_endpoint}/containers/.env" || fail "The old default AWS endpoint was not migrated."
! grep -Fq 'http://localstack:4566' "${legacy_endpoint}/containers/.env" || fail "The old default AWS endpoint remains active."

custom_endpoint="$(new_fixture 'custom AWS endpoint')"
printf '%s\n' 'export TOOLBOX_CONTAINER_AWS_ENDPOINT="http://localhost:5500"' > "${custom_endpoint}/containers/.env"
run_setup "$custom_endpoint" || fail "Custom endpoint setup failed."
grep -Fxq 'export TOOLBOX_CONTAINER_AWS_ENDPOINT="http://localhost:5500"' "${custom_endpoint}/containers/.env" || \
    fail "The default endpoint migration overwrote a developer's explicit custom endpoint."

podman="$(new_fixture 'podman without licensing or socket')"
run_setup "$podman" podman || fail "Free AWS emulation did not reach Podman startup."
grep -Fq 'up --build -d' "${podman}/runtime.log" || fail "Podman did not start the selected services."
for group in common azure aws; do
    grep -Fq "docker-compose-${group}.yml -p dev_${group}_shared up --build -d" "${podman}/runtime.log" || fail "Podman did not start the ${group} group."
done
! grep -Fq 'RemoteSocket.Path' "${podman}/runtime.log" || fail "AWS emulation still required a runtime socket."
TOOLBOX_TEST_PODMAN_VERSION="<no value>" TOOLBOX_TEST_PODMAN_REMOTE=false \
    run_setup "$podman" podman || fail "Native Podman setup failed."
grep -Fq 'info --format {{.Version.Version}}' "${podman}/runtime.log" || fail "Native Podman did not check its engine version."

for server_version in 5.7.1 invalid; do
    unsupported="$(new_fixture "server ${server_version}")"
    if TOOLBOX_TEST_PODMAN_VERSION="$server_version" \
        run_setup "$unsupported" podman; then
        fail "Setup accepted unsupported Podman server version ${server_version}."
    fi
    grep -Fq 'Podman 6 or later' "${unsupported}/setup.log" || \
        grep -Fq 'Podman server 6 or later' "${unsupported}/setup.log" || fail "Unsupported server error was unclear."
    ! grep -Fq 'up --build -d' "${unsupported}/runtime.log" || fail "Setup started services with an unsupported server."
    ! grep -Fq 'info --format {{.Version.Version}}' "${unsupported}/runtime.log" || fail "Remote Podman fell back to the client version."
done

for runtime in docker podman; do
    teardown="$(new_fixture "${runtime} teardown")"
    : > "${teardown}/runtime.log"
    TOOLBOX_TEST_RUNTIME_LOG="${teardown}/runtime.log" PATH="${mock_bin}:${PATH}" \
        bash "${teardown}/docker_down.sh" --runtime "$runtime" > "${teardown}/down.log" 2>&1 || fail 'Teardown failed.'
    for group in common azure aws; do
        grep -Fq "docker-compose-${group}.yml -p dev_${group}_shared down --remove-orphans" "${teardown}/runtime.log" || fail "Teardown did not stop the ${group} group."
        for label in com.docker.compose.project io.podman.compose.project; do
            grep -Fq "label=${label}=dev_${group}_shared" "${teardown}/runtime.log" || fail "Teardown did not clean up the ${group} group."
        done
    done
    ! grep -Eq 'volume rm|--volumes' "${teardown}/runtime.log" || fail 'Normal teardown must retain data volumes.'
done
echo 'Bash setup/teardown tests passed: three groups, additive configuration, generated credentials, free AWS emulation, and Podman engine versions.'
