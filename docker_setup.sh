#!/bin/bash
# Setup Docker Services (Linux/Unix/WSL/Devcontainer)

set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;90m'
NC='\033[0m' # No Color

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONTAINERS_DIR="${SCRIPT_DIR}/containers"
CERTS_DIR="${CONTAINERS_DIR}/certs"
ENV_FILE="${CONTAINERS_DIR}/.env"
ENV_EXAMPLE_FILE="${CONTAINERS_DIR}/.env.example"

generate_random_password() {
    local password=""

    # `tr | head` can trip `pipefail` because `tr` exits on SIGPIPE after `head` finishes.
    set +o pipefail
    password="$(tr -dc 'A-Za-z0-9' </dev/urandom | head -c 24)"
    set -o pipefail

    if [[ ${#password} -ne 24 ]]; then
        echo -e "${RED}Error: failed to generate WireMock keystore password.${NC}" >&2
        exit 1
    fi

    printf '%s' "$password"
}

#region Environment Setup
echo -e "${CYAN}=== Environment Setup ===${NC}"

# Track if .env was just created to detect potential keystore password mismatch
ENV_FILE_JUST_CREATED=false

# Create .env from .env.example if it doesn't exist
if [[ ! -f "$ENV_FILE" ]]; then
    if [[ -f "$ENV_EXAMPLE_FILE" ]]; then
        echo -e "${YELLOW}Creating .env from .env.example...${NC}"
        cp "$ENV_EXAMPLE_FILE" "$ENV_FILE"
        echo -e "${GREEN}.env file created.${NC}"
        ENV_FILE_JUST_CREATED=true
    else
        echo -e "${RED}Error: .env.example not found. Cannot create .env file.${NC}"
        exit 1
    fi
fi

# Note: .env is consumed by docker compose; this script does not execute it
# to avoid running arbitrary configuration as shell code.
#endregion

#region WireMock Certificate Setup
echo -e "\n${CYAN}=== WireMock Certificate Setup ===${NC}"

WIREMOCK_KEYSTORE="${CERTS_DIR}/wiremock.jks"
GENERATE_CERT_SCRIPT="${CERTS_DIR}/generate-wiremock-cert.sh"

if [[ ! -f "$WIREMOCK_KEYSTORE" ]]; then
    if [[ -f "$GENERATE_CERT_SCRIPT" ]]; then
        echo -e "${YELLOW}Generating WireMock TLS certificate...${NC}"

        # Generate a random password for the keystore
        KEYSTORE_PASSWORD="$(generate_random_password)"

        # Run the certificate generation script
        bash "$GENERATE_CERT_SCRIPT" -p "$KEYSTORE_PASSWORD" -f

        if [[ -f "$WIREMOCK_KEYSTORE" ]]; then
            # Update .env with the generated password
            if grep -q 'WIREMOCK_KEYSTORE_PASSWORD=' "$ENV_FILE"; then
                # Use portable in-place edit (works on both GNU and BSD/macOS sed)
                if [[ "$(uname)" == "Darwin" ]]; then
                    sed -i '' "s/WIREMOCK_KEYSTORE_PASSWORD=\"[^\"]*\"/WIREMOCK_KEYSTORE_PASSWORD=\"${KEYSTORE_PASSWORD}\"/" "$ENV_FILE"
                else
                    sed -i "s/WIREMOCK_KEYSTORE_PASSWORD=\"[^\"]*\"/WIREMOCK_KEYSTORE_PASSWORD=\"${KEYSTORE_PASSWORD}\"/" "$ENV_FILE"
                fi
            else
                echo "WIREMOCK_KEYSTORE_PASSWORD=\"${KEYSTORE_PASSWORD}\"" >> "$ENV_FILE"
            fi
            echo -e "${GREEN}WireMock certificate generated and .env updated.${NC}"
        else
            echo -e "${YELLOW}Warning: WireMock certificate generation failed. HTTPS may not work.${NC}"
            echo -e "${YELLOW}You can manually run: ${GENERATE_CERT_SCRIPT}${NC}"
        fi
    else
        echo -e "${YELLOW}Warning: WireMock certificate script not found at: ${GENERATE_CERT_SCRIPT}${NC}"
    fi
else
    # Keystore exists - check for potential password mismatch
    if [[ "$ENV_FILE_JUST_CREATED" == "true" ]]; then
        echo -e "${YELLOW}WireMock keystore already exists, but .env was just created from .env.example.${NC}"
        echo -e "${YELLOW}This may cause a password mismatch. Regenerating keystore to match .env password...${NC}"

        if [[ -f "$GENERATE_CERT_SCRIPT" ]]; then
            # Read the password from the newly created .env file
            ENV_PASSWORD=$(grep 'WIREMOCK_KEYSTORE_PASSWORD=' "$ENV_FILE" | sed 's/WIREMOCK_KEYSTORE_PASSWORD="\?\([^"]*\)"\?/\1/')
            if [[ -z "$ENV_PASSWORD" ]]; then
                ENV_PASSWORD="changeit"  # Default from .env.example
            fi

            # Regenerate keystore with the .env password
            bash "$GENERATE_CERT_SCRIPT" -p "$ENV_PASSWORD" -f

            if [[ -f "$WIREMOCK_KEYSTORE" ]]; then
                echo -e "${GREEN}WireMock keystore regenerated to match .env password.${NC}"
            else
                echo -e "${YELLOW}Warning: Failed to regenerate WireMock keystore. HTTPS may not work.${NC}"
                echo -e "${YELLOW}You can manually run: ${GENERATE_CERT_SCRIPT} -p \"${ENV_PASSWORD}\" -f${NC}"
            fi
        else
            echo -e "${YELLOW}Warning: WireMock certificate script not found. Cannot regenerate keystore.${NC}"
            echo -e "${YELLOW}Manual action needed: Either regenerate .env or regenerate the keystore.${NC}"
        fi
    else
        echo -e "${GRAY}WireMock keystore already exists. Skipping certificate generation.${NC}"
    fi
fi

# Import certificate to system trust store
WIREMOCK_CERT="${CERTS_DIR}/wiremock.crt"
if [[ -f "$WIREMOCK_CERT" ]]; then
    echo -e "${YELLOW}Importing WireMock certificate to system trust store...${NC}"

    # Detect OS and import certificate accordingly
    if [[ -d "/usr/local/share/ca-certificates" ]]; then
        # Debian/Ubuntu
        CERT_DEST="/usr/local/share/ca-certificates/wiremock.crt"
        if [[ -f "$CERT_DEST" ]]; then
            echo -e "${GRAY}Certificate already installed in system trust store.${NC}"
        else
            if [[ $EUID -eq 0 ]]; then
                cp "$WIREMOCK_CERT" "$CERT_DEST"
                update-ca-certificates
                echo -e "${GREEN}Certificate imported to system trust store.${NC}"
            else
                # Try with sudo
                if command -v sudo &> /dev/null; then
                    sudo cp "$WIREMOCK_CERT" "$CERT_DEST"
                    sudo update-ca-certificates
                    echo -e "${GREEN}Certificate imported to system trust store.${NC}"
                else
                    echo -e "${YELLOW}Warning: Cannot import certificate without root privileges.${NC}"
                    echo -e "${GRAY}Manual import: sudo cp '$WIREMOCK_CERT' /usr/local/share/ca-certificates/ && sudo update-ca-certificates${NC}"
                fi
            fi
        fi
    elif [[ -d "/etc/pki/ca-trust/source/anchors" ]]; then
        # RHEL/CentOS/Fedora
        CERT_DEST="/etc/pki/ca-trust/source/anchors/wiremock.crt"
        if [[ -f "$CERT_DEST" ]]; then
            echo -e "${GRAY}Certificate already installed in system trust store.${NC}"
        else
            if [[ $EUID -eq 0 ]]; then
                cp "$WIREMOCK_CERT" "$CERT_DEST"
                update-ca-trust
                echo -e "${GREEN}Certificate imported to system trust store.${NC}"
            else
                if command -v sudo &> /dev/null; then
                    sudo cp "$WIREMOCK_CERT" "$CERT_DEST"
                    sudo update-ca-trust
                    echo -e "${GREEN}Certificate imported to system trust store.${NC}"
                else
                    echo -e "${YELLOW}Warning: Cannot import certificate without root privileges.${NC}"
                    echo -e "${GRAY}Manual import: sudo cp '$WIREMOCK_CERT' /etc/pki/ca-trust/source/anchors/ && sudo update-ca-trust${NC}"
                fi
            fi
        fi
    elif [[ -d "/etc/ssl/certs" ]] && [[ -f "/etc/alpine-release" ]]; then
        # Alpine Linux
        CERT_DEST="/usr/local/share/ca-certificates/wiremock.crt"
        if [[ -f "$CERT_DEST" ]]; then
            echo -e "${GRAY}Certificate already installed in system trust store.${NC}"
        else
            if [[ $EUID -eq 0 ]]; then
                mkdir -p /usr/local/share/ca-certificates
                cp "$WIREMOCK_CERT" "$CERT_DEST"
                update-ca-certificates
                echo -e "${GREEN}Certificate imported to system trust store.${NC}"
            else
                if command -v sudo &> /dev/null; then
                    sudo mkdir -p /usr/local/share/ca-certificates
                    sudo cp "$WIREMOCK_CERT" "$CERT_DEST"
                    sudo update-ca-certificates
                    echo -e "${GREEN}Certificate imported to system trust store.${NC}"
                else
                    echo -e "${YELLOW}Warning: Cannot import certificate without root privileges.${NC}"
                fi
            fi
        fi
    else
        echo -e "${YELLOW}Warning: Unknown Linux distribution. Cannot auto-import certificate.${NC}"
        echo -e "${GRAY}You may need to manually add '$WIREMOCK_CERT' to your system's CA trust store.${NC}"
    fi

    # Also set NODE_EXTRA_CA_CERTS for Node.js applications
    if ! grep -q "NODE_EXTRA_CA_CERTS" "$ENV_FILE"; then
        echo "NODE_EXTRA_CA_CERTS=\"${WIREMOCK_CERT}\"" >> "$ENV_FILE"
        echo -e "${GREEN}NODE_EXTRA_CA_CERTS configured for Node.js applications.${NC}"
    fi
else
    echo -e "${YELLOW}Warning: WireMock certificate not found. HTTPS clients may not trust the server.${NC}"
fi
#endregion

#region Docker Services
echo -e "\n${CYAN}=== Docker Services ===${NC}"

if command -v docker &> /dev/null; then
    echo -e "${YELLOW}Starting Docker containers...${NC}"

    # Start the shared development collection.
    docker compose \
        --env-file "$ENV_FILE" \
        -f "${CONTAINERS_DIR}/docker-compose-common.yml" \
        -p dev_common_shared \
        up -d

    echo -e "${GREEN}Docker containers started.${NC}"
else
    echo -e "${RED}Error: Docker is not installed or not in PATH.${NC}"
    exit 1
fi
#endregion

echo -e "\n${GREEN}=== Setup Complete ===${NC}"
echo -e "Services available:"
echo -e "${GRAY}  SQL Server:     localhost:${MSSQL_PORT:-10433}${NC}"
echo -e "${GRAY}  CosmosDB:       https://localhost:${COSMOSDB_HTTPS_PORT:-10081}${NC}"
echo -e "${GRAY}  Cosmos Explorer: http://localhost:${COSMOSDB_EXPLORER_PORT:-10181}${NC}"
echo -e "${GRAY}  Redis:          localhost:${REDIS_PORT:-10120}${NC}"
echo -e "${GRAY}  RedisInsight:   http://localhost:${REDISINSIGHT_WEB_PORT:-10121}${NC}"
echo -e "${GRAY}  SMTP4Dev SMTP:  localhost:${SMTP4DEV_SMTP_PORT:-10130}${NC}"
echo -e "${GRAY}  SMTP4Dev POP:   localhost:${SMTP4DEV_POP_PORT:-10131}${NC}"
echo -e "${GRAY}  SMTP4Dev IMAP:  localhost:${SMTP4DEV_IMAP_PORT:-10132}${NC}"
echo -e "${GRAY}  SMTP4Dev Web:   http://localhost:${SMTP4DEV_WEB_PORT:-10140}${NC}"
echo -e "${GRAY}  Seq (OTEL):     http://localhost:${SEQ_HTTP_PORT:-10150}${NC}"
echo -e "${GRAY}  WireMock HTTP:  http://localhost:${WIREMOCK_HTTP_PORT:-10080}${NC}"
echo -e "${GRAY}  WireMock HTTPS: https://localhost:${WIREMOCK_HTTPS_PORT:-10443}${NC}"
echo -e "${GRAY}  Azurite Blob:   localhost:${AZURITE_BLOB_PORT:-11000}${NC}"
echo -e "${GRAY}  Azurite Queue:  localhost:${AZURITE_QUEUE_PORT:-11001}${NC}"
echo -e "${GRAY}  Azurite Table:  localhost:${AZURITE_TABLE_PORT:-11002}${NC}"
echo -e "${GRAY}  Service Bus:    localhost:${SERVICEBUS_AMQP_PORT:-10170}${NC}"
echo -e "${GRAY}  Service Bus Admin: localhost:${SERVICEBUS_ADMIN_PORT:-10171}${NC}"
echo ""
echo "Head back to README.md for deployment of the database and other services..."
