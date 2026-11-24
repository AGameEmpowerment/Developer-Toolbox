#!/usr/bin/env bash
# Local development only. Source this file; never source a developer's dotenv file.
resolve_wiremock_password() (
    set -euo pipefail
    local environment_file="$1" runtime="$2" temporary ownership='' path password compose_file
    temporary="$(umask 077; mktemp -d)"
    trap 'rm -rf "$temporary"' EXIT
    path="$temporary"
    case "$(uname -s)" in
        MINGW*|MSYS*|CYGWIN*)
            path="$(cygpath -m "$temporary")"
            environment_file="$(cygpath -m "$environment_file")"
            ;;
        *)
            if [[ "$runtime" == docker ]]; then ownership=",\"user\":\"$(id -u):$(id -g)\""; fi
            ;;
    esac
    compose_file="$path/compose.json"
    if [[ "$runtime" == podman ]]; then ownership=',"userns_mode":"keep-id"'; fi
    path="${path//\\/\\\\}"
    path="${path//\"/\\\"}"
    source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.devcontainer" && pwd)/ensure_image.sh"
    ensure_toolbox_image "$runtime" wiremock/wiremock:latest || return 1
    printf '%s\n' '{"x-podman":{"in_pod":false},"services":{"password":{"image":"wiremock/wiremock:latest","pull_policy":"never","network_mode":"none","environment":{"TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD":"${WIREMOCK_KEYSTORE_PASSWORD:-changeit}"},"entrypoint":["/bin/sh","-c"],"command":["umask 077; printf %s \"$${TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD}\" > /toolbox-secret/value"],"volumes":["'"$path"':/toolbox-secret:rw,z"]'"$ownership"'}}}' > "$temporary/compose.json"
    if ! MSYS_NO_PATHCONV=1 "$runtime" compose --env-file "$environment_file" \
        -f "$compose_file" -p "toolbox-cert-$(basename "$temporary" | tr '[:upper:].' '[:lower:]-')" \
        run --rm -T password >/dev/null 2>&1; then
        echo 'Unable to resolve the WireMock password with the selected Compose provider.' >&2
        return 1
    fi
    [[ -f "$temporary/value" ]] || { echo 'The certificate utility did not produce its private value.' >&2; return 1; }
    IFS= read -r -d '' password < "$temporary/value" || true
    printf '%s' "$password"
)

initialize_toolbox_environment() {
    TOOLBOX_GENERATED_WIREMOCK_PASSWORD=false
    local environment_file="$1" key line raw effective replacement temporary
    local keys=(MSSQL_SA_PASSWORD POSTGRES_PASSWORD RABBITMQ_DEFAULT_PASS
        SEQ_STORAGE_SECRETKEY WIREMOCK_KEYSTORE_PASSWORD TOOLBOX_CONTAINER_AWS_ENDPOINT)
    [[ -f "$environment_file" ]] || { echo 'The toolbox environment file must exist.' >&2; return 1; }
    for key in "${keys[@]}"; do
        effective=missing
        while IFS= read -r line || [[ -n "$line" ]]; do
            if [[ "$line" =~ ^[[:space:]]*(export[[:space:]]+)?${key}[[:space:]]*=(.*)$ ]]; then
                raw="${BASH_REMATCH[2]}"
                raw="${raw#"${raw%%[![:space:]]*}"}"
                raw="${raw%"${raw##*[![:space:]]}"}"
                if [[ "$key" == TOOLBOX_CONTAINER_AWS_ENDPOINT ]]; then
                    if [[ "$raw" =~ ^(\"http://localstack:4566\"|\'http://localstack:4566\')[[:space:]]*(#.*)?$ || "$raw" =~ ^http://localstack:4566([[:space:]]+#.*|[[:space:]]*)$ ]]; then effective=legacy; else effective=explicit; fi
                elif [[ "$raw" =~ ^(\"auto\"|\'auto\')[[:space:]]*(#.*)?$ || "$raw" =~ ^auto([[:space:]]+#.*|[[:space:]]*)$ ]]; then
                    effective=auto
                elif [[ "$raw" =~ ^(\"\"|\'\')?[[:space:]]*(#.*)?$ ]]; then
                    effective=blank
                else effective=explicit; fi
            fi
        done < "$environment_file"
        if [[ "$effective" != auto && "$effective" != legacy ]]; then continue; fi
        if [[ "$effective" == legacy ]]; then
            replacement='http://aws-emulator:5000'
        elif [[ "$key" == SEQ_STORAGE_SECRETKEY ]]; then
            replacement="$(dd if=/dev/urandom bs=32 count=1 2>/dev/null | base64 | tr -d '\r\n')" || return 1
            [[ ${#replacement} -eq 44 ]] || { echo 'Failed to generate a local key.' >&2; return 1; }
        else
            replacement="$(od -An -tx1 -N32 /dev/urandom | tr -d ' \r\n')" || return 1
            [[ ${#replacement} -eq 64 ]] || { echo 'Failed to generate a local password.' >&2; return 1; }
            replacement="Tb!1${replacement}"
        fi
        temporary="$(umask 077; mktemp "${environment_file}.generated.XXXXXX")" || return 1
        if ! (
            while IFS= read -r line || [[ -n "$line" ]]; do
                if [[ "$line" =~ ^[[:space:]]*(export[[:space:]]+)?${key}[[:space:]]*= ]]; then
                    printf '%s="%s"\n' "$key" "$replacement"
                else printf '%s\n' "$line"; fi
            done < "$environment_file"
        ) > "$temporary"; then rm -f "$temporary"; return 1; fi
        if ! mv -f "$temporary" "$environment_file"; then rm -f "$temporary"; return 1; fi
        if [[ "$key" == WIREMOCK_KEYSTORE_PASSWORD ]]; then TOOLBOX_GENERATED_WIREMOCK_PASSWORD=true; fi
        printf '%s initialized in containers/.env.\n' "$key"
    done
}
