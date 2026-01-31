# WireMock TLS Certificates

This directory contains certificates and keystores for WireMock HTTPS support.

## Quick Start

1. **Generate the keystore** (requires Java JDK installed):

   ```powershell
   .\Generate-WireMockCert.ps1
   ```

2. **Add password to .env file** (in the `containers` directory):

   ```env
   WIREMOCK_KEYSTORE_PASSWORD=changeit
   ```

3. **Start WireMock**:

   ```powershell
   docker compose up wiremock
   ```

4. **Test the HTTPS endpoint**:

   ```powershell
   # Skip certificate validation for quick test
   curl -k https://localhost:10443/__admin/health

   # Or with the certificate
   curl --cacert ./certs/wiremock.crt https://localhost:10443/__admin/health
   ```

## Files

| File | Description |
|------|-------------|
| `Generate-WireMockCert.ps1` | PowerShell script to generate keystore and certificate |
| `wiremock.jks` | Java KeyStore containing the private key and certificate (generated) |
| `wiremock.crt` | Public certificate in PEM format for client trust (generated) |

## Certificate Details

The generated certificate includes:

- **CN (Common Name)**: `localhost`
- **SANs (Subject Alternative Names)**:
  - `dns:localhost`
  - `dns:wiremock`
  - `dns:host.docker.internal`
  - `ip:127.0.0.1`
- **Validity**: 10 years (default)
- **Key Algorithm**: RSA 2048-bit

## Client Trust Configuration

### Automatic Trust (Recommended)

When you run `docker_setup.ps1`, the WireMock certificate is **automatically imported** into your Windows `CurrentUser\Root` certificate store. This means:

- ✅ .NET `HttpClient` will trust the certificate automatically
- ✅ Browsers (Edge, Chrome) will trust the certificate
- ✅ PowerShell `Invoke-WebRequest`/`Invoke-RestMethod` will work without `-SkipCertificateCheck`
- ✅ curl on Windows will trust the certificate

**No additional configuration is needed for most applications.**

When you run `docker_down.ps1 -CleanCerts` or `-CleanAll`, the certificate is automatically removed from the trust store.

### Manual Trust (If Automatic Import Fails)

If the automatic import fails (e.g., due to permissions), you can manually import:

```powershell
# Import to CurrentUser (no admin required)
Import-Certificate -FilePath .\certs\wiremock.crt -CertStoreLocation Cert:\CurrentUser\Root

# Or import to LocalMachine (requires admin, trusts for all users)
Import-Certificate -FilePath .\certs\wiremock.crt -CertStoreLocation Cert:\LocalMachine\Root
```

### .NET Applications

With the certificate in the Windows trust store, standard `HttpClient` works without any special configuration:

```csharp
// This just works after docker_setup.ps1 imports the certificate
var client = new HttpClient();
var response = await client.GetAsync(\"https://localhost:10443/__admin/health\");
```

**Development-only bypass (not recommended):**

```csharp
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback =
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};
var client = new HttpClient(handler);
```

### Java Applications

```bash
keytool -importcert \
  -alias wiremock \
  -file wiremock.crt \
  -keystore truststore.jks \
  -storepass changeit \
  -noprompt
```

Then configure your application to use the truststore:

```bash
java -Djavax.net.ssl.trustStore=truststore.jks \
     -Djavax.net.ssl.trustStorePassword=changeit \
     -jar your-app.jar
```

### Node.js

```javascript
const https = require('https');
const fs = require('fs');

const agent = new https.Agent({
  ca: fs.readFileSync('./certs/wiremock.crt')
});

// Use agent in fetch/axios/http requests
```

### curl

```bash
curl --cacert ./certs/wiremock.crt https://localhost:10443/__admin/health
```

### Postman

1. Go to **Settings** → **Certificates**
2. Under **CA Certificates**, click **Add**
3. Select `wiremock.crt`

## Regenerating Certificates

To regenerate certificates (e.g., after expiry or changing password):

```powershell
.\Generate-WireMockCert.ps1 -Force -KeystorePassword "newpassword"
```

Remember to update `WIREMOCK_KEYSTORE_PASSWORD` in your `.env` file.

## Ports

| Port | Protocol | Description |
|------|----------|-------------|
| 10080 | HTTP | WireMock HTTP endpoint (backward compatibility) |
| 10443 | HTTPS | WireMock HTTPS endpoint with TLS |

## Troubleshooting

### "PKIX path building failed" (Java)

The client doesn't trust the WireMock certificate. Import `wiremock.crt` into your Java truststore.

### "SSL certificate problem" (curl)

Use `--cacert ./certs/wiremock.crt` or `-k` to skip verification (dev only).

### Container fails to start

1. Ensure `wiremock.jks` exists (run `Generate-WireMockCert.ps1`)
2. Verify `WIREMOCK_KEYSTORE_PASSWORD` matches the keystore password
3. Check Docker logs: `docker logs Wiremock`

### keytool not found

Install Java JDK from [Adoptium](https://adoptium.net/) or set `JAVA_HOME` environment variable.
