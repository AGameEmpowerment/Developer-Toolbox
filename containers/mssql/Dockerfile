# Local development only. Resolve matching Full-Text Search from this base at build time.
FROM mcr.microsoft.com/mssql/server:latest
# FTS must match the actual locally selected base image publisher version.
ARG MSSQL_ENGINE_VERSION

# We need tooling (sqlcmd) which is NOT in the base image by default.
USER root

ENV ACCEPT_EULA=Y \
    DEBIAN_FRONTEND=noninteractive

# The base image already ships the Microsoft signing key and the prod repo (tools/odbc).
# Full-Text Search lives in the version-specific mssql-server repo, which is added here.
# The container image has no dpkg record for its engine, so installing FTS through apt would
# install the engine a second time and add roughly 2.6 GB of uncompressed package content.
# Download and extract the exactly matched FTS package instead to keep this developer image small.
RUN apt-get update \
    && apt-get install -y --no-install-recommends dos2unix locales \
    && . /etc/os-release \
    && product_version="$MSSQL_ENGINE_VERSION" \
    && printf '%s\n' "$product_version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$' \
    && case "$product_version" in 17.*) sql_year=2025 ;; 16.*) sql_year=2022 ;; *) echo 'Unknown SQL Server engine; cannot select matching FTS repository.' >&2; exit 1 ;; esac \
    && echo "deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/ubuntu/${VERSION_ID}/mssql-server-${sql_year} ${VERSION_CODENAME} main" \
        > /etc/apt/sources.list.d/mssql-server.list \
    && apt-get update \
    && ACCEPT_EULA=Y apt-get install -y --no-install-recommends msodbcsql18 mssql-tools18 \
    && cd /tmp \
    && fts_version=$(apt-cache madison mssql-server-fts | awk -v expected="$product_version" 'index($3, expected "-") == 1 {print $3; exit}') \
    && test -n "$fts_version" \
    && apt-get download "mssql-server-fts=${fts_version}" \
    && dpkg-deb -x mssql-server-fts_*.deb / \
    && test -f /opt/mssql/lib/sqlservr.fts.sfp \
    && { [ -e /opt/mssql-tools ] || ln -s /opt/mssql-tools18 /opt/mssql-tools; } \
    && echo 'en_US.UTF-8 UTF-8' > /etc/locale.gen && locale-gen \
    && rm -rf /var/lib/apt/lists/* /tmp/*

# Add sqlcmd to PATH for both root and mssql users
ENV PATH="/opt/mssql-tools18/bin:$PATH"

# Create app directory & copy scripts
RUN mkdir -p /usr/src/app
WORKDIR /usr/src/app
COPY . /usr/src/app
RUN dos2unix *.sh *.sql || true

# Grant permissions for init scripts (principle of least privilege still respected once we drop user)
RUN chmod 750 /usr/src/app/db-init.sh /usr/src/app/entrypoint.sh \
    && chown -R mssql:mssql /usr/src/app

# Switch back to mssql user and run the entrypoint script
USER mssql
ENTRYPOINT ["/bin/bash", "./entrypoint.sh"]

# Container health check: uses a lightweight SELECT 1.
# -C trusts the engine's self-signed certificate; ODBC 18 encrypts by default and
# refuses the connection without it, which made the container report unhealthy.
# NOTE: Uses MSSQL_SA_PASSWORD from environment; avoid logging sensitive output.
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=5 \
    CMD /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "SET NOCOUNT ON; SELECT 1" >/dev/null 2>&1 || exit 1
