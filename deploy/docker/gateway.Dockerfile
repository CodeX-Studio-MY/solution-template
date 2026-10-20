# The Gateway image also carries the built frontend (the shell and every module), which it serves like a CDN would:
# the shell at the root and each module under /mfe/{module-key}/{version}/. Build from the repository root:
#   docker build -f deploy/docker/gateway.Dockerfile -t farm360-gateway .
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:f62a272ac1b46e83f56b8ed0416572f31cd1128e2c4a5e63eb34d348e4a36095
ARG NODE_IMAGE=node:24-alpine@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1

FROM ${NODE_IMAGE} AS frontend
ARG MODULE_VERSION=0.0.0-dev
ENV MODULE_VERSION=${MODULE_VERSION}
WORKDIR /src
RUN corepack enable pnpm
COPY frontend/ frontend/
WORKDIR /src/frontend
RUN pnpm install --frozen-lockfile && pnpm build

FROM ${SDK_IMAGE} AS build
WORKDIR /src
COPY backend/ backend/
RUN dotnet publish backend/src/Gateway/Gateway.csproj --configuration Release --output /app /p:UseAppHost=false

FROM ${RUNTIME_IMAGE} AS runtime
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Bff__ShellRoot=/app/shell \
    Bff__ModulesRoot=/app/mfe \
    TZ=UTC
WORKDIR /app
COPY --from=build /app .
COPY --from=frontend /src/frontend/dist/shell shell
COPY --from=frontend /src/frontend/dist/mfe mfe
USER app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 CMD wget -q -O /dev/null http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Farm360.Gateway.dll"]
