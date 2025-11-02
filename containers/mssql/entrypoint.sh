#start SQL Server, start the script to create/setup the DB
#You need a non-terminating process to keep the container alive.
#In a series of commands separated by single ampersands the commands to the left of the right-most ampersand are run in the background.
#So - if you are executing a series of commands simultaneously using single ampersands, the command at the right-most position needs to be non-terminating
#!/bin/bash
set -euo pipefail

# Start SQL Server in the background, then run the DB initialization script.
# db-init.sh contains its own retry loop and will wait until SQL Server is ready.

echo "Starting SQL Server in background..."
/opt/mssql/bin/sqlservr &
SQLSRV_PID=$!

echo "Running DB initialization script..."
cd /usr/src/app || exit 1
./db-init.sh
if [ $? -ne 0 ]; then
    echo "Database initialization failed. Exiting."
    # Terminate SQL Server process if init failed
    kill "$SQLSRV_PID" || true
    exit 1
fi

echo "Database initialization completed. Waiting for SQL Server (foreground)..."

# Wait on the SQL Server process so the container stays alive and signals are forwarded properly
wait "$SQLSRV_PID"
