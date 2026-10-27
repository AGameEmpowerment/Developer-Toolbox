#!/bin/bash
# Local development only. Realm semantics and URI validation belong to the caller.
set +x
set -euo pipefail
export LC_ALL=C
umask 077

fail() {
    printf '%s\n' "TOOLBOX_OIDC_E_$1" >&2
    exit 78
}

# The native diagnostic needs neither credentials nor writable provider data.
if [[ $# == 1 && $1 == --version ]]; then
    exec /opt/keycloak/bin/kc.sh "$@"
fi

[[ $# -gt 0 && ( $1 == start-dev || $1 == start ) ]] || fail COMMAND
has_import=false
for argument in "$@"; do
    case "$argument" in
        --import-realm) has_import=true ;;
        # CLI options must not bypass the environment checked below.
        --hostname*|--config-file*|-cf|-cf=*|--http-relative-path*) fail COMMAND ;;
        --import-realm=*) fail COMMAND ;;
    esac
done
[[ $has_import == true ]] || fail COMMAND

# Only the URI shape is checked here, without another runtime parser. The owner
# validates URI semantics/routing and verifies the exact discovery issuer.
valid_origin() {
    local origin=$1 authority host port label
    [[ $origin =~ ^https?://([^/\?#]+)$ ]] || return 1
    authority=${BASH_REMATCH[1]}
    if [[ $authority =~ ^\[([0-9A-Fa-f:]+)\](:([0-9]+))?$ ]]; then
        host=${BASH_REMATCH[1]}
        port=${BASH_REMATCH[3]}
        [[ $host == *:* ]] || return 1
    elif [[ $authority =~ ^([A-Za-z0-9.-]+)(:([0-9]+))?$ ]]; then
        host=${BASH_REMATCH[1]}
        port=${BASH_REMATCH[3]}
        [[ ${#host} -le 253 && $host != .* && $host != *. && $host != *..* ]] || return 1
        local -a labels
        IFS=. read -r -a labels <<< "$host"
        for label in "${labels[@]}"; do
            [[ ${#label} -le 63 && $label != -* && $label != *- ]] || return 1
        done
    else
        return 1
    fi
    if [[ -n $port ]]; then
        [[ ${#port} -le 5 && $port != 0* ]] || return 1
        (( 10#$port >= 1 && 10#$port <= 65535 )) || return 1
    fi
}

valid_origin "${KC_HOSTNAME:-}" || fail HOSTNAME
export KC_HOSTNAME_STRICT=true

profile=${TOOLBOX_OIDC_PROFILE:-default}
case "$profile" in
    default)
        [[ -z ${TOOLBOX_OIDC_REALM:-} || $TOOLBOX_OIDC_REALM == toolbox-local ]] || fail REALM
        export TOOLBOX_OIDC_REALM=toolbox-local
        for secret_name in TOOLBOX_OIDC_ADMIN_PASSWORD TOOLBOX_OIDC_USER_PASSWORD TOOLBOX_OIDC_CLIENT_SECRET; do
            [[ ${!secret_name:-} =~ ^[A-Za-z0-9_-]{24,128}$ ]] || fail SECRET
        done
        callback=${TOOLBOX_OIDC_CALLBACK_URI:-}
        # Reject wildcard/fragment and JSON or environment-placeholder injection.
        [[ $callback =~ ^(https?://[^/]+)(/[^[:space:]\"\'\\\$\{\}\*\#]*)?$ ]] || fail CALLBACK
        valid_origin "${BASH_REMATCH[1]}" || fail CALLBACK
        ;;
    project)
        [[ ${TOOLBOX_OIDC_REALM:-} =~ ^[A-Za-z0-9_-]{1,128}$ ]] || fail REALM
        ;;
    *) fail PROFILE ;;
esac

import_directory=/opt/keycloak/data/import
# Reject symlinked ancestors as well as the final file. Do not follow a path out
# of this container's owned native import storage.
for directory in /opt /opt/keycloak /opt/keycloak/data "$import_directory"; do
    [[ ! -L $directory ]] || fail IMPORT_DIRECTORY
done
if [[ $profile == default && ! -e $import_directory ]]; then
    mkdir -m 700 "$import_directory" 2>/dev/null || fail WRITE
fi
[[ -d $import_directory && -r $import_directory && -x $import_directory ]] || fail IMPORT_DIRECTORY
shopt -s nullglob dotglob
entries=("$import_directory"/*)
import_file=$import_directory/$TOOLBOX_OIDC_REALM-realm.json
if [[ $profile == default ]]; then
    [[ ${#entries[@]} == 0 ]] || fail IMPORT_COUNT
    chmod 700 "$import_directory" 2>/dev/null || fail WRITE
    # Noclobber avoids overwriting an import created after the directory check.
    (set -o noclobber; cat /opt/toolbox-oidc/default-realm.template.json > "$import_file") 2>/dev/null || fail WRITE
else
    [[ ${#entries[@]} == 1 ]] || fail IMPORT_COUNT
    [[ ${entries[0]} == "$import_file" ]] || fail IMPORT
fi
[[ ! -L $import_file && -f $import_file && -r $import_file ]] || fail IMPORT

# Replace the wrapper so the native process receives container termination.
exec /opt/keycloak/bin/kc.sh "$@"
