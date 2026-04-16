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

# --- Stage 1: Extract FunctionsNetHost from NuGet ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS extract-nethost

WORKDIR /extract

# Download the .nupkg directly from the NuGet feed (it's just a zip file) and extract.
COPY NuGet.config .
RUN dotnet nuget list source --configfile NuGet.config --format short \
    | grep -oP 'https://\S+' | head -1 > /tmp/feed-url.txt

# Use dotnet restore with a minimal project to pull the package.
RUN echo '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.Azure.Functions.DotNetIsolatedNativeHost" Version="1.0.14" /></ItemGroup></Project>' > /extract/Extract.csproj \
    && dotnet restore Extract.csproj --configfile NuGet.config

RUN CONTENT_DIR=$(find /root/.nuget/packages/microsoft.azure.functions.dotnetisolatednativehost/1.0.14/contentFiles/any/any/workers/dotnet-isolated -maxdepth 0) \
    && cp "${CONTENT_DIR}/bin/FunctionsNetHost" /extract/ \
    && cp "${CONTENT_DIR}/bin/libnethost.so" /extract/ \
    && cp "${CONTENT_DIR}/worker.config.json" /extract/ \
    && cp -r "${CONTENT_DIR}/bin/prelaunchapps" /extract/

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
ENV FUNCTIONS_GRPC_HOST=localhost
ENV FUNCTIONS_GRPC_PORT=50052

ENTRYPOINT ["/bin/sh", "-c", \
    "exec /app/FunctionsNetHost FunctionsNetHost --functions-uri http://${FUNCTIONS_GRPC_HOST}:${FUNCTIONS_GRPC_PORT} --functions-request-id dev-request-id --functions-worker-id w_nethost01 --functions-grpc-max-message-length 134217728"]
