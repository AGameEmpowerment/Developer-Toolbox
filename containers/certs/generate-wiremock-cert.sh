#!/bin/bash
#
# Generate a JKS keystore and export the certificate for WireMock HTTPS support.
#
# This script creates a Java KeyStore (JKS) with a self-signed certificate for local
# WireMock HTTPS development. It also exports the public certificate (.crt) for client trust.
#
# Prerequisites:
#   - Free OpenJDK must be installed and keytool must be available in PATH
#   - Alternatively, set JAVA_HOME environment variable
#
# Usage:
#   ./generate-wiremock-cert.sh                    # Use default password "changeit"
#   ./generate-wiremock-cert.sh -p mypassword      # Use custom password
#   ./generate-wiremock-cert.sh -p mypassword -f   # Force overwrite existing files
#
# After generation, update your .env file with:
#   WIREMOCK_KEYSTORE_PASSWORD=<your-password>
#

if [ -z "${BASH_VERSION:-}" ]; then
    echo "Missing dependency: Bash 3.2 or later is required. Run this script with bash, not sh." >&2
    exit 1
fi

if (( BASH_VERSINFO[0] < 3 || (BASH_VERSINFO[0] == 3 && BASH_VERSINFO[1] < 2) )); then
    echo "Missing dependency: Bash 3.2 or later is required. Current version: ${BASH_VERSION}." >&2
    exit 1
fi

set -euo pipefail

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
VERBOSE=false

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
        --verbose)
            VERBOSE=true
            shift
            ;;
        -h|--help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -p, --password PASSWORD  Keystore password (default: changeit)"
            echo "  -v, --validity DAYS      Certificate validity in days (default: 3650)"
            echo "  -f, --force              Overwrite existing files without prompting"
            echo "  --verbose                Print the keystore password to stdout (use with caution)"
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
KEYSTORE_PATH="${SCRIPT_DIR}/wiremock.jks"
CERT_PATH="${SCRIPT_DIR}/wiremock.crt"
WIREMOCK_HTTPS_PORT="10443"

# Check for existing files
if [[ "$FORCE" == false ]] && [[ -f "$KEYSTORE_PATH" ]]; then
    read -p "Keystore '$KEYSTORE_PATH' already exists. Overwrite? (y/N) " response
    if [[ ! "$response" =~ ^[Yy]$ ]]; then
        echo -e "${YELLOW}Aborted. Use -f or --force to overwrite without prompting.${NC}"
        exit 0
    fi
fi

# Find keytool
KEYTOOL=""

# Check PATH first
if command -v keytool &> /dev/null; then
    KEYTOOL="keytool"
fi

# Check JAVA_HOME if not in PATH
if [[ -z "$KEYTOOL" ]] && [[ -n "$JAVA_HOME" ]]; then
    if [[ -f "${JAVA_HOME}/bin/keytool" ]]; then
        KEYTOOL="${JAVA_HOME}/bin/keytool"
    fi
fi

# Check common installation paths on Linux
if [[ -z "$KEYTOOL" ]]; then
    COMMON_PATHS=(
        "/usr/lib/jvm/*/bin/keytool"
        "/usr/java/*/bin/keytool"
        "/opt/java/*/bin/keytool"
        "/opt/jdk*/bin/keytool"
    )

    for pattern in "${COMMON_PATHS[@]}"; do
        found="$(compgen -G "$pattern" 2>/dev/null || true)"
        found="${found%%$'\n'*}"
        if [[ -n "$found" ]] && [[ -f "$found" ]]; then
            KEYTOOL="$found"
            break
        fi
    done
fi

if [[ -z "$KEYTOOL" ]]; then
    echo -e "${RED}Missing dependency: Java JDK keytool was not found.${NC}"
    echo "Please ensure free OpenJDK is installed and one of the following:"
    echo "  1. Add Java bin directory to PATH"
    echo "  2. Set JAVA_HOME environment variable"
    echo "  3. Install free OpenJDK on Debian/Ubuntu: sudo apt install default-jdk"
    echo "     Fedora/RHEL alternative: sudo dnf install java-latest-openjdk-devel"
    exit 1
fi

echo -e "${CYAN}Using keytool: ${KEYTOOL}${NC}"

# Remove existing files if present
if [[ -f "$KEYSTORE_PATH" ]]; then
    rm -f "$KEYSTORE_PATH"
    echo -e "${GRAY}Removed existing keystore.${NC}"
fi
if [[ -f "$CERT_PATH" ]]; then
    rm -f "$CERT_PATH"
    echo -e "${GRAY}Removed existing certificate.${NC}"
fi

echo ""
echo -e "${CYAN}Generating JKS keystore with self-signed certificate...${NC}"

# Generate keystore with certificate including SANs for localhost
"$KEYTOOL" -genkeypair \
    -alias wiremock \
    -keyalg RSA \
    -keysize 2048 \
    -validity "$VALIDITY_DAYS" \
    -keystore "$KEYSTORE_PATH" \
    -storetype JKS \
    -storepass "$KEYSTORE_PASSWORD" \
    -keypass "$KEYSTORE_PASSWORD" \
    -dname "CN=localhost, OU=Development, O=Local, L=Local, ST=UT, C=US" \
    -ext "SAN=dns:localhost,dns:wiremock,dns:host.docker.internal,ip:127.0.0.1"

echo -e "${GREEN}Keystore created: ${KEYSTORE_PATH}${NC}"

# Export certificate for client trust
echo ""
echo -e "${CYAN}Exporting public certificate...${NC}"

"$KEYTOOL" -exportcert \
    -alias wiremock \
    -keystore "$KEYSTORE_PATH" \
    -storepass "$KEYSTORE_PASSWORD" \
    -rfc \
    -file "$CERT_PATH"

echo -e "${GREEN}Certificate exported: ${CERT_PATH}${NC}"

# Summary
echo ""
echo -e "${CYAN}========================================${NC}"
echo -e "${CYAN}  WireMock HTTPS Certificate Generated${NC}"
echo -e "${CYAN}========================================${NC}"
echo ""
echo -e "Files created:"
echo -e "${GRAY}  Keystore:    ${KEYSTORE_PATH}${NC}"
echo -e "${GRAY}  Certificate: ${CERT_PATH}${NC}"
echo ""
if [[ "$VERBOSE" == "true" ]]; then
    echo -e "${YELLOW}Keystore password: ${KEYSTORE_PASSWORD}${NC}"
    echo ""
fi
echo -e "Next steps:"
echo -e "${GRAY}  1. Add to your .env file:${NC}"
if [[ "$VERBOSE" == "true" ]]; then
    echo -e "     WIREMOCK_KEYSTORE_PASSWORD=${KEYSTORE_PASSWORD}"
else
    echo -e "     WIREMOCK_KEYSTORE_PASSWORD=<password-from-generation-output>"
fi
echo ""
echo -e "${GRAY}  2. Start WireMock:${NC}"
echo -e "     docker compose up wiremock"
echo ""
echo -e "${GRAY}  3. Test HTTPS endpoint:${NC}"
echo -e "     curl -k https://localhost:${WIREMOCK_HTTPS_PORT}/__admin/health"
echo ""
echo -e "Client trust options:"
echo -e "${GRAY}  - .NET: Trust wiremock.crt via the OS certificate store (for example, Windows 'Trusted Root Certification Authorities') or configure HttpClientHandler to trust this certificate${NC}"
echo -e "${GRAY}  - Java: keytool -importcert -file wiremock.crt -keystore truststore.jks${NC}"
echo -e "${GRAY}  - curl: curl --cacert wiremock.crt https://localhost:${WIREMOCK_HTTPS_PORT}/...${NC}"
echo -e "${GRAY}  - Linux: sudo cp wiremock.crt /usr/local/share/ca-certificates/ && sudo update-ca-certificates${NC}"
echo ""
