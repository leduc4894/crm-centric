ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.14-noble@sha256:1a48394b6f29d3a32c79d9edecd4abda0ecbe8776d567dbfc4590ed6979c740c
FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /build
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Platform.Infrastructure/ src/Platform.Infrastructure/
COPY src/Platform.Api/ src/Platform.Api/
COPY src/Platform.Migrator/ src/Platform.Migrator/
RUN dotnet restore src/Platform.Api/Platform.Api.csproj --locked-mode && dotnet restore src/Platform.Migrator/Platform.Migrator.csproj --locked-mode
RUN dotnet publish src/Platform.Api/Platform.Api.csproj -c Release --no-restore -o /publish/api && dotnet publish src/Platform.Migrator/Platform.Migrator.csproj -c Release --no-restore -o /publish/migrator
FROM ${DOTNET_RUNTIME_IMAGE} AS runtime
ARG LOCAL_UID=1000
ARG LOCAL_GID=1000
RUN case "$LOCAL_UID:$LOCAL_GID" in *[!0-9:]*|:*|*:) exit 1 ;; esac; test "$LOCAL_UID" -gt 0 && test "$LOCAL_UID" -le 2147483647 && test "$LOCAL_GID" -gt 0 && test "$LOCAL_GID" -le 2147483647
COPY --chmod=0555 infra/compose/entrypoints/platform.sh infra/compose/entrypoints/healthcheck.sh /usr/local/bin/
USER ${LOCAL_UID}:${LOCAL_GID}
WORKDIR /app
FROM runtime AS api
COPY --from=build /publish/api/ ./
HEALTHCHECK --interval=5s --timeout=5s --start-period=10s --retries=3 CMD ["/usr/local/bin/healthcheck.sh"]
ENTRYPOINT ["/usr/local/bin/platform.sh", "api"]
FROM runtime AS migrator
COPY --from=build /publish/migrator/ ./
ENTRYPOINT ["/usr/local/bin/platform.sh", "migrator"]
