#!/usr/bin/env bash
set -euo pipefail

echo "[entrypoint] Launching SQL Server..."
/opt/mssql/bin/sqlservr &
SQL_PID=$!

echo "[entrypoint] SQL Server PID: $SQL_PID"

# Run init script (it will block until server is reachable)
/usr/src/app/db-init.sh || {
	echo "[entrypoint] db-init failed (exit $?); stopping server" >&2
	kill $SQL_PID
	wait $SQL_PID || true
	exit 10
}

echo "[entrypoint] Initialization complete. Foregrounding sqlservr."
wait $SQL_PID
