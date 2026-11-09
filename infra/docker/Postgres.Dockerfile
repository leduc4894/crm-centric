ARG POSTGRES_IMAGE=postgres:18.6-bookworm@sha256:f1ea2a1a4fb7b68d4836be1fdcb8a6466a89b072158bf9d97663600538c294c5
FROM ${POSTGRES_IMAGE} AS runtime
COPY --chmod=0555 infra/compose/entrypoints/postgres.sh /usr/local/bin/local-postgres.sh
ENTRYPOINT ["/usr/local/bin/local-postgres.sh"]
CMD ["postgres"]
