# WireMock TLS Certificates

This directory contains certificates and keystores for WireMock HTTPS support.

## Quick Start

### Windows (PowerShell)

```powershell
# From repository root - handles everything automatically
.\docker_setup.ps1
```

### Linux/WSL/Devcontainer (Bash)

```bash
# From repository root - handles everything automatically
./docker_setup.sh
```

### macOS

```bash
# From repository root - generates certificates but does NOT auto-import to system trust
./docker_setup.sh

# After running docker_setup.sh, manually trust the certificate (see Manual Trust section below)
sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain ./containers/certs/wiremock.crt
```

The setup scripts will:

1. Generate the keystore and certificate if not present
2. Create `.env` from `.env.example` with a random password
3. Import the certificate to the system trust store (Windows and Linux only; macOS requires manual import - see above)
4. Start all Docker containers

### Manual Certificate Generation

If you need to regenerate certificates manually:

**Windows (PowerShell):**

```powershell
.\Generate-WireMockCert.ps1 -Force -KeystorePassword "mypassword"
```

**Linux/macOS/WSL (Bash):**

```bash
./generate-wiremock-cert.sh -f -p "mypassword"
```

### Test the HTTPS endpoint

```bash
# Skip certificate validation for quick test
curl -k https://localhost:10443/__admin/health

# With certificate trust (after setup script imports it)
curl https://localhost:10443/__admin/health
```

## Files

| File                        | Description                                                          |
| --------------------------- | -------------------------------------------------------------------- |
| `Generate-WireMockCert.ps1` | PowerShell script to generate keystore and certificate (Windows)     |
| `generate-wiremock-cert.sh` | Bash script to generate keystore and certificate (Linux/macOS/WSL)   |
| `wiremock.jks`              | Java KeyStore containing the private key and certificate (generated) |
| `wiremock.crt`              | Public certificate in PEM format for client trust (generated)        |

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

#### Windows

When you run `docker_setup.ps1`, the WireMock certificate is **automatically imported and approved** on your local Windows machine in the `CurrentUser\Root` certificate store. This means:

- ✅ .NET `HttpClient` will trust the certificate automatically
- ✅ Browsers (Edge, Chrome) will trust the certificate
- ✅ PowerShell `Invoke-WebRequest`/`Invoke-RestMethod` will work without `-SkipCertificateCheck`
- ✅ curl on Windows will trust the certificate

When you run `docker_down.ps1 -CleanCerts` or `-CleanAll`, the certificate is automatically removed from the trust store.

#### Linux/macOS/WSL/Devcontainer

When you run `docker_setup.sh`, the WireMock certificate is **automatically imported and approved** on your local Linux machine in the system CA trust store:

- ✅ Debian/Ubuntu: `/usr/local/share/ca-certificates/`
- ✅ RHEL/CentOS/Fedora: `/etc/pki/ca-trust/source/anchors/`
- ✅ Alpine: `/usr/local/share/ca-certificates/`

This means:

- ✅ .NET `HttpClient` will trust the certificate automatically
- ✅ curl will trust the certificate
- ✅ Node.js (with `NODE_EXTRA_CA_CERTS` set in `.env`)
- ✅ Python requests library

When you run `docker_down.sh --clean-certs` or `--clean-all`, the certificate is automatically removed from the trust store.

**No additional configuration is needed for most applications.**

### Manual Trust (If Automatic Import Fails)

#### Windows (PowerShell)

Run the following commands from the **repository root**:

```powershell
# Import to CurrentUser (no admin required)
Import-Certificate -FilePath .\containers\certs\wiremock.crt -CertStoreLocation Cert:\CurrentUser\Root

# Or import to LocalMachine (requires admin, trusts for all users)
Import-Certificate -FilePath .\containers\certs\wiremock.crt -CertStoreLocation Cert:\LocalMachine\Root
```

#### Linux (Debian/Ubuntu)

Run the following commands from the **repository root**:

```bash
sudo cp ./containers/certs/wiremock.crt /usr/local/share/ca-certificates/
sudo update-ca-certificates
```

#### Linux (RHEL/CentOS/Fedora)

Run the following commands from the **repository root**:

```bash
sudo cp ./containers/certs/wiremock.crt /etc/pki/ca-trust/source/anchors/
sudo update-ca-trust
```

#### macOS

Run the following command from the **repository root**:

```bash
sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain ./containers/certs/wiremock.crt
```

### .NET Applications

With the certificate in the system trust store, standard `HttpClient` works without any special configuration:

```csharp
// This just works after docker_setup.ps1/docker_setup.sh imports the certificate
var client = new HttpClient();
var response = await client.GetAsync("https://localhost:10443/__admin/health");
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

The setup script automatically adds `NODE_EXTRA_CA_CERTS` to your `.env` file. For manual configuration:

```bash
export NODE_EXTRA_CA_CERTS=/path/to/containers/certs/wiremock.crt
```

Or in your code:

```javascript
const https = require("https");
const fs = require("fs");

const agent = new https.Agent({
    ca: fs.readFileSync("./certs/wiremock.crt"),
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

**Windows:**

```powershell
.\docker_down.ps1 -CleanCerts
.\docker_setup.ps1
```

**Linux/macOS/WSL:**

```bash
./docker_down.sh --clean-certs
./docker_setup.sh
```

Or manually with a custom password:

**Windows:**

```powershell
.\Generate-WireMockCert.ps1 -Force -KeystorePassword "newpassword"
```

**Linux/macOS/WSL:**

```bash
./generate-wiremock-cert.sh -f -p "newpassword"
```

Remember to update `WIREMOCK_KEYSTORE_PASSWORD` in your `.env` file if regenerating manually.

## Ports

| Port  | Protocol | Description                                     |
| ----- | -------- | ----------------------------------------------- |
| 10080 | HTTP     | WireMock HTTP endpoint (backward compatibility) |
| 10443 | HTTPS    | WireMock HTTPS endpoint with TLS                |

## Troubleshooting

### "PKIX path building failed" (Java)

The client doesn't trust the WireMock certificate. Import `wiremock.crt` into your Java truststore.

### "SSL certificate problem" (curl)

Use `--cacert ./certs/wiremock.crt` or `-k` to skip verification (dev only).

### Container fails to start

1. Ensure `wiremock.jks` exists:
    - Windows: Run `.\docker_setup.ps1` or `.\Generate-WireMockCert.ps1`
    - Linux: Run `./docker_setup.sh` or `./generate-wiremock-cert.sh`
2. Verify `WIREMOCK_KEYSTORE_PASSWORD` in `.env` matches the keystore password
3. Check Docker logs: `docker logs Wiremock`

### keytool not found

**Windows:** Install free Microsoft OpenJDK with `winget install Microsoft.OpenJDK.25` or set `JAVA_HOME` environment variable.

**Linux (Debian/Ubuntu):**

```bash
sudo apt install default-jdk
```

**Linux (Fedora/RHEL):**

```bash
sudo dnf install java-latest-openjdk-devel
```

**Devcontainer:** The devcontainer.json includes Java 21 feature - rebuild the container if keytool is missing.
