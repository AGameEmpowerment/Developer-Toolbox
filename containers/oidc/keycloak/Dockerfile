# Local development only. Qualify and record the resolved image ID for each run.
ARG UPSTREAM_IMAGE=quay.io/keycloak/keycloak:latest
FROM ${UPSTREAM_IMAGE}

ARG SOURCE_REVISION=unknown
LABEL org.developertoolbox.oidc.contract="1" \
      org.opencontainers.image.source="https://github.com/AGameEmpowerment/Developer-Toolbox" \
      org.opencontainers.image.revision="${SOURCE_REVISION}"

# start-dev performs native augmentation; this is deliberately not an optimized build.
ENV KC_HEALTH_ENABLED=true \
    KC_HTTP_ENABLED=true \
    KC_HTTP_PORT=8080 \
    KC_HTTP_MANAGEMENT_PORT=9000 \
    KC_HOSTNAME_STRICT=true

# Preserve the upstream non-root user and all upstream license notices.
COPY --chmod=0555 startup.sh /opt/toolbox-oidc/startup.sh
COPY --chmod=0444 default-realm.template.json /opt/toolbox-oidc/default-realm.template.json

EXPOSE 8080 9000
ENTRYPOINT ["/opt/toolbox-oidc/startup.sh"]
CMD ["start-dev", "--import-realm"]
