#!/usr/bin/env bash
set -euo pipefail

echo "[db-init] Starting database initialization script"

SA_PASSWORD="${MSSQL_SA_PASSWORD:-${SA_PASSWORD:-}}"
if [ -z "${SA_PASSWORD}" ]; then
    echo "[db-init] ERROR: SA password environment variable (MSSQL_SA_PASSWORD) not set." >&2
    exit 1
fi

SQLCMD_BIN="$(command -v sqlcmd || true)"
if [ -z "$SQLCMD_BIN" ]; then
    # Fallback legacy paths
    for p in /opt/mssql-tools18/bin/sqlcmd /opt/mssql-tools/bin/sqlcmd; do
        if [ -x "$p" ]; then SQLCMD_BIN="$p"; break; fi
    done
fi

if [ -z "$SQLCMD_BIN" ]; then
    echo "[db-init] ERROR: sqlcmd not found in PATH or expected install locations." >&2
    exit 2
fi

echo "[db-init] Using sqlcmd at: $SQLCMD_BIN"

MAX_ATTEMPTS=60
BASE_SLEEP_SECONDS=2
ATTEMPT=1

# Decide whether to trust self-signed certificate.
# Default: trust (safe inside dev container). Override with MSSQL_TRUST_CERT=false to disable.
TRUST_SERVER_CERT="${MSSQL_TRUST_CERT:-true}"
SQLCMD_CERT_FLAG=""
if [ "$TRUST_SERVER_CERT" = "true" ]; then
    # Prefer explicit trust flag (-C) when supported (Driver 18+). If not, append TrustServerCertificate to connection.
    if "$SQLCMD_BIN" -? 2>&1 | grep -qi "-C"; then
        SQLCMD_CERT_FLAG="-C"
    fi
fi


echo "[db-init] Readiness: trust-cert=$TRUST_SERVER_CERT; sqlcmd options: ${SQLCMD_CERT_FLAG:-<none>}"

while true; do
    # Lightweight connectivity check; capture stderr for diagnostics.
    if "$SQLCMD_BIN" -S localhost -U sa -P "$SA_PASSWORD" $SQLCMD_CERT_FLAG -Q "SET NOCOUNT ON; SELECT 1" >/dev/null 2>"/tmp/sqlcmd_err"; then
        break
    fi

    # Heuristic: if failure is ONLY certificate verify and we are set to trust, retry once with -C forced.
    if grep -qi "certificate verify failed" /tmp/sqlcmd_err 2>/dev/null; then
        if [ "$TRUST_SERVER_CERT" = "true" ] && [ -z "$SQLCMD_CERT_FLAG" ]; then
            echo "[db-init] Detected cert validation failure; enabling trust (-C) and retrying immediately." >&2
            SQLCMD_CERT_FLAG="-C"
            continue
        fi
    fi

    if [ $ATTEMPT -ge $MAX_ATTEMPTS ]; then
        echo "[db-init] ERROR: SQL Server not reachable after $MAX_ATTEMPTS attempts." >&2
        # Print last error (sanitized)
        if [ -s /tmp/sqlcmd_err ]; then
            echo "[db-init] Last sqlcmd error:" >&2
            sed 's/\r//' /tmp/sqlcmd_err >&2
        fi
        exit 3
    fi

    if [ $ATTEMPT -eq 1 ] || [ $((ATTEMPT % 10)) -eq 0 ]; then
        # Show diagnostic error occasionally (avoid log flooding)
        if [ -s /tmp/sqlcmd_err ]; then
            echo "[db-init] (diagnostic) sqlcmd error snippet:" >&2
            head -n 3 /tmp/sqlcmd_err | sed 's/\r//' >&2 || true
        fi
    fi

    echo "[db-init] SQL Server not ready yet (attempt $ATTEMPT/$MAX_ATTEMPTS)..."
    ATTEMPT=$((ATTEMPT+1))
    # Exponential backoff with cap (max 10s)
    SLEEP_SECONDS=$(( BASE_SLEEP_SECONDS * 2 ** (ATTEMPT / 4) ))
    if [ $SLEEP_SECONDS -lt $BASE_SLEEP_SECONDS ]; then SLEEP_SECONDS=$BASE_SLEEP_SECONDS; fi
    if [ $SLEEP_SECONDS -gt 10 ]; then SLEEP_SECONDS=10; fi
    sleep $SLEEP_SECONDS
done

echo "[db-init] Server is reachable. Executing schema script..."
"$SQLCMD_BIN" -S localhost -U sa -P "$SA_PASSWORD" $SQLCMD_CERT_FLAG -d master -i ./db-init.sql
echo "[db-init] db-init completed successfully"