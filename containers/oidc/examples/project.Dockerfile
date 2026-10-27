# Local development only. Build the base into this same engine/store first.
ARG OIDC_BASE_IMAGE=localhost/toolbox-oidc-keycloak:latest
FROM ${OIDC_BASE_IMAGE}

# Metadata stays outside native Keycloak auto-import storage; no secrets here.
# Create the parent separately: COPY's mode must not make its directory 0444.
WORKDIR /opt/project-identity
COPY --chmod=0444 profile.json ./profile.json
# Restore the inspected upstream working directory (qualify on provider updates).
WORKDIR /
ENV TOOLBOX_OIDC_PROFILE=project
# Inherit the common startup entrypoint, command and upstream non-root user.
