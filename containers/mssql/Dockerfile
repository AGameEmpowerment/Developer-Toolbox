FROM mcr.microsoft.com/mssql/server:latest

# We need tooling (sqlcmd) which is NOT in the base image by default.
USER root

ENV ACCEPT_EULA=Y \
    DEBIAN_FRONTEND=noninteractive

# Install required packages & Microsoft repo for mssql-tools18 / odbc
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl apt-transport-https gnupg2 dos2unix locales \
    && curl https://packages.microsoft.com/keys/microsoft.asc | apt-key add - \
    && curl https://packages.microsoft.com/config/ubuntu/22.04/prod.list -o /etc/apt/sources.list.d/mssql-release.list \
    && apt-get update \
    && ACCEPT_EULA=Y apt-get install -y --no-install-recommends msodbcsql18 mssql-tools18 \
    && ln -s /opt/mssql-tools18 /opt/mssql-tools || true \
    && echo 'en_US.UTF-8 UTF-8' > /etc/locale.gen && locale-gen \
    && apt-get purge -y gnupg2 \
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
# NOTE: Uses MSSQL_SA_PASSWORD from environment; avoid logging sensitive output.
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=5 \
    CMD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "SET NOCOUNT ON; SELECT 1" >/dev/null 2>&1 || exit 1
