# Local-development wrapper for the Toolbox's Alpine-based Node.js services.
# Keep the Alpine release aligned with the upstream image for musl compatibility.
ARG NODE_RUNTIME_IMAGE=docker.io/library/node:lts-alpine3.23
ARG UPSTREAM_IMAGE=mcr.microsoft.com/azure-storage/azurite:latest

FROM ${NODE_RUNTIME_IMAGE} AS node_runtime

FROM ${UPSTREAM_IMAGE}
COPY --from=node_runtime /etc/alpine-release /tmp/toolbox-node-alpine-release
COPY --from=node_runtime /usr/local/bin/node /usr/local/bin/node
COPY --from=node_runtime /usr/lib/libatomic.so.1* /usr/lib/
RUN test "$(cut -d . -f 1,2 /etc/alpine-release)" = "$(cut -d . -f 1,2 /tmp/toolbox-node-alpine-release)" \
    && node --version
