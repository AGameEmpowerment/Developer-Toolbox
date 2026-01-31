#!/bin/bash
# Teardown Docker Services (Linux/Unix/WSL/Devcontainer)

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;90m'
NC='\033[0m' # No Color

# Parse arguments
CLEAN_CERTS=false
CLEAN_ENV=false
CLEAN_ALL=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --clean-certs|-c)
            CLEAN_CERTS=true
            shift
            ;;
        --clean-env|-e)
            CLEAN_ENV=true
            shift
            ;;
        --clean-all|-a)
            CLEAN_ALL=true
            shift
            ;;
        --help|-h)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -c, --clean-certs  Remove WireMock certificates (wiremock.jks, wiremock.crt)"
            echo "  -e, --clean-env    Remove .env file (will be regenerated on next setup)"
            echo "  -a, --clean-all    Remove all ephemeral files (certs + .env)"
            echo "  -h, --help         Show this help message"
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
CONTAINERS_DIR="${SCRIPT_DIR}/containers"
CERTS_DIR="${CONTAINERS_DIR}/certs"

#region Docker Teardown
echo -e "${CYAN}=== Docker Teardown ===${NC}"

if command -v docker &> /dev/null; then
    echo -e "${YELLOW}Stopping and removing containers...${NC}"

    # Teardown the vs multi-container
    docker compose -f "./containers/docker-compose-common.yml" -p dev_common_shared down || true
    #docker compose -f "./containers/docker-compose.yml" -p example down

    echo -e "${GREEN}Containers removed.${NC}"
else
    echo -e "${YELLOW}Warning: Docker not found. Skipping container teardown.${NC}"
fi
#endregion

#region Cleanup Ephemeral Files
if [[ "$CLEAN_CERTS" == true ]] || [[ "$CLEAN_ALL" == true ]]; then
    echo -e "\n${CYAN}=== Cleaning WireMock Certificates ===${NC}"

    # Remove certificate from system trust store
    echo -e "${YELLOW}Removing WireMock certificate from system trust store...${NC}"

    if [[ -d "/usr/local/share/ca-certificates" ]]; then
        # Debian/Ubuntu/Alpine
        CERT_DEST="/usr/local/share/ca-certificates/wiremock.crt"
        if [[ -f "$CERT_DEST" ]]; then
            if [[ $EUID -eq 0 ]]; then
                rm -f "$CERT_DEST"
                update-ca-certificates --fresh 2>/dev/null || update-ca-certificates
                echo -e "${GRAY}  Removed from system trust store.${NC}"
            else
                if command -v sudo &> /dev/null; then
                    sudo rm -f "$CERT_DEST"
                    sudo update-ca-certificates --fresh 2>/dev/null || sudo update-ca-certificates
                    echo -e "${GRAY}  Removed from system trust store.${NC}"
                else
                    echo -e "${YELLOW}  Warning: Cannot remove certificate without root privileges.${NC}"
                fi
            fi
        else
            echo -e "${GRAY}  No WireMock certificate found in system trust store.${NC}"
        fi
    elif [[ -d "/etc/pki/ca-trust/source/anchors" ]]; then
        # RHEL/CentOS/Fedora
        CERT_DEST="/etc/pki/ca-trust/source/anchors/wiremock.crt"
        if [[ -f "$CERT_DEST" ]]; then
            if [[ $EUID -eq 0 ]]; then
                rm -f "$CERT_DEST"
                update-ca-trust
                echo -e "${GRAY}  Removed from system trust store.${NC}"
            else
                if command -v sudo &> /dev/null; then
                    sudo rm -f "$CERT_DEST"
                    sudo update-ca-trust
                    echo -e "${GRAY}  Removed from system trust store.${NC}"
                else
                    echo -e "${YELLOW}  Warning: Cannot remove certificate without root privileges.${NC}"
                fi
            fi
        else
            echo -e "${GRAY}  No WireMock certificate found in system trust store.${NC}"
        fi
    fi

    # Remove certificate files
    CERT_FILES=(
        "${CERTS_DIR}/wiremock.jks"
        "${CERTS_DIR}/wiremock.crt"
        "${CERTS_DIR}/truststore.jks"
    )

    for file in "${CERT_FILES[@]}"; do
        if [[ -f "$file" ]]; then
            rm -f "$file"
            echo -e "${GRAY}  Removed: $(basename "$file")${NC}"
        fi
    done
    echo -e "${GREEN}Certificate files cleaned.${NC}"
fi

if [[ "$CLEAN_ENV" == true ]] || [[ "$CLEAN_ALL" == true ]]; then
    echo -e "\n${CYAN}=== Cleaning Environment File ===${NC}"

    ENV_FILE="${CONTAINERS_DIR}/.env"
    if [[ -f "$ENV_FILE" ]]; then
        rm -f "$ENV_FILE"
        echo -e "${GRAY}  Removed: .env${NC}"
    fi
    echo -e "${GREEN}Environment file cleaned.${NC}"
fi
#endregion

echo -e "\n${GREEN}=== Teardown Complete ===${NC}"

if [[ "$CLEAN_CERTS" == false ]] && [[ "$CLEAN_ENV" == false ]] && [[ "$CLEAN_ALL" == false ]]; then
    echo ""
    echo -e "${YELLOW}Tip: Use these flags to clean ephemeral files:${NC}"
    echo -e "${GRAY}  -c, --clean-certs  : Remove WireMock certificates (wiremock.jks, wiremock.crt)${NC}"
    echo -e "${GRAY}  -e, --clean-env    : Remove .env file (will be regenerated on next setup)${NC}"
    echo -e "${GRAY}  -a, --clean-all    : Remove all ephemeral files (certs + .env)${NC}"
fi
