#!/bin/bash
#
# Generate a self-signed certificate and PKCS12 keystore for WireMock HTTPS support.
#
# This script creates a self-signed certificate and PKCS12 keystore for local
# WireMock HTTPS development using OpenSSL. It also exports the public certificate (.crt) for client trust.
#
# Prerequisites:
#   - OpenSSL must be installed and available in PATH
#   - Most Linux distributions include OpenSSL by default
#   - macOS: typically included, or install via Homebrew: brew install openssl
#
# Usage:
#   ./generate-wiremock-cert.sh                    # Use default password "changeit"
#   ./generate-wiremock-cert.sh -p mypassword      # Use custom password
#   ./generate-wiremock-cert.sh -p mypassword -f   # Force overwrite existing files
#
# After generation, update your .env file with:
#   WIREMOCK_KEYSTORE_PASSWORD=<your-password>
#

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;90m'
NC='\033[0m' # No Color

# Default values
KEYSTORE_PASSWORD="changeit"
VALIDITY_DAYS=3650
FORCE=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -p|--password)
            KEYSTORE_PASSWORD="$2"
            shift 2
            ;;
        -v|--validity)
            VALIDITY_DAYS="$2"
            shift 2
            ;;
        -f|--force)
            FORCE=true
            shift
            ;;
        -h|--help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -p, --password PASSWORD  Keystore password (default: changeit)"
            echo "  -v, --validity DAYS      Certificate validity in days (default: 3650)"
            echo "  -f, --force              Overwrite existing files without prompting"
            echo "  -h, --help               Show this help message"
            exit 0
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            echo "Use --help for usage information."
            exit 1
            ;;
    esac
done

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Output paths
KEYSTORE_PATH="${SCRIPT_DIR}/wiremock.pfx"
PRIVATE_KEY_PATH="${SCRIPT_DIR}/wiremock.key"
CERT_PATH="${SCRIPT_DIR}/wiremock.crt"
CONFIG_PATH="${SCRIPT_DIR}/wiremock.conf"

# Check for existing files
if [[ "$FORCE" == false ]] && [[ -f "$KEYSTORE_PATH" ]]; then
    read -p "Certificate files already exist in this directory. Overwrite? (y/N) " response
    if [[ ! "$response" =~ ^[Yy]$ ]]; then
        echo -e "${YELLOW}Aborted. Use -f or --force to overwrite without prompting.${NC}"
        exit 0
    fi
fi

# Find openssl
if ! command -v openssl &> /dev/null; then
    echo ""
    echo -e "${RED}========================================${NC}"
    echo -e "${RED}  OpenSSL Not Found${NC}"
    echo -e "${RED}========================================${NC}"
    echo ""
    echo -e "${YELLOW}OpenSSL is required but not installed on this system.${NC}"
    echo ""
    echo -e "${CYAN}Installation instructions:${NC}"
    echo ""
    echo -e "${CYAN}Linux (Debian/Ubuntu):${NC}"
    echo -e "${GRAY}  sudo apt update && sudo apt install openssl${NC}"
    echo ""
    echo -e "${CYAN}Linux (Fedora/RHEL):${NC}"
    echo -e "${GRAY}  sudo dnf install openssl${NC}"
    echo ""
    echo -e "${CYAN}macOS (Homebrew):${NC}"
    echo -e "${GRAY}  brew install openssl${NC}"
    echo ""
    echo -e "${CYAN}Linux (Alpine):${NC}"
    echo -e "${GRAY}  apk add openssl${NC}"
    echo ""
    echo -e "${RED}========================================${NC}"
    echo ""
    exit 1
fi

echo -e "${CYAN}Using OpenSSL: $(command -v openssl)${NC}"

# Remove existing files if present
if [[ -f "$PRIVATE_KEY_PATH" ]]; then
    rm -f "$PRIVATE_KEY_PATH"
    echo -e "${GRAY}Removed existing private key.${NC}"
fi
if [[ -f "$KEYSTORE_PATH" ]]; then
    rm -f "$KEYSTORE_PATH"
    echo -e "${GRAY}Removed existing keystore.${NC}"
fi
if [[ -f "$CERT_PATH" ]]; then
    rm -f "$CERT_PATH"
    echo -e "${GRAY}Removed existing certificate.${NC}"
fi
if [[ -f "$CONFIG_PATH" ]]; then
    rm -f "$CONFIG_PATH"
fi

echo ""
echo -e "${CYAN}Generating self-signed certificate with OpenSSL...${NC}"

# Create OpenSSL config for SAN (Subject Alternative Names)
cat > "$CONFIG_PATH" << 'EOF'
[req]
default_bits = 2048
prompt = no
default_md = sha256
distinguished_name = dn
req_extensions = v3_req

[dn]
CN=localhost
OU=Development
O=Local
L=Local
ST=UT
C=US

[v3_req]
subjectAltName = DNS:localhost,DNS:wiremock,DNS:host.docker.internal,IP:127.0.0.1
EOF

# Generate private key and self-signed certificate in one step
openssl req -new -x509 \
    -newkey rsa:2048 \
    -keyout "$PRIVATE_KEY_PATH" \
    -out "$CERT_PATH" \
    -days "$VALIDITY_DAYS" \
    -nodes \
    -config "$CONFIG_PATH" \
    -extensions v3_req

if [[ $? -ne 0 ]]; then
    echo -e "${RED}Error: Failed to generate certificate.${NC}"
    exit 1
fi

echo -e "${GREEN}Certificate created: ${CERT_PATH}${NC}"
echo -e "${GREEN}Private key created: ${PRIVATE_KEY_PATH}${NC}"

# Create PKCS12 keystore from certificate and key
echo ""
echo -e "${CYAN}Creating PKCS12 keystore...${NC}"

openssl pkcs12 -export \
    -in "$CERT_PATH" \
    -inkey "$PRIVATE_KEY_PATH" \
    -out "$KEYSTORE_PATH" \
    -name wiremock \
    -passout "pass:${KEYSTORE_PASSWORD}"

if [[ $? -ne 0 ]]; then
    echo -e "${RED}Error: Failed to create PKCS12 keystore.${NC}"
    exit 1
fi

echo -e "${GREEN}Keystore created: ${KEYSTORE_PATH}${NC}"

# Convert PKCS12 to JKS for better compatibility with WireMock
# This is done using keytool if available (usually in Java or Docker containers)
JKS_PATH="${CERTS_DIR}/wiremock.jks"

# Try to use keytool from a Docker image if local keytool is not available
if command -v keytool &> /dev/null; then
    echo ""
    echo -e "${CYAN}Converting PKCS12 to JKS format for WireMock compatibility...${NC}"
    keytool -importkeystore \
        -srckeystore "$KEYSTORE_PATH" \
        -srcstoretype PKCS12 \
        -srcstorepass "$KEYSTORE_PASSWORD" \
        -destkeystore "$JKS_PATH" \
        -deststoretype JKS \
        -deststorepass "$KEYSTORE_PASSWORD" \
        -noprompt 2>&1 | grep -v "^Warning:" || true

    if [[ $? -eq 0 ]]; then
        echo -e "${GREEN}JKS keystore created: ${JKS_PATH}${NC}"
    fi
elif command -v docker &> /dev/null; then
    echo ""
    echo -e "${CYAN}Converting PKCS12 to JKS format using Docker keytool...${NC}"
    docker run --rm -v "$(pwd):/certs" wiremock/wiremock:latest keytool \
        -importkeystore \
        -srckeystore /certs/wiremock.pfx \
        -srcstoretype PKCS12 \
        -srcstorepass "$KEYSTORE_PASSWORD" \
        -destkeystore /certs/wiremock.jks \
        -deststoretype JKS \
        -deststorepass "$KEYSTORE_PASSWORD" \
        -noprompt 2>&1 | grep -v "^Warning:" || true

    if [[ $? -eq 0 ]]; then
        echo -e "${GREEN}JKS keystore created: ${JKS_PATH}${NC}"
    fi
else
    echo -e "${YELLOW}Note: keytool not found. JKS conversion skipped.${NC}"
    echo -e "${YELLOW}To create JKS keystore manually, run:${NC}"
    echo -e "  keytool -importkeystore -srckeystore ${KEYSTORE_PATH} -srcstoretype PKCS12 -destkeystore ${JKS_PATH} -deststoretype JKS"
fi

# Summary
echo ""
echo -e "${CYAN}========================================${NC}"
echo -e "${CYAN}  WireMock HTTPS Certificate Generated${NC}"
echo -e "${CYAN}========================================${NC}"
echo ""
echo -e "Files created:"
echo -e "${GRAY}  Certificate: ${CERT_PATH}${NC}"
echo -e "${GRAY}  Private key: ${PRIVATE_KEY_PATH}${NC}"
echo -e "${GRAY}  Keystore:    ${KEYSTORE_PATH}${NC}"
if [[ -f "$JKS_PATH" ]]; then
    echo -e "${GRAY}  JKS Keystore: ${JKS_PATH}${NC}"
fi
echo ""
echo -e "${YELLOW}Keystore password: ${KEYSTORE_PASSWORD}${NC}"
echo ""
echo -e "Next steps:"
echo -e "${GRAY}  1. Add to your .env file:${NC}"
echo -e "     WIREMOCK_KEYSTORE_PASSWORD=${KEYSTORE_PASSWORD}"
echo ""
echo -e "${GRAY}  2. Start WireMock:${NC}"
echo -e "     docker compose up wiremock"
echo ""
echo -e "${GRAY}  3. Test HTTPS endpoint:${NC}"
echo -e "     curl -k https://localhost:10443/__admin/health"
echo ""
echo -e "Client trust options:"
echo -e "${GRAY}  - .NET: Import wiremock.crt or configure HttpClientHandler${NC}"
echo -e "${GRAY}  - Java: keytool -importcert -file wiremock.crt -keystore truststore.jks${NC}"
echo -e "${GRAY}  - curl: curl --cacert wiremock.crt https://localhost:10443/...${NC}"
echo -e "${GRAY}  - Linux: sudo cp wiremock.crt /usr/local/share/ca-certificates/ && sudo update-ca-certificates${NC}"
echo -e "${GRAY}  - Postman: Settings > Certificates > Add CA Certificate${NC}"
echo ""
