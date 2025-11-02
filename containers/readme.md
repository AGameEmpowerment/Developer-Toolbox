# Containers README

This folder contains developer-focused Docker Compose configurations and helper files for local emulators used by the project.

## Local SQL Server volume

A persistent Docker named volume is used for the local SQL Server service in `docker-compose-common.yml`:

- Volume name: `mssql-data`
- Container path: `/var/opt/mssql`

This keeps the database files persisted across container restarts and recreations.

## Run compose (development)

From the repository root you can start the configured services with:

```pwsh
# Start the full development compose stack
docker compose -f containers/docker-compose-common.yml up -d

# Tail logs for SQL server
docker compose -f containers/docker-compose-common.yml logs -f mssql
```

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
- If you need to inspect the volume, use a temporary container and shell into it:

```pwsh
docker run --rm -it -v containers_mssql-data:/volume alpine sh
ls -la /volume
```

If you'd like, I can also add a compose override `docker-compose.override.yml` for developer bind mounts, or a CI job that creates and archives the SQL backup automatically.