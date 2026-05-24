# Containers README

This folder contains developer-focused Docker Compose configurations and helper files for local emulators used by the project.

## Local SQL Server volume

A persistent Docker named volume is used for the local SQL Server service in `docker-compose-common.yml`:

- Volume name: `mssql-data`
- Container path: `/var/opt/mssql`

This keeps the database files persisted across container restarts and recreations.

## Local Azurite volume

A persistent Docker named volume is used for the local Azurite service in `docker-compose-common.yml`:

- Volume name: `azurite-data`
- Container path: `/data`

This keeps Azurite blob, queue, and table data persisted across container restarts and recreations.

## Run compose (development)

From the repository root you can start the configured services with:

```pwsh
# Preferred: use the helper script which sets up environment and runs compose
./docker_setup.ps1

# Or start the full development compose stack directly
docker compose -f containers/docker-compose-common.yml up -d

# Refresh the Docker images used by the development stack
./docker_pull.ps1

# Tail logs for SQL server
docker compose -f containers/docker-compose-common.yml logs -f mssql

# Tail logs for RedisInsight
docker compose -f containers/docker-compose-common.yml logs -f redisinsight
```

```bash
# Linux/macOS/WSL equivalents
./docker_setup.sh
./docker_pull.sh
```

To stop the shared local stack without deleting persistent data, run `./docker_down.ps1` on Windows or `./docker_down.sh` on Linux/macOS/WSL. Deleting Docker named volumes now requires an explicit second confirmation flag: `-CleanVolumes -Force` or `--clean-volumes --force`.

Redis and RedisInsight endpoints for local development:

- Redis: `localhost:10120`
- RedisInsight Web UI: `http://localhost:10121`

RedisInsight is preconfigured to connect to the Redis service in Docker (`redis:6379`, DB `0`).
If your app writes to another logical database (for example, DB `1`), switch the selected DB in RedisInsight after connecting.

## Backup & restore the SQL volume

To back up the SQL Server database files from the named volume to a tar file on the host:

```pwsh
# Create a temporary container to tar the volume contents
docker run --rm \
  -v containers_mssql-data:/volume \
  -v ${PWD}:/backup \
  alpine \
  sh -c "cd /volume && tar czf /backup/mssql-data-backup-$(date +%Y%m%d%H%M%S).tgz ."
```

To restore from a backup tar into the named volume (overwrite volume contents):

```pwsh
# Restore into a temporary container (dangerous: will overwrite existing volume contents)
docker run --rm \
  -v containers_mssql-data:/volume \
  -v ${PWD}:/backup \
  alpine \
  sh -c "cd /volume && tar xzf /backup/mssql-data-backup.tgz"
```

Notes:

- Named volume in the compose file appears as `containers_mssql-data` on the host (Docker-managed). The exact name includes the compose project prefix which `docker compose` prints during `up`.
- If you prefer direct host access to database files for debugging, consider switching the volume to a bind mount (e.g., `./mssql-data:/var/opt/mssql`) — be aware of permission differences between host and container filesystems.

## Troubleshooting

- Ensure `MSSQL_SA_PASSWORD` and `ACCEPT_EULA` are set in your environment or an `.env` file when running the compose file.
  - If you don't already have a `.env` file, copy the provided example:

    ```pwsh
    Copy-Item .env.example .env
    ```

    The repository includes a `.env.example` which contains the necessary variables and sane defaults. Creating a local `.env` (or editing after copying) ensures the `docker_setup.ps1` and `docker compose` commands pick up required configuration.
- If RedisInsight appears empty while cache is active, verify the selected Redis DB index in RedisInsight (the default preconfigured DB is `0`).
- If you need to inspect the volume, use a temporary container and shell into it:

```pwsh
docker run --rm -it -v containers_mssql-data:/volume alpine sh
ls -la /volume
```
