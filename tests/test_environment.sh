#!/usr/bin/env bash
set -euo pipefail
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${repository_root}/containers/environment.sh"
test_directory="$(mktemp -d)"
trap 'rm -rf "$test_directory"' EXIT
environment_file="${test_directory}/.env"
cat > "$environment_file" <<'ENV'
MSSQL_SA_PASSWORD="auto"
POSTGRES_PASSWORD='auto' # generated
export RABBITMQ_DEFAULT_PASS = auto
SEQ_STORAGE_SECRETKEY="auto"
ConnectionStrings__Postgres="Password=${POSTGRES_PASSWORD}"
TOOLBOX_CUSTOM="$(do-not-run)"
postgres_password="auto"
ENV
initialize_toolbox_environment "$environment_file" > "${test_directory}/output"
for key in MSSQL_SA_PASSWORD POSTGRES_PASSWORD RABBITMQ_DEFAULT_PASS; do
    grep -Eq "^${key}=\"Tb!1[a-f0-9]{64}\"$" "$environment_file"
done
for key in SEQ_STORAGE_SECRETKEY; do
    grep -Eq "^${key}=\"[A-Za-z0-9+/]{43}=\"$" "$environment_file"
done
grep -Fq 'ConnectionStrings__Postgres="Password=${POSTGRES_PASSWORD}"' "$environment_file"
grep -Fq 'TOOLBOX_CUSTOM="$(do-not-run)"' "$environment_file"
grep -Fq 'postgres_password="auto"' "$environment_file"
cp "$environment_file" "${test_directory}/saved"
initialize_toolbox_environment "$environment_file" > /dev/null
cmp "$environment_file" "${test_directory}/saved"
while IFS= read -r line; do
    value="${line#*=}"; value="${value#\"}"; value="${value%\"}"
    if [[ "$line" == MSSQL_* || "$line" == POSTGRES_* || "$line" == RABBITMQ_* || "$line" == SEQ_* ]]; then
        ! grep -Fq "$value" "${test_directory}/output"
    fi
done < "$environment_file"
printf 'POSTGRES_PASSWORD=auto\nexport POSTGRES_PASSWORD = '\''developer custom password'\''\nSEQ_STORAGE_SECRETKEY="developer-key"\nMSSQL_SA_PASSWORD="AUTO"' > "$environment_file"
cp "$environment_file" "${test_directory}/explicit"
initialize_toolbox_environment "$environment_file" > /dev/null
cmp "$environment_file" "${test_directory}/explicit"
printf '%s\n' 'POSTGRES_PASSWORD=auto#developer-password' 'MSSQL_SA_PASSWORD=auto#not-a-comment' \
    'RABBITMQ_DEFAULT_PASS=auto#literal' 'WIREMOCK_KEYSTORE_PASSWORD=auto#local-keystore' > "$environment_file"
cp "$environment_file" "${test_directory}/literal-hashes"
initialize_toolbox_environment "$environment_file" > /dev/null
cmp "$environment_file" "${test_directory}/literal-hashes"
[[ "$TOOLBOX_GENERATED_WIREMOCK_PASSWORD" == false ]]
sed 's/=auto#/=#/' "${test_directory}/literal-hashes" > "$environment_file"
cp "$environment_file" "${test_directory}/leading-hashes"
initialize_toolbox_environment "$environment_file" > /dev/null
cmp "$environment_file" "${test_directory}/leading-hashes"
[[ "$TOOLBOX_GENERATED_WIREMOCK_PASSWORD" == false ]]
printf 'SEQ_STORAGE_SECRETKEY="auto"' > "${test_directory}/other.env"
initialize_toolbox_environment "${test_directory}/other.env" > /dev/null
! grep -Fq "$(cat "${test_directory}/other.env")" "${test_directory}/saved"
[[ -z "$(find "$test_directory" -name '*.generated.*')" ]]
if [[ "$(uname)" != MINGW* && "$(uname)" != MSYS* ]]; then
    permission="$(stat -c '%a' "${test_directory}/other.env" 2>/dev/null || stat -f '%Lp' "${test_directory}/other.env")"
    [[ "$permission" == 600 ]]
fi
echo 'Environment generation tests passed.'
