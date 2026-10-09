# One image per service host (and the Gateway's siblings: Scheduler, Migrator). Build from the repository root:
#   docker build -f deploy/docker/service.Dockerfile \
#     --build-arg PROJECT=backend/src/Hosts/PurchaseRequest.Host/PurchaseRequest.Host.csproj \
#     --build-arg ASSEMBLY=Acme.PurchaseRequest.Host.dll -t acme-purchase-request .
#
# Base images are pinned by digest (update them with Dependabot, which reads this file), the final image runs as a non-root user,
# carries no build tools, and has a health check. Containers run in UTC; nothing is stored on their disk.
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:f62a272ac1b46e83f56b8ed0416572f31cd1128e2c4a5e63eb34d348e4a36095

FROM ${SDK_IMAGE} AS build
ARG PROJECT
WORKDIR /src
COPY backend/ backend/
RUN dotnet publish "${PROJECT}" --configuration Release --output /app /p:UseAppHost=false

FROM ${RUNTIME_IMAGE} AS runtime
ARG ASSEMBLY
ENV ASSEMBLY=${ASSEMBLY} \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    TZ=UTC
WORKDIR /app
COPY --from=build /app .
# The base image's non-root user.
USER app
EXPOSE 8080
# Liveness checks the process only (never a dependency); readiness is /health/ready.
HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 CMD wget -q -O /dev/null http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["sh", "-c", "exec dotnet \"$ASSEMBLY\""]
