# FunctionsNetHost (.NET Isolated Worker Host)
#
# Runs FunctionsNetHost — the native AOT bootstrap process that hosts
# .NET Isolated worker apps. Customer code is mounted at /home/site/wwwroot
# by the platform before calling /admin/worker/assign.
#
# Build context: repository root
#
# Usage:
#   docker build -f tools/ComputeSeparation/FunctionsNetHost/Dockerfile -t functions-nethost .
#   docker run -v /path/to/app:/home/site/wwwroot \
#              -e FUNCTIONS_GRPC_HOST=<proxy-host> \
#              -e FUNCTIONS_GRPC_PORT=50054 \
#              functions-nethost

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS extract-nethost

WORKDIR /extract

# Build Linux FunctionsNetHost from source and stage the same files that the packaged native host provides.
RUN apt-get update \
    && apt-get install -y curl clang zlib1g-dev \
    && mkdir -p /tmp/azure-functions-dotnet-worker \
    && curl -fsSL "https://github.com/Azure/azure-functions-dotnet-worker/archive/f17863bb9dd53485fa613964b70ae997f2e430e9.tar.gz" \
        | tar -xz --strip-components=1 -C /tmp/azure-functions-dotnet-worker \
    && for tfm in net10.0 net9.0 net8.0; do \
        version="${tfm#net}"; \
        dotnet publish /tmp/azure-functions-dotnet-worker/host/src/PrelaunchApp/App.csproj -c Release -f "${tfm}" -p:UseAppHost=false -o "/extract/prelaunchapps/${version}"; \
    done \
    && dotnet publish /tmp/azure-functions-dotnet-worker/host/src/FunctionsNetHost/FunctionsNetHost.csproj -c Release -r linux-x64 -o /tmp/functionsnethost-linux \
    && cp /tmp/functionsnethost-linux/FunctionsNetHost /extract/ \
    && cp /tmp/functionsnethost-linux/libnethost.so /extract/ \
    && cp /tmp/azure-functions-dotnet-worker/host/tools/build/worker.config.json /extract/

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble

WORKDIR /app

COPY --from=extract-nethost /extract/FunctionsNetHost ./
COPY --from=extract-nethost /extract/libnethost.so ./
COPY --from=extract-nethost /extract/worker.config.json ./
COPY --from=extract-nethost /extract/prelaunchapps/ ./prelaunchapps/

# Pre-create the customer code directory (platform mounts content here).
RUN mkdir -p /home/site/wwwroot

ENV HOME=/home
ENV FUNCTIONS_WORKER_RUNTIME_VERSION=10.0
ENV FUNCTIONS_GRPC_HOST=localhost
# Worker-proxy's worker-facing gRPC listener defaults to 50054 (WORKER_GRPC_PORT).
# FunctionsNetHost dials this endpoint to register as the language worker.
ENV FUNCTIONS_GRPC_PORT=50054

ENTRYPOINT ["/bin/sh", "-c", \
    "exec /app/FunctionsNetHost FunctionsNetHost --functions-uri http://${FUNCTIONS_GRPC_HOST}:${FUNCTIONS_GRPC_PORT} --functions-request-id dev-request-id --functions-worker-id w_nethost01 --functions-grpc-max-message-length 134217728"]
