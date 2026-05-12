# SampleIsolatedApp + FunctionsNetHost
#
# Builds the sample .NET Isolated function app and packages it with
# FunctionsNetHost for local E2E testing. Customer code is pre-baked
# at /home/site/wwwroot — no external mount needed.
#
# Build context: repository root
#
# Usage:
#   docker build -f tools/ComputeSeparation/SampleIsolatedApp/Dockerfile -t sample-isolated-worker .
#   docker run -e FUNCTIONS_GRPC_HOST=<proxy-host> \
#              -e FUNCTIONS_GRPC_PORT=50052 \
#              sample-isolated-worker

# --- Stage 1: Build FunctionsNetHost from source ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS extract-nethost

WORKDIR /extract

# Build Linux FunctionsNetHost from source and stage the same files that the packaged native host provides.
RUN apt-get update \
    && apt-get install -y curl clang zlib1g-dev \
    && mkdir -p /tmp/azure-functions-dotnet-worker \
    && curl -fsSL "https://github.com/Azure/azure-functions-dotnet-worker/archive/05634f3c5a949c98c6b97b2c420e659303139387.tar.gz" \
        | tar -xz --strip-components=1 -C /tmp/azure-functions-dotnet-worker \
    && for tfm in net10.0 net9.0 net8.0; do \
        version="${tfm#net}"; \
        dotnet publish /tmp/azure-functions-dotnet-worker/host/src/PrelaunchApp/App.csproj -c Release -f "${tfm}" -p:UseAppHost=false -o "/extract/prelaunchapps/${version}"; \
    done \
    && dotnet publish /tmp/azure-functions-dotnet-worker/host/src/FunctionsNetHost/FunctionsNetHost.csproj -c Release -r linux-x64 -o /tmp/functionsnethost-linux \
    && cp /tmp/functionsnethost-linux/FunctionsNetHost /extract/ \
    && cp /tmp/functionsnethost-linux/libnethost.so /extract/ \
    && cp /tmp/azure-functions-dotnet-worker/host/tools/build/worker.config.json /extract/

# --- Stage 2: Publish sample isolated app ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish-app

WORKDIR /src
COPY tools/ComputeSeparation/SampleIsolatedApp/ ./SampleIsolatedApp/

RUN dotnet publish SampleIsolatedApp/SampleIsolatedApp.csproj \
    -c Release -o /app/publish

# --- Stage 3: Final image ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble

WORKDIR /app

COPY --from=extract-nethost /extract/FunctionsNetHost ./
COPY --from=extract-nethost /extract/libnethost.so ./
COPY --from=extract-nethost /extract/worker.config.json ./
COPY --from=extract-nethost /extract/prelaunchapps/ ./prelaunchapps/

# Sample app pre-baked at the standard customer code path.
COPY --from=publish-app /app/publish/ /home/site/wwwroot/

ENV HOME=/home
ENV FUNCTIONS_WORKER_RUNTIME_VERSION=10.0
ENV FUNCTIONS_GRPC_HOST=localhost
ENV FUNCTIONS_GRPC_PORT=50052

ENTRYPOINT ["/bin/sh", "-c", \
    "exec /app/FunctionsNetHost FunctionsNetHost --functions-uri http://${FUNCTIONS_GRPC_HOST}:${FUNCTIONS_GRPC_PORT} --functions-request-id dev-request-id --functions-worker-id w_nethost01 --functions-grpc-max-message-length 134217728"]
