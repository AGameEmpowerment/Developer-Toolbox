#!/bin/bash
# Teardown Container Services (Linux/Unix/WSL/Devcontainer)

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

# Parse arguments
CLEAN_CERTS=false
CLEAN_ENV=false
CLEAN_VOLUMES=false
CLEAN_ALL=false
FORCE=false
CONTAINER_RUNTIME="${CONTAINER_RUNTIME:-auto}"

while [[ $# -gt 0 ]]; do
    case $1 in
        --runtime)
            if [[ $# -lt 2 || "$2" == -* ]]; then
                echo -e "${RED}Error: --runtime requires auto, docker, or podman.${NC}" >&2
                exit 1
            fi
            CONTAINER_RUNTIME="$2"
            shift 2
            ;;
        --runtime=*)
            CONTAINER_RUNTIME="${1#*=}"
            shift
            ;;
        --clean-certs|-c)
            CLEAN_CERTS=true
            shift
            ;;
        --clean-env|-e)
            CLEAN_ENV=true
            shift
            ;;
        --clean-volumes|-v)
            CLEAN_VOLUMES=true
            shift
            ;;
        --clean-all|-a)
            CLEAN_ALL=true
            shift
            ;;
        --force|-f)
            FORCE=true
            shift
            ;;
        --help|-h)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -c, --clean-certs  Remove WireMock certificates (wiremock.jks, wiremock.crt)"
            echo "  -e, --clean-env    Remove .env file (will be regenerated on next setup)"
            echo "  -v, --clean-volumes Remove container named volumes for the compose project (requires --force)"
            echo "  -a, --clean-all    Remove all ephemeral files and container named volumes (requires --force)"
            echo "  -f, --force        Confirm destructive cleanup that removes container named volumes"
            echo "  --runtime auto|docker|podman  Container runtime to use (default: auto, or CONTAINER_RUNTIME)"
            echo "  -h, --help         Show this help message"
            exit 0
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}" >&2
            echo "Use --help for usage information." >&2
            exit 1
            ;;
    esac
done

resolve_container_runtime() {
    local requested_runtime="$1"

    case "$requested_runtime" in
        auto)
            if command -v docker >/dev/null 2>&1; then
                printf '%s' "docker"
            elif command -v podman >/dev/null 2>&1; then
                printf '%s' "podman"
            fi
            ;;
        docker|podman)
            if command -v "$requested_runtime" >/dev/null 2>&1; then
                printf '%s' "$requested_runtime"
            else
                echo -e "${RED}Missing dependency: '${requested_runtime}' was not found in PATH.${NC}" >&2
                echo "Install ${requested_runtime}, make sure its CLI is available in PATH, then open a new terminal." >&2
                exit 1
            fi
            ;;
        *)
            echo -e "${RED}Error: Invalid runtime '${requested_runtime}'. Use docker, podman, or auto.${NC}" >&2
            exit 1
            ;;
    esac
}

runtime_ready() {
    local runtime="$1"

    "$runtime" info >/dev/null 2>&1
}

runtime_unavailable_message() {
    local runtime="$1"
    local display_name="$2"

    if [[ "$runtime" == "docker" ]]; then
        echo "${display_name} CLI is installed, but the Docker engine is not reachable. Start Docker Desktop, wait until it finishes starting, then rerun this script."
    else
        echo "${display_name} CLI is installed, but the Podman engine is not reachable. Start the Podman machine with 'podman machine start', then rerun this script."
    fi
}

compose_available() {
    local runtime="$1"

    "$runtime" compose version >/dev/null 2>&1
}

format_runtime_display_name() {
    case "$1" in
        docker)
            printf '%s' "Docker"
            ;;
        podman)
            printf '%s' "Podman"
            ;;
        "")
            printf '%s' "Container"
            ;;
        *)
            printf '%s' "$1"
            ;;
    esac
}

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONTAINERS_DIR="${SCRIPT_DIR}/containers"
CERTS_DIR="${CONTAINERS_DIR}/certs"
CONTAINER_CLI="$(resolve_container_runtime "$CONTAINER_RUNTIME")"
RUNTIME_DISPLAY_NAME="$(format_runtime_display_name "$CONTAINER_CLI")"

if [[ "$CLEAN_VOLUMES" == true || "$CLEAN_ALL" == true ]] && [[ "$FORCE" != true ]]; then
    echo -e "${RED}Destructive cleanup requires --force. Re-run with --clean-volumes --force or --clean-all --force to remove container named volumes.${NC}" >&2
    exit 1
fi

if [[ -n "$CONTAINER_CLI" ]] && ! runtime_ready "$CONTAINER_CLI"; then
    runtime_message="$(runtime_unavailable_message "$CONTAINER_CLI" "$RUNTIME_DISPLAY_NAME")"

    if [[ "$CLEAN_VOLUMES" == true || "$CLEAN_ALL" == true ]]; then
        echo -e "${RED}${runtime_message}${NC}" >&2
        exit 1
    fi

    echo -e "${YELLOW}Warning: ${runtime_message} Skipping container teardown.${NC}" >&2
    CONTAINER_CLI=""
    RUNTIME_DISPLAY_NAME="Container"
fi

if [[ "$CLEAN_VOLUMES" == true || "$CLEAN_ALL" == true ]] && [[ -z "$CONTAINER_CLI" ]]; then
    echo -e "${RED}Missing dependency: Docker or Podman must be installed and running to remove container named volumes.${NC}" >&2
    exit 1
fi

#region Container Teardown
if [[ -n "$CONTAINER_CLI" ]]; then
    echo -e "${CYAN}=== ${RUNTIME_DISPLAY_NAME} Teardown ===${NC}"
else
    echo -e "${CYAN}=== Container Teardown ===${NC}"
fi

if [[ -n "$CONTAINER_CLI" ]]; then
    echo -e "${YELLOW}Stopping and removing containers...${NC}"

    # Teardown the shared development collection.
    if compose_available "$CONTAINER_CLI"; then
        if ! "$CONTAINER_CLI" compose \
            -f "${CONTAINERS_DIR}/docker-compose-common.yml" \
            -p dev_common_shared \
            down --remove-orphans; then
            echo -e "${YELLOW}Warning: ${RUNTIME_DISPLAY_NAME} compose down failed. Continuing with leftover resource cleanup.${NC}" >&2
        fi
    else
        echo -e "${YELLOW}Warning: Missing dependency: ${RUNTIME_DISPLAY_NAME} compose support is not available. Skipping compose down and continuing with leftover resource cleanup.${NC}" >&2
    fi

    # Safety net: remove any leftover resources still labeled with this compose project.
    PROJECT_NAMES=(
        "dev_common_shared"
    )
    COMPOSE_PROJECT_LABELS=(
        "com.docker.compose.project"
        "io.podman.compose.project"
    )

    for project_name in "${PROJECT_NAMES[@]}"; do
        container_ids=()
        for label_name in "${COMPOSE_PROJECT_LABELS[@]}"; do
            while IFS= read -r container_id; do
                if [[ -n "$container_id" && ! " ${container_ids[*]} " =~ [[:space:]]${container_id}[[:space:]] ]]; then
                    container_ids+=("$container_id")
                fi
            done < <("$CONTAINER_CLI" ps -aq --filter "label=${label_name}=${project_name}" || true)
        done

        if [[ ${#container_ids[@]} -gt 0 ]]; then
            echo -e "${YELLOW}Removing leftover containers for project '${project_name}'...${NC}"
            "$CONTAINER_CLI" rm -f "${container_ids[@]}" >/dev/null || true
        fi

        network_ids=()
        for label_name in "${COMPOSE_PROJECT_LABELS[@]}"; do
            while IFS= read -r network_id; do
                if [[ -n "$network_id" && ! " ${network_ids[*]} " =~ [[:space:]]${network_id}[[:space:]] ]]; then
                    network_ids+=("$network_id")
                fi
            done < <("$CONTAINER_CLI" network ls -q --filter "label=${label_name}=${project_name}" || true)
        done

        if [[ ${#network_ids[@]} -gt 0 ]]; then
            echo -e "${YELLOW}Removing leftover networks for project '${project_name}'...${NC}"
            "$CONTAINER_CLI" network rm "${network_ids[@]}" >/dev/null || true
        fi

        if [[ "$CLEAN_VOLUMES" == true ]] || [[ "$CLEAN_ALL" == true ]]; then
            volume_ids=()
            for label_name in "${COMPOSE_PROJECT_LABELS[@]}"; do
                while IFS= read -r volume_id; do
                    if [[ -n "$volume_id" && ! " ${volume_ids[*]} " =~ [[:space:]]${volume_id}[[:space:]] ]]; then
                        volume_ids+=("$volume_id")
                    fi
                done < <("$CONTAINER_CLI" volume ls -q --filter "label=${label_name}=${project_name}" || true)
            done

            if [[ ${#volume_ids[@]} -gt 0 ]]; then
                echo -e "${YELLOW}Removing leftover volumes for project '${project_name}'...${NC}"
                "$CONTAINER_CLI" volume rm "${volume_ids[@]}" >/dev/null || true
            fi
        fi
    done

    echo -e "${GREEN}Containers removed.${NC}"
else
    echo -e "${YELLOW}Warning: Docker or Podman not found. Skipping container teardown.${NC}"
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

if [[ "$CLEAN_CERTS" == false ]] && [[ "$CLEAN_ENV" == false ]] && [[ "$CLEAN_VOLUMES" == false ]] && [[ "$CLEAN_ALL" == false ]]; then
    echo ""
    echo -e "${YELLOW}Tip: Use these flags to clean ephemeral files:${NC}"
    echo -e "${GRAY}  -c, --clean-certs  : Remove WireMock certificates (wiremock.jks, wiremock.crt)${NC}"
    echo -e "${GRAY}  -e, --clean-env    : Remove .env file (will be regenerated on next setup)${NC}"
    echo -e "${GRAY}  -v, --clean-volumes --force: Remove container named volumes for the compose project${NC}"
    echo -e "${GRAY}  -a, --clean-all --force    : Remove all ephemeral files and container named volumes${NC}"
    echo -e "${GRAY}  --runtime podman           : Use Podman instead of Docker${NC}"
fi
