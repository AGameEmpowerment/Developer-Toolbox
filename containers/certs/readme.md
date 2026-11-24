# Local HTTPS certificates

Docker setup creates a local development CA and a WireMock server certificate. The CA stays in `containers/.local-ca/`. WireMock reads `containers/certs/wiremock.jks`, which contains its server key and certificate chain.

Use the root certificate for a client that connects to WireMock on port 10443.

```bash
curl --cacert containers/.local-ca/root-ca.crt https://localhost:10443/__admin/health
```

On Windows, Schannel curl requires best-effort revocation checking for the local CA, which has no revocation endpoint. Chain and hostname checks still apply.

```powershell
curl.exe --ssl-revoke-best-effort --cacert containers/.local-ca/root-ca.crt https://localhost:10443/__admin/health
```

Keep the root key on the development machine, outside application bind mounts and version control.

## Issue a certificate for another localhost server

The following commands initialize the CA if needed. The certificate covers `localhost`, `127.0.0.1`, and `::1`. Add local DNS aliases with `-DnsName` or repeated `--host` options.

```powershell
$run = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
.\containers\certs\New-LocalHttpsCertificate.ps1 -Action Issue -Name app -DnsName app.test -OutputDirectory "$run/server"
.\containers\certs\New-LocalHttpsCertificate.ps1 -Action Trust -OutputDirectory "$run/trust"
```

```bash
run="$(mktemp -d)"
bash containers/certs/local-https.sh issue --name app --host app.test --output-dir "$run/server"
bash containers/certs/local-https.sh trust --output-dir "$run/trust"
```

Configure the server with `server/app.crt` and `server/app.key`. Set `TOOLBOX_LOCAL_HTTPS_PFX_PASSWORD` in the issuing process to also export `server/app.pfx`. The issuance commands accept `Initialize` and `initialize` to create the CA separately. They can use OpenSSL from the existing WireMock image when host tools are unavailable.

## Isolate client trust

The trust command copies the public CA certificate to `trust/root-ca.crt` in the run directory. It leaves the operating system's certificate stores unchanged. Configure the client for that invocation.

| Client | Per-run configuration |
| --- | --- |
| curl | `--cacert /path/to/trust/root-ca.crt` |
| Node.js | Set `NODE_EXTRA_CA_CERTS` before starting Node.js. |
| Python requests | Pass the CA file as `verify` or set `REQUESTS_CA_BUNDLE`. |
| Java | Import the public CA certificate into a disposable Java truststore and set `javax.net.ssl.trustStore` for the JVM. |
| .NET | Configure the client handler with an `X509ChainPolicy` using `CustomRootTrust` and the CA certificate in `CustomTrustStore`. |

Use a distinct output directory for each run. Delete it when the server and clients are finished. For a run that needs its own CA identity too, set `TOOLBOX_LOCAL_CA_DIR` to a directory inside that run before issuing certificates and creating trust files.

## Renew WireMock's server certificate

WireMock certificates signed by the local CA have a default lifetime of 365 days. Use the same keystore password as `WIREMOCK_KEYSTORE_PASSWORD` in `containers/.env` and restart WireMock after renewing.

```powershell
.\containers\certs\Generate-WireMockCert.ps1 -UseLocalCa -Force
```

```bash
bash containers/certs/generate-wiremock-cert.sh --local-ca -f
```

Provide the keystore password to the generator through `TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD` when it differs from the generator's standalone default. Existing users of the shared CA can keep their trust file when renewing the server certificate. If you create a new CA, reissue the server certificates and create fresh client trust files.
