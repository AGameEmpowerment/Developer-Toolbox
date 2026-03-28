#!/bin/bash
# Teardown Docker Services (Linux/Unix/WSL/Devcontainer)

set -euo pipefail

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

    # Teardown the shared development collection.
    docker compose \
        -f "${CONTAINERS_DIR}/docker-compose-common.yml" \
        -p dev_common_shared \
        down --remove-orphans || true

    # Safety net: remove any leftover resources still labeled with this compose project.
    PROJECT_NAMES=(
        "dev_common_shared"
    )

    for project_name in "${PROJECT_NAMES[@]}"; do
        container_ids=()
        while IFS= read -r container_id; do
            if [[ -n "$container_id" ]]; then
                container_ids+=("$container_id")
            fi
        done < <(docker ps -aq --filter "label=com.docker.compose.project=${project_name}" || true)
        if [[ ${#container_ids[@]} -gt 0 ]]; then
            echo -e "${YELLOW}Removing leftover containers for project '${project_name}'...${NC}"
            docker rm -f "${container_ids[@]}" >/dev/null || true
        fi

        network_ids=()
        while IFS= read -r network_id; do
            if [[ -n "$network_id" ]]; then
                network_ids+=("$network_id")
            fi
        done < <(docker network ls -q --filter "label=com.docker.compose.project=${project_name}" || true)
        if [[ ${#network_ids[@]} -gt 0 ]]; then
            echo -e "${YELLOW}Removing leftover networks for project '${project_name}'...${NC}"
            docker network rm "${network_ids[@]}" >/dev/null || true
        fi

        volume_ids=()
        while IFS= read -r volume_id; do
            if [[ -n "$volume_id" ]]; then
                volume_ids+=("$volume_id")
            fi
        done < <(docker volume ls -q --filter "label=com.docker.compose.project=${project_name}" || true)
        if [[ ${#volume_ids[@]} -gt 0 ]]; then
            echo -e "${YELLOW}Removing leftover volumes for project '${project_name}'...${NC}"
            docker volume rm "${volume_ids[@]}" >/dev/null || true
        fi
    done

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
