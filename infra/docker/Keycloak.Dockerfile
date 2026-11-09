ARG KEYCLOAK_IMAGE=quay.io/keycloak/keycloak:26.8.2@sha256:3879c623248408de29a5303178b5816c77e8dfef3890c361f8c0bbeaa979ba3b
FROM ${KEYCLOAK_IMAGE} AS runtime
ENV KC_HEALTH_ENABLED=true KC_DB=postgres KC_DB_USERNAME=keycloak
RUN /opt/keycloak/bin/kc.sh build --db=postgres --health-enabled=true
USER 0
ARG LOCAL_UID=1000
ARG LOCAL_GID=1000
RUN case "$LOCAL_UID:$LOCAL_GID" in *[!0-9:]*|:*|*:) exit 1 ;; esac; test "$LOCAL_UID" -gt 0 && test "$LOCAL_UID" -le 2147483647 && test "$LOCAL_GID" -gt 0 && test "$LOCAL_GID" -le 2147483647 && chown -R "$LOCAL_UID:$LOCAL_GID" /opt/keycloak
COPY --chmod=0555 infra/compose/entrypoints/keycloak.sh infra/compose/entrypoints/healthcheck.sh infra/compose/entrypoints/keycloak-healthcheck.sh /usr/local/bin/
COPY --chmod=0444 infra/keycloak/crm-local-realm.json /opt/keycloak/data/import/crm-local-realm.json
RUN chmod 0755 /opt/keycloak/data/import
USER ${LOCAL_UID}:${LOCAL_GID}
HEALTHCHECK --interval=5s --timeout=5s --start-period=30s --retries=6 CMD ["/usr/local/bin/keycloak-healthcheck.sh"]
ENTRYPOINT ["/usr/local/bin/keycloak.sh"]
