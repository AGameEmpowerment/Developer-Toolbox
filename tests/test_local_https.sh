#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
temp_root="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
fixture="$(mktemp -d "$temp_root/toolbox-local-https.XXXXXXXX")"
cleanup() {
    case "$fixture" in "$temp_root"/toolbox-local-https.*) rm -rf -- "$fixture" ;; esac
}
trap cleanup EXIT
fail() { printf '%s\n' "$*" >&2; exit 1; }

mkdir -p "$fixture/containers/certs" "$fixture/.devcontainer"
certs="$fixture/containers/certs"
cp "$repo_root/containers/certs/generate-wiremock-cert.sh" "$repo_root/containers/certs/local-https.sh" "$certs/"
cp "$repo_root/.devcontainer/ensure_image.sh" "$fixture/.devcontainer/"

export TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD='fixture-key-password'
export TOOLBOX_LOCAL_CA_DIR="$fixture/shared authority"
bash "$certs/generate-wiremock-cert.sh" --local-ca -f
bash "$certs/local-https.sh" trust --output-dir "$fixture/trust-one"
cp "$fixture/trust-one/root-ca.crt" "$fixture/expected-root.crt"
export TOOLBOX_LOCAL_HTTPS_PFX_PASSWORD='fixture-server-password'
bash "$certs/local-https.sh" issue --name app --host app.test --output-dir "$fixture/server"
openssl pkcs12 -in "$fixture/server/app.pfx" -noout -passin env:TOOLBOX_LOCAL_HTTPS_PFX_PASSWORD
bash "$certs/local-https.sh" trust --output-dir "$fixture/trust-two"

cmp "$fixture/expected-root.crt" "$fixture/trust-two/root-ca.crt"
cmp "$fixture/expected-root.crt" "$certs/wiremock-ca.crt"
[[ "$(find "$fixture/trust-two" -type f | wc -l)" -eq 1 ]] || fail 'Trust directory included an unexpected file.'
if bash "$certs/local-https.sh" trust --output-dir "$fixture/trust-one"; then
    fail 'Existing per-run trust was overwritten.'
fi
openssl verify -CAfile "$fixture/trust-one/root-ca.crt" -purpose sslserver -verify_hostname wiremock "$certs/wiremock.crt"
openssl verify -CAfile "$fixture/trust-one/root-ca.crt" -purpose sslserver -verify_ip ::1 "$certs/wiremock.crt"
openssl verify -CAfile "$fixture/trust-two/root-ca.crt" -purpose sslserver -verify_hostname app.test "$fixture/server/app.crt"
if openssl verify -CAfile "$fixture/trust-two/root-ca.crt" -verify_hostname different.test "$fixture/server/app.crt"; then
    fail 'An incorrect TLS host was accepted.'
fi

bash "$certs/local-https.sh" initialize --ca-dir "$fixture/independent-ca"
if openssl verify -CAfile "$fixture/independent-ca/root-ca.crt" "$certs/wiremock.crt"; then
    fail 'An unrelated root certificate accepted WireMock.'
fi
keytool -list -v -alias wiremock -storetype JKS -keystore "$certs/wiremock.jks" \
    -storepass:env TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD > "$fixture/keystore.txt" 2>/dev/null
grep -q '^Certificate chain length: 2[[:space:]]*$' "$fixture/keystore.txt" || fail 'WireMock JKS did not include the full chain.'
keytool -exportcert -rfc -alias wiremock -storetype JKS -keystore "$certs/wiremock.jks" \
    -storepass:env TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD > "$fixture/wiremock-jks.crt" 2>/dev/null
openssl verify -CAfile "$fixture/trust-one/root-ca.crt" -purpose sslserver -verify_hostname localhost "$fixture/wiremock-jks.crt"

printf '%s\n' 'Shared local CA, isolated trust, and WireMock JKS checks passed.'
