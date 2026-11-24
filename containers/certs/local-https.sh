#!/usr/bin/env bash
# Local development only. See readme.md for examples and trust configuration.
set -euo pipefail
umask 077

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fail() { printf '%s\n' "$*" >&2; exit 1; }

action="${1:---help}"
[[ $# -eq 0 ]] || shift
ca_dir="${TOOLBOX_LOCAL_CA_DIR:-${script_dir}/../.local-ca}"
output_dir=''
name=localhost
days=365
runtime="${CONTAINER_RUNTIME:-auto}"
hosts=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        --ca-dir|--output-dir|--name|--days|--runtime|--host)
            [[ $# -ge 2 && -n "$2" ]] || fail "$1 requires a value."
            case "$1" in
                --ca-dir) ca_dir="$2" ;;
                --output-dir) output_dir="$2" ;;
                --name) name="$2" ;;
                --days) days="$2" ;;
                --runtime) runtime="$2" ;;
                --host) hosts+=("$2") ;;
            esac
            shift 2 ;;
        *) fail "Unknown option: $1" ;;
    esac
done

case "$action" in
    initialize) ;;
    issue|trust) [[ -n "$output_dir" ]] || fail "$action requires --output-dir." ;;
    -h|--help)
        printf '%s\n' 'Usage: local-https.sh initialize|issue|trust [--ca-dir DIR] [--runtime ENGINE]' \
            '  issue --output-dir DIR [--name NAME] [--host DNS_NAME (repeatable)] [--days DAYS]' \
            '  trust --output-dir DIR'
        exit 0 ;;
    *) fail "Unknown action: $action" ;;
esac
case "$runtime" in auto|docker|podman) ;; *) fail 'Invalid container runtime.' ;; esac
[[ "$name" =~ ^[A-Za-z0-9][A-Za-z0-9_-]{0,62}$ ]] || fail 'Certificate name must be a simple file name.'
[[ "$days" =~ ^[1-9][0-9]{0,2}$ ]] || fail 'Certificate validity must be between 1 and 999 days.'

san='DNS:localhost,IP:127.0.0.1,IP:::1'
for host in "${hosts[@]+"${hosts[@]}"}"; do
    [[ "$host" =~ ^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$ ]] ||
        fail "Invalid local DNS name: $host"
    case "$host" in
        localhost) ;;
        *.localhost|*.test|*.local|*.internal) san="$san,DNS:$host" ;;
        *.*) fail "Unsupported local DNS suffix: $host" ;;
        *) san="$san,DNS:$host" ;;
    esac
done

case "$action" in
    trust) [[ -f "$ca_dir/root-ca.crt" ]] || fail "Initialize the local CA first: $ca_dir" ;;
    *) mkdir -p -- "$ca_dir" ;;
esac
ca_dir="$(cd "$ca_dir" && pwd -P)"
if [[ -n "$output_dir" ]]; then
    mkdir -p -- "$output_dir"
    output_dir="$(cd "$output_dir" && pwd -P)"
fi

if [[ "$action" == trust ]]; then
    # Exclusive creation preserves the identity of trust files already used by a run.
    (set -o noclobber; cat "$ca_dir/root-ca.crt" > "$output_dir/root-ca.crt") ||
        fail "Could not write a new trust file in $output_dir."
    printf '%s\n' "$output_dir/root-ca.crt"
    exit 0
fi

if ! command -v openssl >/dev/null 2>&1; then
    if [[ "$runtime" == auto ]]; then
        for candidate in docker podman; do
            if command -v "$candidate" >/dev/null 2>&1 && "$candidate" info >/dev/null 2>&1; then
                runtime="$candidate"
                break
            fi
        done
    fi
    [[ "$runtime" != auto ]] && command -v "$runtime" >/dev/null 2>&1 ||
        fail 'Certificate generation requires OpenSSL or a reachable Docker/Podman engine.'
    source "$script_dir/../../.devcontainer/ensure_image.sh"
    ensure_toolbox_image "$runtime" wiremock/wiremock:latest
    mount_mode=rw
    script_mount_mode=ro
    identity=()
    platform="$(uname -s)"
    case "$platform" in
        MINGW*|MSYS*|CYGWIN*)
            script_dir="$(cygpath -m "$script_dir")"
            ca_dir="$(cygpath -m "$ca_dir")"
            [[ -z "$output_dir" ]] || output_dir="$(cygpath -m "$output_dir")" ;;
    esac
    if [[ "$runtime" == podman ]]; then
        mount_mode=rw,z
        script_mount_mode=ro,z
        identity=(--userns keep-id)
    elif [[ "$platform" != MINGW* && "$platform" != MSYS* && "$platform" != CYGWIN* && "$platform" != Darwin* ]]; then
        identity=(--user "$(id -u):$(id -g)")
    fi
    mounts=(--volume "$script_dir/local-https.sh:/tmp/local-https.sh:$script_mount_mode"
        --volume "$ca_dir:/tmp/local-ca:$mount_mode")
    args=("$action" --ca-dir /tmp/local-ca --name "$name" --days "$days")
    if [[ -n "$output_dir" ]]; then
        mounts+=(--volume "$output_dir:/tmp/local-output:$mount_mode")
        args+=(--output-dir /tmp/local-output)
    fi
    for host in "${hosts[@]+"${hosts[@]}"}"; do args+=(--host "$host"); done
    MSYS_NO_PATHCONV=1 "$runtime" run --rm --network none --entrypoint bash \
        --env TOOLBOX_LOCAL_HTTPS_PFX_PASSWORD "${identity[@]+"${identity[@]}"}" "${mounts[@]}" \
        wiremock/wiremock:latest /tmp/local-https.sh "${args[@]}"
    exit 0
fi

stage=''
lock=''
cleanup() {
    if [[ -n "$stage" ]]; then
        rm -f -- "$stage/root-ca.key" "$stage/root-ca.crt" "$stage/server.key" "$stage/server.csr" \
            "$stage/server.crt" "$stage/server.pfx" "$stage/extensions.cnf"
        rmdir -- "$stage"
    fi
    [[ -z "$lock" ]] || rmdir -- "$lock"
}
trap cleanup EXIT

if [[ ! -f "$ca_dir/root-ca.crt" || ! -f "$ca_dir/root-ca.key" ]]; then
    mkdir -- "$ca_dir/.initializing" 2>/dev/null ||
        fail "The local CA is being initialized or a previous run was interrupted: $ca_dir"
    lock="$ca_dir/.initializing"
    if [[ ! -f "$ca_dir/root-ca.crt" || ! -f "$ca_dir/root-ca.key" ]]; then
        [[ ! -e "$ca_dir/root-ca.crt" && ! -e "$ca_dir/root-ca.key" ]] ||
            fail "The local CA is incomplete. Restore its two files or use a new CA directory: $ca_dir"
        stage="$(mktemp -d "$ca_dir/.issue.XXXXXXXX")"
        printf '%s\n' '[req]' 'distinguished_name = dn' '[dn]' '[ca]' \
            'basicConstraints = critical,CA:true,pathlen:0' \
            'keyUsage = critical,keyCertSign,cRLSign' 'subjectKeyIdentifier = hash' > "$stage/extensions.cnf"
        MSYS2_ARG_CONV_EXCL='/CN=' openssl req -new -x509 -newkey rsa:3072 -nodes -sha256 -days 3650 \
            -config "$stage/extensions.cnf" -extensions ca -subj '/CN=Toolbox Local Development CA' \
            -keyout "$stage/root-ca.key" -out "$stage/root-ca.crt" 2>/dev/null ||
            fail 'OpenSSL could not create the local CA.'
        mv -- "$stage/root-ca.key" "$ca_dir/root-ca.key"
        mv -- "$stage/root-ca.crt" "$ca_dir/root-ca.crt"
        cleanup
        lock=''
        stage=''
    fi
fi

if [[ "$action" == initialize ]]; then
    printf '%s\n' "$ca_dir/root-ca.crt"
    exit 0
fi

openssl x509 -checkend "$((days * 86400))" -noout -in "$ca_dir/root-ca.crt" >/dev/null ||
    fail 'The local CA expires before the requested server certificate. Shorten the validity or use a new CA directory.'
mkdir -- "$output_dir/.$name.issuing" 2>/dev/null ||
    fail "An issuance is in progress or a previous run was interrupted for $name."
lock="$output_dir/.$name.issuing"
for extension in key crt pfx; do
    file="$output_dir/$name.$extension"
    [[ ! -e "$file" && ! -L "$file" ]] || fail "Choose a fresh output directory or name: $file"
done
stage="$(mktemp -d "$output_dir/.issue.XXXXXXXX")"
printf '%s\n' '[req]' 'distinguished_name = dn' '[dn]' '[server]' \
    'basicConstraints = critical,CA:false' 'keyUsage = critical,digitalSignature,keyEncipherment' \
    'extendedKeyUsage = serverAuth' "subjectAltName = $san" > "$stage/extensions.cnf"
MSYS2_ARG_CONV_EXCL='/CN=' openssl req -new -newkey rsa:2048 -nodes -sha256 -subj '/CN=localhost' -config "$stage/extensions.cnf" \
    -keyout "$stage/server.key" -out "$stage/server.csr" 2>/dev/null ||
    fail 'OpenSSL could not create the local server key and request.'
# Random serials allow independent parallel issuances without a shared counter file.
if ! signing_output="$(openssl x509 -req -in "$stage/server.csr" -CA "$ca_dir/root-ca.crt" -CAkey "$ca_dir/root-ca.key" \
    -set_serial "0x$(openssl rand -hex 16)" -sha256 -days "$days" -extfile "$stage/extensions.cnf" -extensions server \
    -out "$stage/server.crt" 2>&1)"; then
    fail "$signing_output"
fi
openssl verify -CAfile "$ca_dir/root-ca.crt" -purpose sslserver "$stage/server.crt" >/dev/null

if [[ -n "${TOOLBOX_LOCAL_HTTPS_PFX_PASSWORD:-}" ]]; then
    openssl pkcs12 -export -name "$name" -inkey "$stage/server.key" -in "$stage/server.crt" \
        -certfile "$ca_dir/root-ca.crt" -out "$stage/server.pfx" -passout env:TOOLBOX_LOCAL_HTTPS_PFX_PASSWORD
    mv -f -- "$stage/server.pfx" "$output_dir/$name.pfx"
fi
mv -f -- "$stage/server.key" "$output_dir/$name.key"
mv -f -- "$stage/server.crt" "$output_dir/$name.crt"
printf '%s\n' "$output_dir/$name.crt"
