#!/bin/bash
# Setup Container Services (Linux/Unix/WSL/Devcontainer)

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

CONTAINER_RUNTIME="${CONTAINER_RUNTIME:-auto}"

usage() {
    echo "Usage: $0 [OPTIONS]"
    echo ""
    echo "Options:"
    echo "  --runtime auto|docker|podman  Container runtime to use (default: auto, or CONTAINER_RUNTIME)"
    echo "  -h, --help               Show this help message"
}

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
        --help|-h)
            usage
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
            local installed_runtime=false
            local runtime
            for runtime in docker podman; do
                if command -v "$runtime" >/dev/null 2>&1; then
                    installed_runtime=true
                    if "$runtime" info >/dev/null 2>&1 && "$runtime" compose version >/dev/null 2>&1; then
                        printf '%s' "$runtime"
                        return
                    fi
                fi
            done

            if [[ "$installed_runtime" == false ]]; then
                echo -e "${RED}Missing dependency: no supported container runtime CLI was found.${NC}" >&2
                echo "Install Docker Desktop or Podman, make sure the CLI is available in PATH, then open a new terminal." >&2
            else
                echo -e "${RED}No installed container runtime is reachable with Compose support.${NC}" >&2
                echo "Start Docker or Podman, or select one explicitly with --runtime." >&2
            fi
            exit 1
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

format_runtime_display_name() {
    case "$1" in
        docker)
            printf '%s' "Docker"
            ;;
        podman)
            printf '%s' "Podman"
            ;;
        *)
            printf '%s' "$1"
            ;;
    esac
}

assert_runtime_ready() {
    local runtime="$1"
    local display_name="$2"

    if "$runtime" info >/dev/null 2>&1; then
        return
    fi

    if [[ "$runtime" == "docker" ]]; then
        echo -e "${RED}${display_name} CLI is installed, but the Docker engine is not reachable.${NC}" >&2
        echo "Start Docker Desktop, wait until it finishes starting, then rerun this script." >&2
    else
        echo -e "${RED}${display_name} CLI is installed, but the Podman engine is not reachable.${NC}" >&2
        echo "Start the Podman machine with 'podman machine start', then rerun this script." >&2
    fi

    exit 1
}

assert_compose_available() {
    local runtime="$1"
    local display_name="$2"

    if ! "$runtime" compose version >/dev/null 2>&1; then
        echo -e "${RED}Missing dependency: ${display_name} compose support is not available.${NC}" >&2
        echo "Install ${display_name} with compose support, or choose another runtime with --runtime." >&2
        exit 1
    fi
}

assert_podman_stop_persistence() {
    [[ "$CONTAINER_CLI" == podman ]] || return 0
    local server_version remote major
    server_version="$("$CONTAINER_CLI" version --format '{{.Server.Version}}' 2>/dev/null || true)"
    if [[ ! "$server_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-+].+)?$ ]]; then
        # Native Linux has no separate server object; info describes that engine.
        remote="$("$CONTAINER_CLI" info --format '{{.Host.ServiceIsRemote}}' 2>/dev/null || true)"
        if [[ "$remote" == false ]]; then
            server_version="$("$CONTAINER_CLI" info --format '{{.Version.Version}}' 2>/dev/null || true)"
        fi
    fi
    if [[ ! "$server_version" =~ ^([0-9]+)\.[0-9]+\.[0-9]+([-+].+)?$ ]]; then
        echo "Unable to determine the Podman server version. Podman 6 or later is required to keep manually stopped services stopped across restarts." >&2
        exit 1
    fi
    major="${BASH_REMATCH[1]}"
    if (( 10#$major < 6 )); then
        echo "Podman server 6 or later is required to keep manually stopped services stopped across restarts. Upgrade the Podman engine, then rerun setup." >&2
        exit 1
    fi
}

parse_env_scalar() {
    local value="$1" quote character following parsed="" position=1
    value="${value%$'\r'}"
    value="${value#"${value%%[![:space:]]*}"}"
    value="${value%"${value##*[![:space:]]}"}"
    quote="${value:0:1}"
    if [[ "$quote" == '"' || "$quote" == "'" ]]; then
        while (( position < ${#value} )); do
            character="${value:position:1}"
            if [[ "$character" == "$quote" ]]; then break; fi
            if [[ "$character" == '\' ]] && (( position + 1 < ${#value} )); then
                following="${value:position+1:1}"
                if [[ "$following" == "$quote" || ("$quote" == '"' && "$following" == '\') ]]; then
                    parsed+="$following"
                    position=$((position + 2))
                    continue
                fi
                if [[ "$quote" == '"' ]]; then
                    case "$following" in
                        n) parsed+=$'\n' ;;
                        r) parsed+=$'\r' ;;
                        t) parsed+=$'\t' ;;
                        '$') parsed+='$' ;;
                        *) parsed+='\'; position=$((position + 1)); continue ;;
                    esac
                    position=$((position + 2))
                    continue
                fi
            fi
            parsed+="$character"
            position=$((position + 1))
        done
        printf '%s' "$parsed"
    else
        value="${value%%[[:space:]]#*}"
        value="${value%"${value##*[![:space:]]}"}"
        printf '%s' "$value"
    fi
}

read_env_scalar() {
    local requested_key="$1" line value=""
    while IFS= read -r line || [[ -n "$line" ]]; do
        if [[ "$line" =~ ^[[:space:]]*(export[[:space:]]+)?${requested_key}[[:space:]]*=(.*)$ ]]; then
            value="$(parse_env_scalar "${BASH_REMATCH[2]}")"
        fi
    done < "$ENV_FILE"
    # Match Compose's last assignment when developers redefine a key later.
    printf '%s' "$value"
}

shared_compose() {
    "$CONTAINER_CLI" compose "$@"
}

initialize_shared_data_volumes() {
    # Compose must treat migrated volumes as external because their original labels remain.
    # Label new volumes so explicit teardown cleanup can find them with either runtime.
    local group volume volume_name
    local volumes=()
    for group in azure aws; do
        if [[ "$group" == azure ]]; then
            volumes=(cosmos-data mssql-data azurite-data)
        else
            volumes=(postgres-data dynamodb-data rabbitmq-data)
        fi
        for volume in "${volumes[@]}"; do
            volume_name="dev_common_shared_${volume}"
            if "$CONTAINER_CLI" volume inspect "$volume_name" >/dev/null 2>&1; then continue; fi
            if ! "$CONTAINER_CLI" volume create --driver local \
                --label "com.docker.compose.project=dev_${group}_shared" \
                --label "com.docker.compose.volume=${volume}" "$volume_name" >/dev/null; then
                echo "Unable to create shared data volume '${volume_name}'." >&2
                return 1
            fi
        done
    done
}

remove_legacy_shared_containers() {
    # Native Podman Compose's orphan handling can remove retained resources.
    # Inspect exact project/service labels instead, without removing any volumes.
    local project_label found container_ids="" container_id service seen_ids=$'\n'
    local service_template='{{if eq (index .Config.Labels "com.docker.compose.project") "dev_common_shared"}}{{index .Config.Labels "com.docker.compose.service"}}{{else if eq (index .Config.Labels "io.podman.compose.project") "dev_common_shared"}}{{index .Config.Labels "io.podman.compose.service"}}{{end}}'
    for project_label in com.docker.compose.project io.podman.compose.project; do
        if ! found="$("$CONTAINER_CLI" ps -a --filter "label=${project_label}=dev_common_shared" -q 2>/dev/null)"; then
            echo 'Unable to enumerate retired sample containers in the shared toolbox project.' >&2
            return 1
        fi
        container_ids+="$found"$'\n'
    done
    while IFS= read -r container_id; do
        container_id="${container_id%$'\r'}"
        [[ -n "$container_id" ]] || continue
        [[ "$container_id" =~ ^[a-fA-F0-9]{12,64}$ ]] || { echo 'The selected engine returned an invalid container identifier.' >&2; return 1; }
        [[ "$seen_ids" != *$'\n'"$container_id"$'\n'* ]] || continue
        seen_ids+="$container_id"$'\n'
        if ! service="$("$CONTAINER_CLI" inspect --format "$service_template" "$container_id" 2>/dev/null)"; then
            echo 'Unable to verify labels on a possible retired sample container.' >&2
            return 1
        fi
        service="${service%$'\r'}"
        case "$service" in
            functions-key-init|function-app|lambda-runtime|lambda-sqs-runtime|lambda-sqs-dispatcher|\
            function-app-java|lambda-runtime-java|lambda-sqs-runtime-java|lambda-sqs-dispatcher-java|\
            function-app-node|lambda-runtime-node|lambda-sqs-dispatcher-node|\
            function-app-python|lambda-runtime-python|lambda-sqs-dispatcher-python|\
            mssql|cosmosdb|azurite|service-bus|postgres|dynamodb-volume-init|dynamodb|\
            rabbitmq|aws-emulator|aws-init)
                # Release fixed names; the new groups reuse the original named volumes.
                if ! "$CONTAINER_CLI" rm -f "$container_id" >/dev/null 2>&1; then
                    echo 'Unable to remove a retired sample container from the shared toolbox project.' >&2
                    return 1
                fi
                printf 'Removed legacy shared container: %s (volumes retained).\n' "$service"
                ;;
        esac
    done <<< "$container_ids"
}

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONTAINERS_DIR="${SCRIPT_DIR}/containers"
CERTS_DIR="${CONTAINERS_DIR}/certs"
ENV_FILE="${CONTAINERS_DIR}/.env"
ENV_EXAMPLE_FILE="${CONTAINERS_DIR}/.env.example"
CONTAINER_CLI="$(resolve_container_runtime "$CONTAINER_RUNTIME")"
RUNTIME_DISPLAY_NAME="$(format_runtime_display_name "$CONTAINER_CLI")"
assert_runtime_ready "$CONTAINER_CLI" "$RUNTIME_DISPLAY_NAME"
assert_compose_available "$CONTAINER_CLI" "$RUNTIME_DISPLAY_NAME"
assert_podman_stop_persistence

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

# Keep existing developer .env files compatible when new service settings
# are introduced without overwriting any local values.
existing_keys=$'\n'
while IFS= read -r existing_assignment || [[ -n "$existing_assignment" ]]; do
    existing_assignment="${existing_assignment%$'\r'}"
    if [[ "$existing_assignment" =~ ^[[:space:]]*(export[[:space:]]+)?([A-Za-z_][A-Za-z0-9_]*)[[:space:]]*= ]]; then
        existing_keys+="${BASH_REMATCH[2]}"$'\n'
    fi
done < "$ENV_FILE"
while IFS= read -r assignment || [[ -n "$assignment" ]]; do
    assignment="${assignment%$'\r'}"
    if [[ "$assignment" =~ ^[[:space:]]*([A-Za-z_][A-Za-z0-9_]*)[[:space:]]*= ]]; then
        key="${BASH_REMATCH[1]}"
        if [[ "$existing_keys" != *$'\n'"${key}"$'\n'* ]]; then
            # Preserve quoting and interpolation as data, including a missing final newline.
            printf '\n%s\n' "$assignment" >> "$ENV_FILE"
            existing_keys+="${key}"$'\n'
            echo -e "${GREEN}${key} added to .env for local services.${NC}"
        fi
    fi
done < "$ENV_EXAMPLE_FILE"

# Generate explicit auto placeholders once; the helper never executes .env contents.
source "${CONTAINERS_DIR}/environment.sh"
initialize_toolbox_environment "$ENV_FILE"

# Note: .env is consumed by container compose; this script does not execute it
# to avoid running arbitrary configuration as shell code.
#endregion

bash "${SCRIPT_DIR}/docker_pull.sh" --runtime "$CONTAINER_CLI"
TOOLBOX_MSSQL_ENGINE_VERSION="$("$CONTAINER_CLI" image inspect --format '{{ index .Config.Labels "com.microsoft.version" }}' mcr.microsoft.com/mssql/server:latest)"
[[ "$TOOLBOX_MSSQL_ENGINE_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'The local SQL Server base is missing a valid publisher version label.' >&2; exit 1; }
export TOOLBOX_MSSQL_ENGINE_VERSION

#region WireMock Certificate Setup
SHARED_GROUPS=(common azure aws)
SELECTED_SERVICES=""
for group in "${SHARED_GROUPS[@]}"; do
    if ! group_services="$(shared_compose --env-file "$ENV_FILE" -f "${CONTAINERS_DIR}/docker-compose-${group}.yml" -p "dev_${group}_shared" config --services)"; then
        echo "Unable to resolve the ${group} Compose services. Check containers/.env." >&2
        exit 1
    fi
    SELECTED_SERVICES+="$group_services"$'\n'
done

echo -e "\n${CYAN}=== WireMock Certificate Setup ===${NC}"

WIREMOCK_KEYSTORE="${CERTS_DIR}/wiremock.jks"
GENERATE_CERT_SCRIPT="${CERTS_DIR}/generate-wiremock-cert.sh"

if [[ ! -f "$WIREMOCK_KEYSTORE" || "$ENV_FILE_JUST_CREATED" == true || "${TOOLBOX_GENERATED_WIREMOCK_PASSWORD:-false}" == true ]]; then
    if [[ -f "$GENERATE_CERT_SCRIPT" ]]; then
        echo -e "${YELLOW}Generating WireMock TLS certificate...${NC}"
        if ! ENV_PASSWORD="$(resolve_wiremock_password "$ENV_FILE" "$CONTAINER_CLI" && printf '.')"; then
            exit 1
        fi
        ENV_PASSWORD="${ENV_PASSWORD%.}"
        if [[ -z "$ENV_PASSWORD" ]]; then
            echo 'Configure WIREMOCK_KEYSTORE_PASSWORD in containers/.env before generating certificates.' >&2
            exit 1
        fi
        # Keep the password out of native process arguments and preserve the raw dotenv assignment.
        TOOLBOX_WIREMOCK_KEYSTORE_PASSWORD="$ENV_PASSWORD" \
            bash "$GENERATE_CERT_SCRIPT" --runtime "$CONTAINER_CLI" -f
        echo -e "${GREEN}WireMock certificate generated using the configured keystore password.${NC}"
    else
        echo -e "${YELLOW}Warning: WireMock certificate script not found at: ${GENERATE_CERT_SCRIPT}${NC}"
    fi
else
    echo -e "${GRAY}WireMock keystore already exists. Skipping certificate generation.${NC}"
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
    if [[ -z "$(read_env_scalar NODE_EXTRA_CA_CERTS)" ]]; then
        # Update the empty template value without overwriting a developer's certificate path.
        env_without_node_cert="$(umask 077; mktemp "${ENV_FILE}.tmp.XXXXXX")"
        grep -Ev '^[[:space:]]*(export[[:space:]]+)?NODE_EXTRA_CA_CERTS[[:space:]]*=' "$ENV_FILE" > "$env_without_node_cert" || true
        node_certificate_path="${WIREMOCK_CERT//\\/\\\\}"
        node_certificate_path="${node_certificate_path//\"/\\\"}"
        node_certificate_path="${node_certificate_path//\$/\\\$}"
        printf '\nNODE_EXTRA_CA_CERTS="%s"\n' "$node_certificate_path" >> "$env_without_node_cert"
        if ! mv "$env_without_node_cert" "$ENV_FILE"; then
            rm -f "$env_without_node_cert"
            exit 1
        fi
        echo -e "${GREEN}NODE_EXTRA_CA_CERTS configured for Node.js applications.${NC}"
    fi
else
    echo -e "${YELLOW}Warning: WireMock certificate not found. HTTPS clients may not trust the server.${NC}"
fi
#endregion

#region Container Services
echo -e "\n${CYAN}=== ${RUNTIME_DISPLAY_NAME} Services ===${NC}"

echo -e "${YELLOW}Starting ${RUNTIME_DISPLAY_NAME} containers...${NC}"

# Start the shared infrastructure resources.
initialize_shared_data_volumes
remove_legacy_shared_containers
for group in "${SHARED_GROUPS[@]}"; do
    echo -e "${YELLOW}Starting dev_${group}_shared...${NC}"
    shared_compose \
        --env-file "$ENV_FILE" \
        -f "${CONTAINERS_DIR}/docker-compose-${group}.yml" \
        -p "dev_${group}_shared" \
        up --build -d
done

echo -e "${GREEN}${RUNTIME_DISPLAY_NAME} containers started.${NC}"
#endregion

echo -e "\n${GREEN}=== Setup Complete ===${NC}"
echo -e "Services available:"
echo -e "${GRAY}  SQL Server:     localhost:10433${NC}"
echo -e "${GRAY}  PostgreSQL:     localhost:10432${NC}"
echo -e "${GRAY}  CosmosDB:       https://localhost:10081${NC}"
echo -e "${GRAY}  Cosmos Explorer: https://localhost:10181${NC}"
echo -e "${GRAY}  DynamoDB:       http://localhost:10180${NC}"
echo -e "${GRAY}  Redis:          localhost:10120${NC}"
echo -e "${GRAY}  RedisInsight:   http://localhost:10121${NC}"
echo -e "${GRAY}  SMTP4Dev SMTP:  localhost:10130${NC}"
echo -e "${GRAY}  SMTP4Dev POP:   localhost:10131${NC}"
echo -e "${GRAY}  SMTP4Dev IMAP:  localhost:10132${NC}"
echo -e "${GRAY}  SMTP4Dev Web:   http://localhost:10140${NC}"
echo -e "${GRAY}  Seq (OTLP):     http://localhost:10150${NC}"
echo -e "${GRAY}  WireMock HTTP:  http://localhost:10080${NC}"
echo -e "${GRAY}  WireMock HTTPS: https://localhost:10443${NC}"
echo -e "${GRAY}  Azurite Blob:   localhost:10000${NC}"
echo -e "${GRAY}  Azurite Queue:  localhost:10001${NC}"
echo -e "${GRAY}  Azurite Table:  localhost:10002${NC}"
echo -e "${GRAY}  Service Bus:    localhost:10170${NC}"
echo -e "${GRAY}  Service Bus Admin: localhost:10171${NC}"
if [[ "$SELECTED_SERVICES" == *rabbitmq* ]]; then
    echo -e "${GRAY}  RabbitMQ: localhost:10190; management: http://localhost:10191${NC}"
fi
if [[ "$SELECTED_SERVICES" == *aws-emulator* ]]; then
    echo -e "${GRAY}  Moto SQS/SNS/EventBridge/S3: http://localhost:10195${NC}"
fi
echo ""
echo "See README.md for using these infrastructure resources from the independent examples."
